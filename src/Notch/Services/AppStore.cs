using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace Notch.Services;

public sealed class StoreData
{
    public WaterData Water { get; set; } = new();
    public List<TaskData> Tasks { get; set; } = new();
    /// <summary>Paths parked on the shelf (references only, files are never copied).</summary>
    public List<string> Shelf { get; set; } = new();
}

public sealed class WaterData
{
    public int GoalMl { get; set; } = 2000;
    public int CupMl { get; set; } = 250;
    public int ReminderMinutes { get; set; } = 60;
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

    private AppStore(string path, StoreData data)
    {
        _path = path;
        Data = data;
        _saveTimer.Tick += (_, _) => SaveNow();
    }

    public static AppStore Load()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notch", "data.json");
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
