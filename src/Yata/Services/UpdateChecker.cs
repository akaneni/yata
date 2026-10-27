using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;
using Yata.Helpers;

namespace Yata.Services;

/// <summary>
/// Opt-in check for a newer release: at most once a day, one anonymous request to GitHub's
/// public releases API. Nothing about the PC or its use is sent. Installing happens only when the user
/// clicks "Update and restart" (UpdateInstaller).
/// </summary>
public sealed class UpdateChecker
{
    public const string ReleasesPage = "https://github.com/akaneni/yata/releases/latest";
    internal const string LatestApi = "https://api.github.com/repos/akaneni/yata/releases/latest";
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    private static readonly HttpClient Http = CreateClient();

    private readonly AppStore _store;
    private readonly Func<DateTime> _clock;
    // First look a minute after startup, then hourly to see whether a day has passed.
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };
    private bool _checking;

    public event Action<HudMessage>? Message;
    /// <summary>A newer version became known (or stopped being newer).</summary>
    public event Action? Changed;

    public UpdateChecker(AppStore store, Func<DateTime>? clock = null)
    {
        _store = store;
        _clock = clock ?? (() => DateTime.Now);
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = TimeSpan.FromHours(1);
            await CheckIfDueAsync();
        };
    }

    public static Version Current { get; } =
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? new Version(v.Major, v.Minor, Math.Max(v.Build, 0)) : new Version(0, 1, 0);

    /// <summary>Newer published version, if the last check found one.</summary>
    public string? Available => IsNewer(Data.Latest, Current) ? Data.Latest : null;

    private UpdateData Data => _store.Data.Updates;

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    /// <summary>"v0.2.0" or "0.2" newer than <paramref name="current"/>? Unparseable tags never are.</summary>
    internal static bool IsNewer(string? tag, Version current)
    {
        if (string.IsNullOrWhiteSpace(tag) || !Version.TryParse(tag.Trim().TrimStart('v', 'V'), out var version))
            return false;
        var normalized = new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
        return normalized > current;
    }

    internal async Task CheckIfDueAsync()
    {
        if (!_store.Settings.UpdateCheckEnabled || _checking)
            return;
        var now = _clock();
        if (Data.LastCheck is DateTime last && now - last < Interval && now >= last)
            return;

        _checking = true;
        try
        {
            string? latest = await FetchLatestAsync();
            Data.LastCheck = now;
            if (latest != null)
                Data.Latest = latest.Trim().TrimStart('v', 'V');
            _store.SaveSoon();
            Announce();
            Changed?.Invoke();
        }
        finally
        {
            _checking = false;
        }
    }

    /// <summary>Tells about a new version once; the settings window keeps showing it.</summary>
    internal void Announce()
    {
        if (Available is not string version || Data.Announced == version)
            return;
        Data.Announced = version;
        _store.SaveSoon();
        Message?.Invoke(new HudMessage(Glyphs.Bell, Strings.Hud_NewVersion, version, DurationMs: 5000, Important: true));
    }

    private static async Task<string?> FetchLatestAsync()
    {
        try
        {
            using var response = await Http.GetAsync(LatestApi);
            if (!response.IsSuccessStatusCode)
                return null; // 404 until the first release; rate limits; offline.
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Log.Info("updates", $"Não foi possível consultar a versão nova ({ex.GetType().Name})");
            return null;
        }
    }

    internal static HttpClient CreateClient()
    {
        var client = HttpPool.Client(TimeSpan.FromSeconds(10));
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
