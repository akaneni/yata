using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using Yata.Helpers;

namespace Yata.Services;

public sealed class StoreData
{
    public SettingsData Settings { get; set; } = new();
    public WaterData Water { get; set; } = new();
    public List<TaskData> Tasks { get; set; } = new();
    /// <summary>Google Tasks ids removed here while offline, still to be deleted in Google.</summary>
    public List<string> TaskDeletes { get; set; } = new();
    /// <summary>Paths parked on the shelf (references only, files are never copied).</summary>
    public List<string> Shelf { get; set; } = new();
    public UpdateData Updates { get; set; } = new();
}

/// <summary>What the (opt-in) update check last saw.</summary>
public sealed class UpdateData
{
    public DateTime? LastCheck { get; set; }
    /// <summary>Newest version published, e.g. "0.2.0".</summary>
    public string? Latest { get; set; }
    /// <summary>Version already announced in the notch, so it's shown once.</summary>
    public string? Announced { get; set; }
}

/// <summary>User preferences. "Start with Windows" isn't here: the registry is its source of truth.</summary>
public sealed class SettingsData
{
    // General
    /// <summary>"en" or "pt-BR" (see Lang); null follows the Windows language.</summary>
    public string? Language { get; set; }
    public bool HideInFullscreen { get; set; } = true;
    /// <summary>Screen captures, recordings and screen sharing see through the notch (you still see it).</summary>
    public bool HideFromCapture { get; set; } = true;
    public int HoverDelayMs { get; set; } = 140;
    public bool UseArtworkAccent { get; set; } = true;
    /// <summary>Display the notch sits on (Windows device name); null follows the primary display.</summary>
    public string? Monitor { get; set; }
    /// <summary>Ctrl+Alt+Y opens and closes the notch.</summary>
    public bool HotkeyEnabled { get; set; } = true;
    /// <summary>Ctrl+Alt+J goes to the window of the activity that needs you. Off unless the user opts in.</summary>
    public bool JumpHotkeyEnabled { get; set; }

    // Media
    public bool LyricsEnabled { get; set; } = true;

    // Updates: off unless the user opts in, since it's a network request.
    public bool UpdateCheckEnabled { get; set; }

    // Calendar (Google)
    /// <summary>Notice this long before an event; 0 = only when it starts.</summary>
    public int CalendarReminderMinutes { get; set; } = 5;
    /// <summary>The closed notch shows the next event in the minutes before it.</summary>
    public bool CalendarInCompact { get; set; } = true;
    /// <summary>Calendars to show; null = the ones ticked in Google Calendar itself.</summary>
    public List<string>? CalendarIds { get; set; }

    // Tasks (Google Tasks, two-way)
    public bool TasksSyncEnabled { get; set; }
    /// <summary>Google task list mirrored in the Tasks tab; null = the account's default list.</summary>
    public string? TasksListId { get; set; }
    public string? TasksListName { get; set; }

    // Weather (Open-Meteo)
    public bool WeatherEnabled { get; set; }
    /// <summary>"Campinas, São Paulo" — just for display.</summary>
    public string? WeatherPlace { get; set; }
    /// <summary>Stored already rounded to ~1 km; nothing more precise is kept or sent.</summary>
    public double? WeatherLatitude { get; set; }
    public double? WeatherLongitude { get; set; }
    public bool WeatherRainAlerts { get; set; } = true;

    // Notices
    /// <summary>The chime that comes with reminders.</summary>
    public bool SoundsEnabled { get; set; } = true;
    /// <summary>No chime while the microphone is in use (a call).</summary>
    public bool QuietDuringCalls { get; set; } = true;
    public bool HudVolume { get; set; } = true;
    public bool HudBrightness { get; set; } = true;
    public bool HudPower { get; set; } = true;
    public bool HudAudioDevice { get; set; } = true;
    public bool HudCapsLock { get; set; } = true;
    public bool HudKeyboardLayout { get; set; } = true;
    public bool HudNetwork { get; set; } = true;
    public bool HudPrivacy { get; set; } = true;

    // Activities: other programs on this PC (scripts, builds, AI agents) showing progress in the
    // notch through a local pipe (yata-cli activity). Nothing goes over the network.
    public bool ActivitiesEnabled { get; set; } = true;

    // Focus
    public int FocusMinutes { get; set; } = 25;
    public int ShortBreakMinutes { get; set; } = 5;
    public int LongBreakMinutes { get; set; } = 15;
    public int SessionsPerCycle { get; set; } = 4;
    public bool PauseFocusWhenAway { get; set; } = true;

    // Yata
    public bool CompanionEnabled { get; set; } = true;
    public int CompanionIdleMinutes { get; set; } = 2;
    public int CompanionSleepMinutes { get; set; } = 10;
    public bool AwaySummaryEnabled { get; set; } = true;

