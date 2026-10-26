using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly AppStore _store;
    private readonly GoogleAuth _auth = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(20) };
    private readonly HashSet<string> _sent = new();
    private DateTime _nextFetch = DateTime.MinValue;
    private DateTime _fetchedDay;
    private bool _fetching;
    private CancellationTokenSource? _signIn;
    private bool _signInCancelled;

    public IReadOnlyList<CalendarEvent> Events { get; private set; } = Array.Empty<CalendarEvent>();
    public IReadOnlyList<CalendarInfo> Calendars { get; private set; } = Array.Empty<CalendarInfo>();
    public CalendarStatus Status { get; private set; }
    public string? Email => _auth.Email;
    /// <summary>Last problem worth showing (offline, sign-in cancelled, ...), or null.</summary>
    public string? Error { get; private set; }

    public event Action? Changed;
    public event Action<HudMessage>? Message;

    public GoogleCalendar(AppStore store)
    {
        _store = store;
        _timer.Tick += (_, _) => Tick();
        Status = !_auth.IsConfigured ? CalendarStatus.NotConfigured
               : _auth.IsSignedIn ? CalendarStatus.Connected
               : CalendarStatus.SignedOut;
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
        CancelConnect();
    }

    public async Task ConnectAsync()
    {
        if (Status is CalendarStatus.NotConfigured or CalendarStatus.Connecting)
            return;

        var cts = _signIn = new CancellationTokenSource();
        _signInCancelled = false;
        SetStatus(CalendarStatus.Connecting, null);
        try
        {
            await _auth.SignInAsync(cts.Token);
            Events = Array.Empty<CalendarEvent>();
            SetStatus(CalendarStatus.Connected, null);
            await RefreshAsync();
        }
        catch (OperationCanceledException)
        {
            SetStatus(SignedOutStatus, _signInCancelled ? null : "Tempo esgotado esperando o navegador.");
        }
        catch (GoogleAuthException ex)
        {
            SetStatus(SignedOutStatus, ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Net.Sockets.SocketException)
        {
            SetStatus(SignedOutStatus, "Sem conexão com o Google.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            SetStatus(SignedOutStatus, "Não foi possível abrir o navegador.");
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or
                                   System.Security.Cryptography.CryptographicException)
        {
            SetStatus(SignedOutStatus, "Não foi possível guardar o acesso neste PC.");
        }
        finally
        {
            if (_signIn == cts)
                _signIn = null;
            cts.Dispose();
        }
    }

    public void CancelConnect()
    {
        _signInCancelled = true;
        _signIn?.Cancel();
    }

    public async Task DisconnectAsync()
    {
        CancelConnect();
        Events = Array.Empty<CalendarEvent>();
        Calendars = Array.Empty<CalendarInfo>();
        _sent.Clear();
        Settings.CalendarIds = null;
        _store.SaveSoon();
        SetStatus(CalendarStatus.SignedOut, null);
        await _auth.SignOutAsync();
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

    private CalendarStatus SignedOutStatus => _auth.NeedsReconnect ? CalendarStatus.NeedsReconnect : CalendarStatus.SignedOut;

    private void Tick()
    {
        var now = DateTime.Now;
        if (Status == CalendarStatus.Connected && (now >= _nextFetch || now.Date != _fetchedDay))
            _ = RefreshAsync();
        CheckReminders(now);
    }

    public async Task RefreshAsync()
    {
        if (_fetching || Status != CalendarStatus.Connected)
            return;

        _fetching = true;
        _nextFetch = DateTime.Now + RetryAfter;
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
            Error = null;
            Changed?.Invoke();
            CheckReminders(DateTime.Now);
        }
        catch (GoogleAuthException ex)
        {
            SetStatus(ex.NeedsReconnect ? CalendarStatus.NeedsReconnect : Status, ex.Message);
        }
        catch (CalendarApiDisabledException ex)
        {
            Error = ex.Message;
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Offline or a hiccup: keep showing what we had and retry soon.
            Error = "Sem conexão com o Google Agenda.";
            Changed?.Invoke();
        }
        finally
        {
            _fetching = false;
        }
    }

    private void CheckReminders(DateTime now)
    {
        var lead = TimeSpan.FromMinutes(Math.Max(0, Settings.CalendarReminderMinutes));
        foreach (var (e, starting) in CalendarReminders.Due(Events, now, lead, _sent))
        {
            Chime.Play();
            Message?.Invoke(new HudMessage(Glyphs.Calendar, e.Title,
                starting ? "agora" : CalendarReminders.Until(e.Start, now), DurationMs: 6000, Important: true));
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
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            // A calendar that went away or lost sharing shouldn't hide the others.
            return new List<CalendarEvent>();
        }
    }

    /// <summary>GET with the access token; a rejected token is refreshed once and the request retried.</summary>
    private async Task<JsonDocument> GetJsonAsync(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            string token = await _auth.GetAccessTokenAsync(forceRefresh: attempt > 0, CancellationToken.None);
            using var request = new HttpRequestMessage(HttpMethod.Get, Api + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await Http.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                continue;

            string body = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
                return JsonDocument.Parse(body);

            // The Calendar API is switched off in the Cloud project: a setup problem, not a calendar one.
            if (body.Contains("accessNotConfigured", StringComparison.Ordinal) ||
                body.Contains("SERVICE_DISABLED", StringComparison.Ordinal))
                throw new CalendarApiDisabledException();
            throw new HttpRequestException($"Google Agenda respondeu {(int)response.StatusCode}.", null, response.StatusCode);
        }
    }

    private void SetStatus(CalendarStatus status, string? error)
    {
        Status = status;
        Error = error;
        Changed?.Invoke();
    }

    private static string Rfc3339(DateTime local) =>
        new DateTimeOffset(local).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}

/// <summary>The Calendar API is switched off in the Google Cloud project behind this build.</summary>
public sealed class CalendarApiDisabledException : Exception
{
    public CalendarApiDisabledException() : base("A API do Google Agenda não está ativada no projeto do Google Cloud.")
    {
    }
}
