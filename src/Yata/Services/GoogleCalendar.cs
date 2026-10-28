using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Yata.Helpers;

namespace Yata.Services;

public enum CalendarStatus { NotConfigured, SignedOut, Connecting, Connected, NeedsReconnect }

/// <summary>
/// Today's and tomorrow's events from Google Calendar (read-only), refreshed every few minutes,
/// with notices before and when each one starts. Events are kept in memory only.
/// </summary>
public sealed class GoogleCalendar
{
    private const string Api = "https://www.googleapis.com/calendar/v3/";
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(5);
    // After a failure, try again sooner than the normal cadence but without hammering.
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    private readonly AppStore _store;
    private readonly GoogleAccount _account;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(20) };
    private readonly HashSet<string> _sent = new();
    private DateTime _nextFetch = DateTime.MinValue;
    private DateTime _fetchedDay;
    // The new day already got its immediate fetch; failures from then on wait RetryAfter like any other.
    private DateTime _dayPrompted;
    private bool _fetching;
    private string? _error;

    public IReadOnlyList<CalendarEvent> Events { get; private set; } = Array.Empty<CalendarEvent>();
    public IReadOnlyList<CalendarInfo> Calendars { get; private set; } = Array.Empty<CalendarInfo>();
    public string? Email => _account.Email;

    /// <summary>The account's state, narrowed to whether it grants calendar access.</summary>
    public CalendarStatus Status => _account.Status switch
    {
        GoogleAccountStatus.NotConfigured => CalendarStatus.NotConfigured,
        GoogleAccountStatus.Connecting => CalendarStatus.Connecting,
        GoogleAccountStatus.NeedsReconnect => CalendarStatus.NeedsReconnect,
        GoogleAccountStatus.Connected when _account.Has(GoogleAuth.CalendarScopes) => CalendarStatus.Connected,
        _ => CalendarStatus.SignedOut,
    };

    /// <summary>Last problem worth showing (offline, sign-in cancelled, ...), or null.</summary>
    public string? Error => Status == CalendarStatus.Connected ? _error : _account.Error;

    public event Action? Changed;
    public event Action<HudMessage>? Message;
    /// <summary>A read from Google started or ended (the ↻ spins meanwhile).</summary>
    public event Action? BusyChanged;

    private readonly ManualGate _manual = new();

    /// <summary>When the events were last read from Google.</summary>
    public DateTime? LastFetched { get; private set; }

    public bool IsFetching => _fetching;

    /// <summary>"Sync now" or the ↻ in the Agenda tab: reads Google at once, unless it just did.</summary>
    public Task RefreshNowAsync() =>
        Status == CalendarStatus.Connected && _manual.TryPass() ? RefreshAsync() : Task.CompletedTask;

    public GoogleCalendar(AppStore store, GoogleAccount account)
    {
        _store = store;
        _account = account;
        _timer.Tick += (_, _) => Tick();
        account.Changed += OnAccountChanged;
    }

    private SettingsData Settings => _store.Settings;

    public void Start()
    {
        _timer.Start();
        Tick();
    }

    public void Stop()
    {
        _timer.Stop();
        _account.CancelConnect();
    }

    /// <summary>Signs in, or adds the calendar permission to an account connected for tasks.</summary>
    public async Task ConnectAsync()
    {
        if (await _account.ConnectAsync(GoogleAuth.CalendarScopes,
                Strings.Calendar_AskPermission))
            await RefreshAsync();
    }

    public void CancelConnect() => _account.CancelConnect();

    public Task DisconnectAsync() => _account.DisconnectAsync();

    private void OnAccountChanged()
    {
        if (_account.Status == GoogleAccountStatus.SignedOut)
        {
            // Disconnected: forget everything that came from the account.
            Events = Array.Empty<CalendarEvent>();
            Calendars = Array.Empty<CalendarInfo>();
            _sent.Clear();
            if (Settings.CalendarIds != null)
            {
                Settings.CalendarIds = null;
                _store.SaveSoon();
            }
        }
        else if (Status == CalendarStatus.Connected && Calendars.Count == 0)
        {
            _ = RefreshAsync();
        }
        Changed?.Invoke();
    }

    /// <summary>Includes or leaves out one calendar, then reloads.</summary>
    public void SetIncluded(string calendarId, bool included)
    {
        // The first change turns "whatever Google shows" into an explicit list.
        var ids = Settings.CalendarIds ?? Calendars.Where(IsIncluded).Select(c => c.Id).ToList();
        ids.Remove(calendarId);
        if (included)
            ids.Add(calendarId);
        Settings.CalendarIds = ids;
        _store.SaveSoon();
        _ = RefreshAsync();
    }

    /// <summary>Until the user picks, the calendars ticked in Google Calendar itself.</summary>
    public bool IsIncluded(CalendarInfo calendar) =>
        Settings.CalendarIds?.Contains(calendar.Id) ?? (calendar.SelectedInGoogle || calendar.Primary);

    /// <summary>
    /// The PC woke up or the network came back: fetch at the next tick (within 20 s, by when the
    /// connection has usually settled) instead of at the next round.
    /// </summary>
    public void Wake() => _nextFetch = DateTime.MinValue;

    private void Tick()
    {
        var now = DateTime.Now;
        if (now.Date != _fetchedDay && now.Date != _dayPrompted)
        {
            _dayPrompted = now.Date;
            _nextFetch = now;
        }
        if (Status == CalendarStatus.Connected && now >= _nextFetch)
            _ = RefreshAsync();
        CheckReminders(now);
    }

    public async Task RefreshAsync()
    {
        if (_fetching || Status != CalendarStatus.Connected)
            return;

        _fetching = true;
        _nextFetch = DateTime.Now + RetryAfter;
        BusyChanged?.Invoke();
        try
        {
            var calendars = await FetchCalendarsAsync();
            var today = DateTime.Today;
            var events = new List<CalendarEvent>();
            foreach (var calendar in calendars.Where(IsIncluded))
                events.AddRange(await FetchEventsAsync(calendar.Id, today, today.AddDays(2)));

            if (Status != CalendarStatus.Connected)
                return; // Disconnected while fetching.

            Calendars = calendars;
            // The same meeting shows up in every calendar it's on.
            Events = events.GroupBy(e => e.Key).Select(g => g.First()).OrderBy(e => e.Start).ThenBy(e => e.Title).ToList();
            var keys = Events.Select(e => e.Key).ToHashSet();
            _sent.RemoveWhere(k => !keys.Contains(k[..k.LastIndexOf('|')]));
            _fetchedDay = today;
            _nextFetch = DateTime.Now + RefreshEvery;
            LastFetched = DateTime.Now;
            _error = null;
            Changed?.Invoke();
            CheckReminders(DateTime.Now);
        }
        catch (GoogleAuthException ex)
        {
            Log.Warn("calendar", "Acesso ao Google recusado", ex);
            _error = ex.Message;
            _account.Lost(ex);
            Changed?.Invoke();
        }
        catch (GoogleApiException ex) when (ex.ApiDisabled)
        {
            Log.Warn("calendar", "API do Google Agenda desativada no projeto", ex);
            _error = Strings.Calendar_ApiDisabled;
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Offline or a hiccup: keep showing what we had and retry soon.
            Log.Warn("calendar", "Falha ao buscar eventos", ex);
            _error = Strings.Calendar_NoConnection;
            Changed?.Invoke();
        }
        finally
        {
            _fetching = false;
            BusyChanged?.Invoke();
        }
    }

    private void CheckReminders(DateTime now)
    {
        var lead = TimeSpan.FromMinutes(Math.Max(0, Settings.CalendarReminderMinutes));
        foreach (var (e, starting) in CalendarReminders.Due(Events, now, lead, _sent))
        {
            // "In 5 min" is only true until it starts, and "now" until it ends; "now" replaces the
            // "in 5 min" still waiting.
            Message?.Invoke(new HudMessage(Glyphs.Calendar, e.Title,
                starting ? Strings.Calendar_Now : CalendarReminders.Until(e.Start, now), DurationMs: 6000, Important: true,
                Tag: "calendar|" + e.Key, Chime: true, Expires: starting ? e.End : e.Start));
        }
    }

    private async Task<List<CalendarInfo>> FetchCalendarsAsync()
    {
        using var json = await GetJsonAsync("users/me/calendarList?maxResults=250&fields=items(id,summary,summaryOverride,selected,primary)");
        var list = new List<CalendarInfo>();
        if (!json.RootElement.TryGetProperty("items", out var items))
            return list;
        foreach (var item in items.EnumerateArray())
        {
            string? id = Str(item, "id");
            if (id == null)
                continue;
            list.Add(new CalendarInfo(id, Str(item, "summaryOverride") ?? Str(item, "summary") ?? id,
                Bool(item, "selected"), Bool(item, "primary")));
        }
        // Primary first, then by name, as in Google Calendar's sidebar.
        return list.OrderByDescending(c => c.Primary).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private async Task<List<CalendarEvent>> FetchEventsAsync(string calendarId, DateTime from, DateTime to)
    {
        string query =
            $"calendars/{Uri.EscapeDataString(calendarId)}/events?singleEvents=true&orderBy=startTime&maxResults=100" +
            $"&timeMin={Uri.EscapeDataString(Rfc3339(from))}&timeMax={Uri.EscapeDataString(Rfc3339(to))}" +
            "&fields=items(id,iCalUID,status,eventType,summary,start,end,hangoutLink,location,description," +
            "conferenceData(entryPoints(entryPointType,uri)),attendees(self,responseStatus))";
        try
        {
            using var json = await GetJsonAsync(query);
            var events = new List<CalendarEvent>();
            if (json.RootElement.TryGetProperty("items", out var items))
                events.AddRange(items.EnumerateArray().Select(CalendarEvents.Parse).OfType<CalendarEvent>());
            return events;
        }
        catch (GoogleApiException ex) when (!ex.ApiDisabled && ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            // A calendar that went away or lost sharing shouldn't hide the others.
            return new List<CalendarEvent>();
        }
    }

    private async Task<JsonDocument> GetJsonAsync(string path) =>
        JsonDocument.Parse(await _account.Auth.SendAsync(HttpMethod.Get, Api + path, null, CancellationToken.None));

    private static string Rfc3339(DateTime local) =>
        new DateTimeOffset(local).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
