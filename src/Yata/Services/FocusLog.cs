using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Yata.Services;

/// <summary>
/// How much focus each day had, against the daily goal, and the task the focus is on. Local, in
/// data.json like the water log; a stretch of focus counts on the day it ends.
/// </summary>
public sealed class FocusLog
{
    private const int KeepDays = 60;

    private readonly AppStore _store;
    private readonly FocusTimer _timer;
    private readonly Func<DateTime> _clock;

    public event Action? Changed;

    /// <param name="clock">Current time; tests pass a fake one.</param>
    public FocusLog(AppStore store, FocusTimer timer, Func<DateTime>? clock = null)
    {
        _store = store;
        _timer = timer;
        _clock = clock ?? (() => DateTime.Now);
        _timer.Focused += Add;
    }

    private FocusData Data => _store.Data.Focus;
    private DateTime Today => _clock().Date;

    public TimeSpan Goal => TimeSpan.FromMinutes(_store.Settings.FocusGoalMinutes);

    /// <summary>Today's focus, the session running now included.</summary>
    public TimeSpan TodayTotal => On(Today) + _timer.CurrentRun;

    /// <summary>Oldest first, ending today (with the session running now).</summary>
    public IReadOnlyList<(DateTime Day, TimeSpan Focus)> LastDays(int count) =>
        Enumerable.Range(0, count)
            .Select(i => Today.AddDays(i - count + 1))
            .Select(day => (day, day == Today ? TodayTotal : On(day)))
            .ToList();

    /// <summary>The task the focus is on; null if none was picked or it's gone.</summary>
    public Guid? TaskId
    {
        get => Data.TaskId;
        set
        {
            if (Data.TaskId == value)
                return;
            Data.TaskId = value;
            _store.SaveSoon();
            Changed?.Invoke();
        }
    }

    /// <summary>The goal changed in the settings.</summary>
    public void ApplySettings() => Changed?.Invoke();

    private void Add(TimeSpan focus)
    {
        string key = Key(Today);
        Data.Days[key] = (Data.Days.TryGetValue(key, out int seconds) ? seconds : 0) + (int)Math.Round(focus.TotalSeconds);
        Prune();
        _store.SaveSoon();
        Changed?.Invoke();
    }

    private TimeSpan On(DateTime day) => TimeSpan.FromSeconds(Data.Days.TryGetValue(Key(day), out int seconds) ? seconds : 0);

    private void Prune()
    {
        string oldest = Key(Today.AddDays(-KeepDays));
        foreach (var key in Data.Days.Keys.Where(k => string.CompareOrdinal(k, oldest) < 0).ToList())
            Data.Days.Remove(key);
    }

    private static string Key(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>"25 min", "1h 40", "2h".</summary>
    public static string Format(TimeSpan time)
    {
        int minutes = (int)time.TotalMinutes;
        if (minutes < 60)
            return $"{minutes} min";
        return minutes % 60 == 0 ? $"{minutes / 60}h" : $"{minutes / 60}h {minutes % 60:00}";
    }
}
