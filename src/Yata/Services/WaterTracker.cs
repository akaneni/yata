using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Threading;

namespace Yata.Services;

/// <summary>Daily water intake with periodic reminders while the goal isn't met.</summary>
public sealed class WaterTracker
{
    private const int FirstReminderHour = 8;
    private const int LastReminderHour = 22;
    private const int KeepDays = 60;

    private readonly AppStore _store;
    private readonly Func<DateTime> _clock;
    private readonly DispatcherTimer _reminder = new() { Interval = TimeSpan.FromMinutes(1) };
    // Don't nag right after the app starts.
    private DateTime _lastReminder;

    public event Action? Changed;
    public event Action<HudMessage>? Message;

    /// <param name="clock">Current time; tests pass a fake one.</param>
    public WaterTracker(AppStore store, Func<DateTime>? clock = null)
    {
        _store = store;
        _clock = clock ?? (() => DateTime.Now);
        _lastReminder = _clock();
        _reminder.Tick += (_, _) => CheckReminder();
    }

    private WaterData Data => _store.Data.Water;
    private DateTime Today => _clock().Date;

    public int GoalMl => Data.GoalMl;
    public int CupMl => Data.CupMl;
    public int TodayMl => AmountOn(Today);

    public void Start() => _reminder.Start();

    public void Stop() => _reminder.Stop();

    /// <summary>Goal or cup size changed in the settings.</summary>
    public void ApplySettings() => Changed?.Invoke();

    public void Drink() => Add(Data.CupMl);

    public void Undo() => Add(-Data.CupMl);

    /// <summary>Oldest first, ending today.</summary>
    public IReadOnlyList<(DateTime Day, int Ml)> LastDays(int count) =>
        Enumerable.Range(0, count)
            .Select(i => Today.AddDays(i - count + 1))
            .Select(day => (day, AmountOn(day)))
            .ToList();

    private void Add(int ml)
    {
        int before = TodayMl;
        int today = Math.Max(0, before + ml);
        Data.Days[Key(Today)] = today;
        if (ml > 0)
            Data.LastDrink = _clock();
        Prune();
        _store.SaveSoon();
        Changed?.Invoke();

        // Shown once the notch closes, since drinking is logged with it open.
        if (before < GoalMl && today >= GoalMl)
            Message?.Invoke(new HudMessage(Glyphs.Water, Strings.Water_GoalReached, $"{today}/{GoalMl} ml", 1,
                DurationMs: 4000, Important: true, Celebrate: true));
    }

    /// <summary>One reminder check, as the minute timer does; for tests.</summary>
    internal void Poll() => CheckReminder();

    private void CheckReminder()
    {
        var now = _clock();
        if (!Data.RemindersEnabled || now.Hour < FirstReminderHour || now.Hour >= LastReminderHour || TodayMl >= GoalMl)
            return;

        var dayStart = now.Date.AddHours(FirstReminderHour);
        var since = new[] { dayStart, _lastReminder, Data.LastDrink ?? DateTime.MinValue }.Max();
        if (now - since < TimeSpan.FromMinutes(Data.ReminderMinutes))
            return;

        _lastReminder = now;
        Message?.Invoke(new HudMessage(Glyphs.Water, Strings.Water_Reminder, $"{TodayMl}/{GoalMl} ml",
            (double)TodayMl / GoalMl, DurationMs: 4000, Important: true,
            Tag: "water", Expires: now.AddMinutes(Data.ReminderMinutes)));
    }

    private int AmountOn(DateTime day) => Data.Days.TryGetValue(Key(day), out int ml) ? ml : 0;

    private void Prune()
    {
        string oldest = Key(Today.AddDays(-KeepDays));
        foreach (var key in Data.Days.Keys.Where(k => string.CompareOrdinal(k, oldest) < 0).ToList())
            Data.Days.Remove(key);
    }

    private static string Key(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
