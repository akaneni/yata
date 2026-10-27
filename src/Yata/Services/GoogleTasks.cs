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

public sealed record TaskListInfo(string Id, string Name);

/// <summary>
/// Two-way sync between the Tasks tab and one Google Tasks list. Changes made here go out a moment
/// later; Google's side is read every couple of minutes. Local-only tasks stay local when it's off.
/// </summary>
public sealed class GoogleTasks
{
    private const string Api = "https://tasks.googleapis.com/tasks/v1/";
    private static readonly TimeSpan SyncEvery = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan AfterLocalChange = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    private readonly AppStore _store;
    private readonly GoogleAccount _account;
    private readonly TaskList _tasks;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private DateTime _nextSync = DateTime.MinValue;
    private bool _syncing;

    public IReadOnlyList<TaskListInfo> Lists { get; private set; } = Array.Empty<TaskListInfo>();
    /// <summary>Last problem worth showing (offline, API off, ...), or null.</summary>
    public string? Error { get; private set; }

    public event Action? Changed;

    public GoogleTasks(AppStore store, GoogleAccount account, TaskList tasks)
    {
        _store = store;
        _account = account;
        _tasks = tasks;
        _timer.Tick += (_, _) => Tick();
        tasks.LocalChanged += () => _nextSync = Min(_nextSync, DateTime.Now + AfterLocalChange);
        account.Changed += OnAccountChanged;
    }

    private SettingsData Settings => _store.Settings;

    /// <summary>The account is connected and allows tasks.</summary>
    public bool HasAccess => _account.Has(GoogleAuth.TasksScopes);

    public bool IsOn => Settings.TasksSyncEnabled && HasAccess;

    public void Start()
    {
        _timer.Start();
        Tick();
    }

    public void Stop() => _timer.Stop();

    /// <summary>Turns sync on, asking Google for the tasks permission first if needed.</summary>
    public async Task EnableAsync()
    {
        if (!HasAccess && !await _account.ConnectAsync(GoogleAuth.TasksScopes,
                Strings.Tasks_AskPermission))
        {
            Changed?.Invoke();
            return;
        }
        Settings.TasksSyncEnabled = true;
        _store.SaveSoon();
        SyncSoon();
    }

    /// <summary>Stops syncing. The mirrored tasks leave the tab (they're still in Google).</summary>
    public void Disable()
    {
        Settings.TasksSyncEnabled = false;
        _store.SaveSoon();
        _tasks.DropSynced();
        Error = null;
        Changed?.Invoke();
    }

    public void SetList(TaskListInfo list)
    {
        if (list.Id == Settings.TasksListId)
            return;
        // The tab mirrors one list: the old one's tasks go, local-only ones are sent to the new one.
        _tasks.DropSynced();
        Settings.TasksListId = list.Id;
        Settings.TasksListName = list.Name;
        _store.SaveSoon();
        Changed?.Invoke();
        SyncSoon();
    }

    private void SyncSoon()
    {
        _nextSync = DateTime.MinValue;
        Tick();
    }

    private void OnAccountChanged()
    {
        if (_account.Status == GoogleAccountStatus.SignedOut && Settings.TasksSyncEnabled)
        {
            // Disconnected: forget what came from the account.
            Settings.TasksSyncEnabled = false;
            Settings.TasksListId = null;
            Settings.TasksListName = null;
            _store.SaveSoon();
            _tasks.DropSynced();
            Lists = Array.Empty<TaskListInfo>();
        }
        Error = null;
        Changed?.Invoke();
        if (IsOn)
            SyncSoon();
    }

    private void Tick()
    {
        if (IsOn && !_syncing && DateTime.Now >= _nextSync)
            _ = SyncAsync();
    }

    private async Task SyncAsync()
    {
        _syncing = true;
        _nextSync = DateTime.Now + RetryAfter;
        try
        {
            await LoadListsAsync();
            string list = Settings.TasksListId!;
            await PushAsync(list);
            var remote = await PullAsync(list);
            if (!IsOn || list != Settings.TasksListId)
                return; // Turned off or switched lists meanwhile.
            _tasks.ApplyRemote(remote);
            // Changes made while this ran go out in a moment; otherwise wait for the next round.
            _nextSync = _tasks.Items.Any(t => t.Dirty || (t.GoogleId == null && !t.Done)) || _tasks.PendingDeletes.Count > 0
                ? DateTime.Now + AfterLocalChange
                : DateTime.Now + SyncEvery;
            Error = null;
        }
        catch (GoogleAuthException ex)
        {
            Log.Warn("google-tasks", "Acesso ao Google recusado", ex);
            Error = ex.Message;
            _account.Lost(ex);
        }
        catch (GoogleApiException ex) when (ex.ApiDisabled)
        {
            Log.Warn("google-tasks", "API do Google Tasks desativada no projeto", ex);
            Error = Strings.Tasks_ApiDisabled;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Offline: changes stay marked and go out on a later round.
            Log.Warn("google-tasks", "Falha ao sincronizar", ex);
            Error = Strings.Tasks_NoConnection;
        }
        finally
        {
            _syncing = false;
            Changed?.Invoke();
        }
    }

