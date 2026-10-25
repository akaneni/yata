using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Threading;
using Yata.Interop;

namespace Yata.Services;

public enum Presence { Active, Idle, Asleep }

public sealed record AwayItem(string Glyph, string Text, DateTime At);

public sealed record AwayReport(TimeSpan Duration, IReadOnlyList<AwayItem> Items);

/// <summary>
/// Notices when the user steps away and comes back, from the time of the last mouse/keyboard
/// input only (GetLastInputInfo) — never what was typed. Collects what happened meanwhile.
/// </summary>
public sealed class PresenceTracker
{
    public static readonly TimeSpan AwayAfter = TimeSpan.FromMinutes(5);
    // Something that fires after this much inactivity probably went unseen.
    private static readonly TimeSpan UnseenAfter = TimeSpan.FromMinutes(1);
    private const int BatteryChangeWorthReporting = 5;

    private readonly SettingsData _settings;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<AwayItem> _items = new();
    private TimeSpan _lastIdle;
    private int? _batteryAtAway;

    /// <summary>Inactivity before Yata shows up.</summary>
    public TimeSpan IdleAfter => TimeSpan.FromMinutes(Math.Max(1, _settings.CompanionIdleMinutes));

    /// <summary>Inactivity before Yata dozes off; always after <see cref="IdleAfter"/>.</summary>
    public TimeSpan SleepAfter => TimeSpan.FromMinutes(Math.Max(_settings.CompanionSleepMinutes, _settings.CompanionIdleMinutes + 1));

    public Presence State { get; private set; } = Presence.Active;
    public bool IsAway { get; private set; }
    public TimeSpan Idle => _simulatedIdle ?? NativeMethods.GetIdleTime();

    private TimeSpan? _simulatedIdle;

    /// <summary>Demo mode: pretend this much inactivity (null returns to the real value).</summary>
    public void Simulate(TimeSpan? idle)
    {
        _simulatedIdle = idle;
        Check();
    }

    /// <summary>When true (e.g. a full-screen video is on), inactivity doesn't count as absence.</summary>
    public Func<bool>? IsSurelyPresent { get; set; }

    public event Action? StateChanged;
    /// <summary>Crossed <see cref="AwayAfter"/>; argument is the inactivity so far.</summary>
    public event Action<TimeSpan>? WentAway;
    public event Action<AwayReport>? Returned;

    public PresenceTracker(SettingsData settings)
    {
        _settings = settings;
        _timer.Tick += (_, _) => Check();
    }

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    /// <summary>Remembers a notice if the user was probably not looking at the screen.</summary>
    public void NoteIfUnseen(HudMessage message)
    {
        if (Idle >= UnseenAfter)
            Add(message.Glyph, message.Value == null ? message.Text : $"{message.Text} · {message.Value}");
    }

    public void Add(string glyph, string text) => _items.Add(new AwayItem(glyph, text, DateTime.Now));

    private void Check()
    {
        var idle = _simulatedIdle ?? (IsSurelyPresent?.Invoke() == true ? TimeSpan.Zero : Idle);

        var state = idle >= SleepAfter ? Presence.Asleep
                  : idle >= IdleAfter ? Presence.Idle
                  : Presence.Active;

        if (!IsAway && idle >= AwayAfter)
        {
            IsAway = true;
            _batteryAtAway = BatteryPercent();
            WentAway?.Invoke(idle);
        }

        if (state == Presence.Active && idle < _lastIdle && _lastIdle >= UnseenAfter)
            ReportReturn(_lastIdle);
        _lastIdle = idle;

        if (state != State)
        {
            State = state;
            StateChanged?.Invoke();
        }
    }

    private void ReportReturn(TimeSpan duration)
    {
        if (IsAway && _batteryAtAway is int before && BatteryPercent() is int after &&
            Math.Abs(after - before) >= BatteryChangeWorthReporting)
            _items.Add(new AwayItem(after > before ? Glyphs.Charging : Glyphs.Battery, $"Bateria {before}% → {after}%", DateTime.Now));

        var report = new AwayReport(duration, _items.OrderBy(i => i.At).ToList());
        _items.Clear();
        IsAway = false;
        _batteryAtAway = null;
        Returned?.Invoke(report);
    }

    private static int? BatteryPercent()
    {
        var status = SystemInformation.PowerStatus;
        if (status.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery) ||
            status.BatteryLifePercent is < 0 or > 1)
            return null;
        return (int)Math.Round(status.BatteryLifePercent * 100);
    }
}
