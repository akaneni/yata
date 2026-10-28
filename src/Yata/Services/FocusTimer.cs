using System;
using System.Windows.Threading;
using Yata.Helpers;

namespace Yata.Services;

public enum FocusPhase { Focus, ShortBreak, LongBreak }

/// <summary>Ready (not started) → Running ⇄ Paused; Stop goes back to Ready.</summary>
public enum FocusState { Ready, Running, Paused }

/// <summary>
/// Pomodoro (by default 25 min focus, 5 min break, 15 min break after every 4th session;
/// lengths come from the settings). Breaks start on their own; the next focus session waits
/// in Ready until the user starts it.
/// </summary>
public sealed class FocusTimer
{
    private readonly SettingsData _settings;
    private readonly Func<DateTime> _now;
    private readonly DispatcherTimer _tick = new();
    private TimeSpan _pausedRemaining;
    private DateTime _endsAt;

    public FocusPhase Phase { get; private set; } = FocusPhase.Focus;
    public FocusState State { get; private set; } = FocusState.Ready;
    /// <summary>Focus sessions finished in the current cycle (0..SessionsPerCycle).</summary>
    public int CompletedSessions { get; private set; }

    public bool IsRunning => State == FocusState.Running;
    public bool IsPaused => State == FocusState.Paused;
    /// <summary>A session or break is under way (running or paused).</summary>
    public bool IsActive => State != FocusState.Ready;
    public bool IsBreak => Phase != FocusPhase.Focus;

    public int SessionsPerCycle => Math.Max(1, _settings.SessionsPerCycle);

    public TimeSpan PhaseLength => TimeSpan.FromMinutes(Math.Max(1, Phase switch
    {
        FocusPhase.ShortBreak => _settings.ShortBreakMinutes,
        FocusPhase.LongBreak => _settings.LongBreakMinutes,
        _ => _settings.FocusMinutes,
    }));

    public TimeSpan Remaining
    {
        get
        {
            if (!IsRunning)
                return _pausedRemaining;
            var left = _endsAt - _now();
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    public double Progress => 1 - Remaining.TotalSeconds / PhaseLength.TotalSeconds;

    public event Action? Changed;
    public event Action<HudMessage>? Message;

    /// <param name="clock">Time source; tests pass a fake one.</param>
    public FocusTimer(SettingsData settings, Func<DateTime>? clock = null)
    {
        _settings = settings;
        _now = clock ?? (() => DateTime.Now);
        _pausedRemaining = PhaseLength;
        _tick.Tick += (_, _) => Poll();
    }

    /// <summary>
    /// New lengths apply at once while Ready; a session under way keeps its countdown,
    /// capped at the new length.
    /// </summary>
    public void ApplySettings()
    {
        if (State == FocusState.Ready || _pausedRemaining > PhaseLength)
            _pausedRemaining = PhaseLength;
        if (CompletedSessions > SessionsPerCycle)
            CompletedSessions = SessionsPerCycle;
        Changed?.Invoke();
    }

    /// <summary>Ready → Running.</summary>
    public void Start()
    {
        if (State != FocusState.Ready)
            return;
        Run();
    }

    /// <summary>Running → Paused.</summary>
    public void Pause()
    {
        if (State != FocusState.Running)
            return;
        Freeze(Remaining);
    }

    /// <summary>Paused → Running, from where it stopped.</summary>
    public void Resume()
    {
        if (State != FocusState.Paused)
            return;
        Run();
    }

    /// <summary>
    /// Abandons whatever is under way and goes back to a fresh, not-started focus session.
    /// Finished sessions in the cycle are kept.
    /// </summary>
    public void Stop()
    {
        _tick.Stop();
        Phase = FocusPhase.Focus;
        State = FocusState.Ready;
        _pausedRemaining = PhaseLength;
        Changed?.Invoke();
    }

    /// <summary>Ends a break early (no chime). Does nothing during focus.</summary>
    public void SkipBreak()
    {
        if (IsBreak)
            Advance(notify: false);
    }

    /// <summary>
    /// The user walked away: pause and give back the minutes that already ran unattended,
    /// so time away doesn't count as focus.
    /// </summary>
    public bool PauseAfterIdle(TimeSpan unattended)
    {
        if (State != FocusState.Running)
            return false;

        var remaining = Remaining + unattended;
        Freeze(remaining < PhaseLength ? remaining : PhaseLength);
        return true;
    }

    /// <summary>Checks for the end of the phase. Called by the internal tick; tests call it directly.</summary>
    internal void Poll()
    {
        if (State != FocusState.Running)
            return;
        if (Remaining <= TimeSpan.Zero)
        {
            Advance(notify: true);
        }
        else
        {
            ScheduleTick();
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Time until the countdown shows another second (it reads whole seconds, rounded down),
    /// plus a hair so the tick lands after the change rather than just before it.
    /// </summary>
    internal static TimeSpan UntilNextSecond(TimeSpan remaining) =>
        TimeSpan.FromTicks(remaining.Ticks % TimeSpan.TicksPerSecond) + TimeSpan.FromMilliseconds(5);

    private void Run()
    {
        _endsAt = _now() + _pausedRemaining;
        State = FocusState.Running;
        ScheduleTick();
        _tick.Start();
        Changed?.Invoke();
    }

    // One wake-up per second shown, instead of polling four times a second.
    private void ScheduleTick() => _tick.Interval = UntilNextSecond(Remaining);

    private void Freeze(TimeSpan remaining)
    {
        _pausedRemaining = remaining;
        State = FocusState.Paused;
        _tick.Stop();
        Changed?.Invoke();
    }

    private void Advance(bool notify)
    {
        if (Phase == FocusPhase.Focus)
        {
            CompletedSessions++;
            Phase = CompletedSessions >= SessionsPerCycle ? FocusPhase.LongBreak : FocusPhase.ShortBreak;
            _pausedRemaining = PhaseLength;
            if (notify)
                Notify(Strings.Focus_BreakTime, $"{PhaseLength.TotalMinutes:0} min", celebrate: true,
                    expires: _now() + PhaseLength);
            Run(); // Breaks start on their own.
        }
        else
        {
            if (Phase == FocusPhase.LongBreak)
                CompletedSessions = 0;
            _tick.Stop();
            Phase = FocusPhase.Focus;
            State = FocusState.Ready;
            _pausedRemaining = PhaseLength;
            if (notify)
                Notify(Strings.Focus_BackToFocus, $"{PhaseLength.TotalMinutes:0} min");
            Changed?.Invoke();
        }
    }

    // Tagged: the news of the next phase replaces the one still waiting. "Break time" is old news
    // once the break is over.
    private void Notify(string text, string value, bool celebrate = false, DateTime? expires = null) =>
        Message?.Invoke(new HudMessage(Glyphs.Timer, text, value, DurationMs: 4000, Important: true, Celebrate: celebrate,
            Tag: "focus", Chime: true, Expires: expires));
}
