using System;
using System.Windows.Threading;
using Yata.Helpers;

namespace Yata.Services;

public enum FocusPhase { Focus, ShortBreak, LongBreak }

/// <summary>
/// Pomodoro (by default 25 min focus, 5 min break, 15 min break after every 4th session;
/// lengths come from the settings). Breaks start on their own; the next focus session waits.
/// </summary>
public sealed class FocusTimer
{
    private readonly SettingsData _settings;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private TimeSpan _pausedRemaining;
    private DateTime _endsAt;
    private bool _phaseStarted;

    public FocusPhase Phase { get; private set; } = FocusPhase.Focus;
    public bool IsRunning { get; private set; }
    /// <summary>Focus sessions finished in the current cycle (0..SessionsPerCycle).</summary>
    public int CompletedSessions { get; private set; }

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
            var left = _endsAt - DateTime.Now;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    public double Progress => 1 - Remaining.TotalSeconds / PhaseLength.TotalSeconds;

    /// <summary>Running, or paused part-way through a phase.</summary>
    public bool IsActive => IsRunning || Remaining < PhaseLength;

    public event Action? Changed;
    public event Action<HudMessage>? Message;

    public FocusTimer(SettingsData settings)
    {
        _settings = settings;
        _pausedRemaining = PhaseLength;
        _tick.Tick += OnTick;
    }

    /// <summary>
    /// New lengths apply at once to a phase that hasn't started; a started one keeps its
    /// countdown, capped at the new length.
    /// </summary>
    public void ApplySettings()
    {
        if (!_phaseStarted || _pausedRemaining > PhaseLength)
            _pausedRemaining = PhaseLength;
        if (CompletedSessions > SessionsPerCycle)
            CompletedSessions = SessionsPerCycle;
        Changed?.Invoke();
    }

    public void Toggle()
    {
        if (IsRunning)
            Pause();
        else
            Start();
    }

    public void Start()
    {
        _endsAt = DateTime.Now + _pausedRemaining;
        IsRunning = true;
        _phaseStarted = true;
        _tick.Start();
        Changed?.Invoke();
    }

    public void Pause()
    {
        _pausedRemaining = Remaining;
        IsRunning = false;
        _tick.Stop();
        Changed?.Invoke();
    }

    /// <summary>
    /// The user walked away: pause and give back the minutes that already ran unattended,
    /// so time away doesn't count as focus.
    /// </summary>
    public bool PauseAfterIdle(TimeSpan unattended)
    {
        if (!IsRunning)
            return false;

        var remaining = Remaining + unattended;
        _pausedRemaining = remaining < PhaseLength ? remaining : PhaseLength;
        IsRunning = false;
        _tick.Stop();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Restarts the current phase from its full length, stopped.</summary>
    public void Reset()
    {
        IsRunning = false;
        _phaseStarted = false;
        _tick.Stop();
        _pausedRemaining = PhaseLength;
        Changed?.Invoke();
    }

    /// <summary>Ends the current phase now, without the chime.</summary>
    public void Skip() => Advance(notify: false);

    private void OnTick(object? sender, EventArgs e)
    {
        if (Remaining <= TimeSpan.Zero)
            Advance(notify: true);
        else
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
                Notify("Hora da pausa", $"{PhaseLength.TotalMinutes:0} min");
            Start();
        }
        else
        {
            if (Phase == FocusPhase.LongBreak)
                CompletedSessions = 0;
            Phase = FocusPhase.Focus;
            IsRunning = false;
            _phaseStarted = false;
            _tick.Stop();
            _pausedRemaining = PhaseLength;
            if (notify)
                Notify("De volta ao foco", $"{PhaseLength.TotalMinutes:0} min");
            Changed?.Invoke();
        }
    }

    private void Notify(string text, string value)
    {
        Chime.Play();
        Message?.Invoke(new HudMessage(Glyphs.Timer, text, value, DurationMs: 4000, Important: true));
    }
}
