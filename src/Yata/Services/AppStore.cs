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
    /// <summary>Paths parked on the shelf (references only, files are never copied).</summary>
    public List<string> Shelf { get; set; } = new();
}

/// <summary>User preferences. "Start with Windows" isn't here: the registry is its source of truth.</summary>
public sealed class SettingsData
{
    // General
    public bool HideInFullscreen { get; set; } = true;
    public int HoverDelayMs { get; set; } = 140;
    public bool UseArtworkAccent { get; set; } = true;

    // Media
    public bool LyricsEnabled { get; set; } = true;

    // Calendar (Google)
    /// <summary>Notice this long before an event; 0 = only when it starts.</summary>
    public int CalendarReminderMinutes { get; set; } = 5;
    /// <summary>The closed notch shows the next event in the minutes before it.</summary>
    public bool CalendarInCompact { get; set; } = true;
    /// <summary>Calendars to show; null = the ones ticked in Google Calendar itself.</summary>
    public List<string>? CalendarIds { get; set; }

    // Notices
    public bool HudVolume { get; set; } = true;
    public bool HudBrightness { get; set; } = true;
    public bool HudPower { get; set; } = true;
    public bool HudAudioDevice { get; set; } = true;
    public bool HudCapsLock { get; set; } = true;
    public bool HudKeyboardLayout { get; set; } = true;
    public bool HudNetwork { get; set; } = true;

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
        _ => true,
    };
}

public sealed class WaterData
{
    public int GoalMl { get; set; } = 2000;
    public int CupMl { get; set; } = 250;
    public int ReminderMinutes { get; set; } = 60;
    public bool RemindersEnabled { get; set; } = true;
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
    public bool Reminded { get; set; }
}

/// <summary>Local JSON storage in %LOCALAPPDATA%\Notch\data.json. Nothing leaves the PC.</summary>
public sealed class AppStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    public StoreData Data { get; }

    public SettingsData Settings => Data.Settings;

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

    public static AppStore Load()
    {
        string path = Path.Combine(AppPaths.DataDir, "data.json");
        StoreData? data = null;
        try
        {
            if (File.Exists(path))
                data = JsonSerializer.Deserialize<StoreData>(File.ReadAllText(path), Options);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Unreadable file: start fresh rather than refuse to run. The bad file is kept aside.
            TryBackup(path);
        }
        return new AppStore(path, data ?? new StoreData());
    }

    /// <summary>Coalesces bursts of changes (e.g. several clicks) into one write.</summary>
    public void SaveSoon()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        _saveTimer.Stop();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Data, Options));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort; the next change retries.
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
        }
    }
}