    public bool Shows(HudKind kind) => kind switch
    {
        HudKind.Volume => HudVolume,
        HudKind.Brightness => HudBrightness,
        HudKind.Power => HudPower,
        HudKind.AudioDevice => HudAudioDevice,
        HudKind.CapsLock => HudCapsLock,
        HudKind.KeyboardLayout => HudKeyboardLayout,
        HudKind.Network => HudNetwork,
        HudKind.Privacy => HudPrivacy,
        HudKind.Activity => ActivitiesEnabled,
        _ => true,
    };
}

public sealed class WaterData
{
    public int GoalMl { get; set; } = 2000;
    public int CupMl { get; set; } = 250;
    public int ReminderMinutes { get; set; } = 60;
    /// <summary>Off unless asked for (the welcome offers it): an hourly nudge nobody wanted is noise.
    /// Existing data.json files keep what they have.</summary>
    public bool RemindersEnabled { get; set; }
    public DateTime? LastDrink { get; set; }
    /// <summary>Milliliters per day, keyed "yyyy-MM-dd".</summary>
    public Dictionary<string, int> Days { get; set; } = new();
}

public sealed class TaskData
{
    public Guid Id { get; set; }
    public string Text { get; set; } = "";
    public bool Done { get; set; }
    public DateTime? Due { get; set; }
    /// <summary>The due has a day but no time (set in Google Tasks, which only keeps dates).</summary>
    public bool DueAllDay { get; set; }
    public bool Reminded { get; set; }
    /// <summary>Id in Google Tasks once synced; null for tasks that live only here.</summary>
    public string? GoogleId { get; set; }
    /// <summary>Changed here and not yet sent to Google.</summary>
    public bool Dirty { get; set; }
}

/// <summary>Local JSON storage in %LOCALAPPDATA%\Yata\data.json. Nothing leaves the PC.</summary>
public sealed class AppStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _readOnly;
    private bool _saveFailing;

    public StoreData Data { get; }

    public SettingsData Settings => Data.Settings;

    /// <summary>data.json was damaged and set aside; the app started over with defaults.</summary>
    public bool RecoveredFromDamage { get; private init; }

    /// <summary>There was no data.json: Yata's first start on this PC (for this user).</summary>
    public bool IsNew { get; private init; }

    /// <summary>Raised after the settings window changed something; listeners re-apply.</summary>
    public event Action? SettingsChanged;

    public void NotifySettingsChanged()
    {
        SaveSoon();
        SettingsChanged?.Invoke();
    }

    private AppStore(string path, StoreData data)
    {
        _path = path;
        Data = data;
        _saveTimer.Tick += (_, _) => SaveNow();
    }

    public static AppStore Load() => Load(Path.Combine(AppPaths.DataDir, "data.json"));

    internal static AppStore Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new AppStore(path, new StoreData()) { IsNew = true };
            var data = JsonSerializer.Deserialize<StoreData>(File.ReadAllText(path), Options);
            return new AppStore(path, data ?? new StoreData());
        }
        catch (JsonException ex)
        {
            // Damaged (e.g. power lost mid-write on an older version): start fresh rather than
            // refuse to run, and keep the bad file aside.
            Log.Error("store", "data.json ilegível; guardado como data.json.bad", ex);
            TryBackup(path);
            return new AppStore(path, new StoreData()) { RecoveredFromDamage = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // There but unreadable right now (locked, permissions): run on defaults without
            // saving, so the user's file isn't overwritten.
            Log.Error("store", "data.json inacessível; nada será gravado nesta sessão", ex);
            return new AppStore(path, new StoreData()) { _readOnly = true };
        }
    }

    /// <summary>
    /// A store that lives only in memory, for tests. Saves are scheduled on a dispatcher timer
    /// that never ticks without a message loop, so nothing is written.
    /// </summary>
    internal static AppStore InMemory() =>
        new(Path.Combine(Path.GetTempPath(), "yata-tests", "unused.json"), new StoreData());

    /// <summary>Coalesces bursts of changes (e.g. several clicks) into one write.</summary>
    public void SaveSoon()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        _saveTimer.Stop();
        if (_readOnly)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temp = _path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, Data, Options);
                // On disk before the swap: a power cut can't leave an empty data.json behind.
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, _path, overwrite: true);
            _saveFailing = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The next change retries; one log line per run of failures is enough.
            if (!_saveFailing)
                Log.Warn("store", "Falha ao gravar data.json", ex);
            _saveFailing = true;
        }
    }

    private static void TryBackup(string path)
    {
        try
        {
            File.Move(path, path + ".bad", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("store", "Não foi possível guardar a cópia data.json.bad", ex);
        }
    }
}
