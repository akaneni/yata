using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Threading;

namespace Notch.Services;

/// <summary>Daily water intake with periodic reminders while the goal isn't met.</summary>
public sealed class WaterTracker
{
    private const int FirstReminderHour = 8;
    private const int LastReminderHour = 22;
    private const int KeepDays = 60;

    private readonly AppStore _store;
    private readonly DispatcherTimer _reminder = new() { Interval = TimeSpan.FromMinutes(1) };
    // Don't nag right after the app starts.
    private DateTime _lastReminder = DateTime.Now;

    public event Action? Changed;
    public event Action<HudMessage>? Message;

    public WaterTracker(AppStore store)
    {
        _store = store;
        _reminder.Tick += (_, _) => CheckReminder();
    }

    private WaterData Data => _store.Data.Water;

    public int GoalMl => Data.GoalMl;
    public int CupMl => Data.CupMl;
    public int TodayMl => AmountOn(DateTime.Today);

    public void Start() => _reminder.Start();

    public void Stop() => _reminder.Stop();

    public void Drink() => Add(Data.CupMl);

    public void Undo() => Add(-Data.CupMl);

    /// <summary>Oldest first, ending today.</summary>
    public IReadOnlyList<(DateTime Day, int Ml)> LastDays(int count) =>
        Enumerable.Range(0, count)
            .Select(i => DateTime.Today.AddDays(i - count + 1))
            .Select(day => (day, AmountOn(day)))
            .ToList();

    private void Add(int ml)
    {
        int today = Math.Max(0, TodayMl + ml);
        Data.Days[Key(DateTime.Today)] = today;
        if (ml > 0)
            Data.LastDrink = DateTime.Now;
        Prune();
        _store.SaveSoon();
        Changed?.Invoke();
    }

    private void CheckReminder()
    {
        var now = DateTime.Now;
        if (now.Hour < FirstReminderHour || now.Hour >= LastReminderHour || TodayMl >= GoalMl)
            return;

        var dayStart = DateTime.Today.AddHours(FirstReminderHour);
        var since = new[] { dayStart, _lastReminder, Data.LastDrink ?? DateTime.MinValue }.Max();
        if (now - since < TimeSpan.FromMinutes(Data.ReminderMinutes))
            return;

        _lastReminder = now;
        Message?.Invoke(new HudMessage(Glyphs.Water, "Hora de beber água", $"{TodayMl}/{GoalMl} ml",
            (double)TodayMl / GoalMl, DurationMs: 4000, Important: true));
    }

    private int AmountOn(DateTime day) => Data.Days.TryGetValue(Key(day), out int ml) ? ml : 0;

    private void Prune()
    {
        string oldest = Key(DateTime.Today.AddDays(-KeepDays));
        foreach (var key in Data.Days.Keys.Where(k => string.CompareOrdinal(k, oldest) < 0).ToList())
            Data.Days.Remove(key);
    }

    private static string Key(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