    private async Task LoadListsAsync()
    {
        using var json = JsonDocument.Parse(await SendAsync(HttpMethod.Get, "users/@me/lists?maxResults=100&fields=items(id,title)"));
        Lists = TaskSync.ParseLists(json.RootElement);
        if (Lists.Count == 0)
            throw new HttpRequestException("Nenhuma lista no Google Tasks.");

        // First run picks the default list (Google returns it first); a deleted list falls back to it.
        if (Lists.FirstOrDefault(l => l.Id == Settings.TasksListId) is not { } current)
        {
            if (Settings.TasksListId != null)
                _tasks.DropSynced();
            current = Lists[0];
            Settings.TasksListId = current.Id;
        }
        Settings.TasksListName = current.Name;
        _store.SaveSoon();
    }

    private async Task PushAsync(string list)
    {
        string tasksPath = $"lists/{Uri.EscapeDataString(list)}/tasks";

        foreach (string id in _tasks.PendingDeletes.ToList())
        {
            try
            {
                await SendAsync(HttpMethod.Delete, $"{tasksPath}/{Uri.EscapeDataString(id)}");
            }
            catch (GoogleApiException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                // Already gone.
            }
            _tasks.Deleted(id);
        }

        // Done tasks that never reached Google stay local: no point filling the list with old history.
        foreach (var item in _tasks.Items.Where(t => t.Dirty || (t.GoogleId == null && !t.Done)).ToList())
        {
            int version = item.Version;
            string body = TaskSync.Body(item);
            string? created;
            if (item.GoogleId == null)
            {
                created = await SendAsync(HttpMethod.Post, $"{tasksPath}?fields=id", body);
            }
            else
            {
                try
                {
                    created = await SendAsync(HttpMethod.Patch, $"{tasksPath}/{Uri.EscapeDataString(item.GoogleId)}?fields=id", body);
                }
                catch (GoogleApiException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    // Deleted in Google before this change got there: the deletion wins.
                    _tasks.Items.Remove(item);
                    _tasks.Save();
                    continue;
                }
            }
            using var json = JsonDocument.Parse(created);
            _tasks.Sent(item, json.RootElement.GetProperty("id").GetString()!, version);
        }
    }

    /// <summary>Open tasks plus the ones completed today, like the tab shows them.</summary>
    private async Task<List<RemoteTask>> PullAsync(string list)
    {
        const string Fields = "&fields=nextPageToken,items(id,title,status,due,deleted)";
        string path = $"lists/{Uri.EscapeDataString(list)}/tasks?maxResults=100";
        string today = Uri.EscapeDataString(new DateTimeOffset(DateTime.Today).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture));

        var tasks = await PullPagesAsync(path + "&showCompleted=false" + Fields);
        // Completed in Google's own apps also means hidden, hence showHidden.
        tasks.AddRange((await PullPagesAsync(path + $"&showCompleted=true&showHidden=true&completedMin={today}" + Fields))
            .Where(t => t.Done));
        return tasks;
    }

    private async Task<List<RemoteTask>> PullPagesAsync(string path)
    {
        var tasks = new List<RemoteTask>();
        string? page = null;
        // A safety cap: 10 pages of 100 is far beyond a list anyone reads in a notch.
        for (int i = 0; i < 10; i++)
        {
            string url = page == null ? path : path + "&pageToken=" + Uri.EscapeDataString(page);
            using var json = JsonDocument.Parse(await SendAsync(HttpMethod.Get, url));
            if (json.RootElement.TryGetProperty("items", out var items))
                tasks.AddRange(items.EnumerateArray().Select(TaskSync.Parse).OfType<RemoteTask>());
            page = json.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
            if (page == null)
                break;
        }
        return tasks;
    }

    private Task<string> SendAsync(HttpMethod method, string path, string? body = null) =>
        _account.Auth.SendAsync(method, Api + path, body, CancellationToken.None);

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}

/// <summary>Google Tasks JSON, in and out.</summary>
internal static class TaskSync
{
    public static RemoteTask? Parse(JsonElement item)
    {
        string? id = Str(item, "id");
        string title = (Str(item, "title") ?? "").Trim();
        // Blank tasks are what Google's apps leave behind when you tap "add" and walk away.
        if (id == null || title.Length == 0 || (item.TryGetProperty("deleted", out var d) && d.ValueKind == JsonValueKind.True))
            return null;
        return new RemoteTask(id, title, Str(item, "status") == "completed", ParseDue(Str(item, "due")));
    }

    /// <summary>"2026-10-28T00:00:00.000Z": the day is what counts, whatever the time zone says.</summary>
    public static DateTime? ParseDue(string? due) =>
        due is { Length: >= 10 } && DateTime.TryParseExact(due[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var day) ? day : null;

    public static List<TaskListInfo> ParseLists(JsonElement root)
    {
        var lists = new List<TaskListInfo>();
        if (root.TryGetProperty("items", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                if (Str(item, "id") is { } id)
                    lists.Add(new TaskListInfo(id, Str(item, "title") is { Length: > 0 } title ? title : Strings.Tasks_Unnamed));
            }
        }
        return lists;
    }

    /// <summary>What a task looks like to Google. Only the day of the due goes; the time stays here.</summary>
    public static string Body(TaskItem item)
    {
        var body = new Dictionary<string, object?>
        {
            ["title"] = item.Text,
            ["status"] = item.Done ? "completed" : "needsAction",
            ["due"] = item.Due is DateTime due ? due.ToString("yyyy-MM-dd'T'00:00:00.000'Z'", CultureInfo.InvariantCulture) : null,
        };
        // Reopening needs the completion date cleared explicitly; completing lets Google stamp it.
        if (!item.Done)
            body["completed"] = null;
        return JsonSerializer.Serialize(body);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
