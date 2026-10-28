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
    // A tick this late means the PC slept (ticks come every second while running).
    internal static readonly TimeSpan SleepGap = TimeSpan.FromSeconds(30);

    private readonly DispatcherTimer _tick = new();
    private TimeSpan _pausedRemaining;
    // What was left when the current run started: what ran since is that minus what's left now.
    private TimeSpan _runFrom;
    private DateTime _endsAt;
    private DateTime _lastTick;

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

    /// <summary>Focus run so far and not yet reported by <see cref="Focused"/>; zero unless focusing.</summary>
    public TimeSpan CurrentRun => State == FocusState.Running && Phase == FocusPhase.Focus ? Ran(Remaining) : TimeSpan.Zero;

    public event Action? Changed;
    public event Action<HudMessage>? Message;
    /// <summary>
    /// A stretch of focus ended (the session finished, was paused or stopped, or the PC slept), with
    /// how long it really ran. Time away given back by <see cref="PauseAfterIdle"/> doesn't count.
    /// </summary>
    public event Action<TimeSpan>? Focused;

    /// <param name="clock">Time source; tests pass a fake one. UTC by default: a time zone change
    /// or daylight saving must not shorten or stretch a session.</param>
    public FocusTimer(SettingsData settings, Func<DateTime>? clock = null)
    {
        _settings = settings;
        _now = clock ?? (() => DateTime.UtcNow);
        _pausedRemaining = PhaseLength;
        _tick.Tick += (_, _) => Tick();
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
        if (State == FocusState.Running)
            EndRun(Remaining);
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

    /// <summary>
    /// The timer's tick. One that comes far later than a second means the PC slept (the lid
    /// closed on the way out of the dock): see <see cref="Slept"/>. Tests call it directly.
    /// </summary>
    internal void Tick()
    {
        var now = _now();
        var last = _lastTick;
        _lastTick = now;
        if (State == FocusState.Running && now - last > SleepGap)
            Slept(last, now);
        else
            Poll();
    }

    /// <summary>
    /// The PC slept from <paramref name="from"/> to <paramref name="to"/>. A focus session pauses
    /// where it was when the PC went to sleep, instead of counting the sleep as focus and starting
    /// the break on its own; it waits for Continue. A break the sleep outlasted counts as taken.
    /// </summary>
    private void Slept(DateTime from, DateTime to)
    {
        var left = _endsAt - from;
        if (Phase == FocusPhase.Focus && left > TimeSpan.Zero)
        {
            Freeze(left < PhaseLength ? left : PhaseLength);
            Message?.Invoke(new HudMessage(Glyphs.Timer, Strings.Focus_PausedAsleep,
                string.Format(Lang.Culture, Strings.Focus_MinutesLeft, Math.Ceiling(left.TotalMinutes)),
                DurationMs: 4000, Important: true, Tag: "focus"));
        }
        else if (Phase != FocusPhase.Focus && to >= _endsAt)
        {
            Advance(notify: false);
        }
        else
        {
            Poll();
        }
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
        _runFrom = _pausedRemaining;
        _lastTick = _now();
        _endsAt = _lastTick + _pausedRemaining;
        State = FocusState.Running;
        ScheduleTick();
        _tick.Start();
        Changed?.Invoke();
    }

    // One wake-up per second shown, instead of polling four times a second.
    private void ScheduleTick() => _tick.Interval = UntilNextSecond(Remaining);

    private void Freeze(TimeSpan remaining)
    {
        if (State == FocusState.Running)
            EndRun(remaining);
        _pausedRemaining = remaining;
        State = FocusState.Paused;
        _tick.Stop();
        Changed?.Invoke();
    }

    private void Advance(bool notify)
    {
        if (Phase == FocusPhase.Focus)
        {
            if (State == FocusState.Running)
                EndRun(TimeSpan.Zero);
            CompletedSessions++;
            Phase = CompletedSessions >= SessionsPerCycle ? FocusPhase.LongBreak : FocusPhase.ShortBreak;
            _pausedRemaining = PhaseLength;
            if (notify)
                Notify(Strings.Focus_BreakTime, $"{PhaseLength.TotalMinutes:0} min", celebrate: true,
                    expires: DateTime.Now + PhaseLength); // Notices expire on the wall clock.
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

    private TimeSpan Ran(TimeSpan remaining) => remaining < _runFrom ? _runFrom - remaining : TimeSpan.Zero;

    /// <summary>The run stops with <paramref name="remaining"/> left; a focus run reports what it covered.</summary>
    private void EndRun(TimeSpan remaining)
    {
        if (Phase == FocusPhase.Focus && Ran(remaining) is { } ran && ran > TimeSpan.Zero)
            Focused?.Invoke(ran);
        _runFrom = remaining; // Reported once, even if Stop follows a Freeze.
    }

    // Tagged: the news of the next phase replaces the one still waiting. "Break time" is old news
    // once the break is over.
    private void Notify(string text, string value, bool celebrate = false, DateTime? expires = null) =>
        Message?.Invoke(new HudMessage(Glyphs.Timer, text, value, DurationMs: 4000, Important: true, Celebrate: celebrate,
            Tag: "focus", Chime: true, Expires: expires));
}
