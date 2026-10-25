using System;
using System.Windows.Threading;
using Notch.Helpers;

namespace Notch.Services;

public enum FocusPhase { Focus, ShortBreak, LongBreak }

/// <summary>
/// Pomodoro: 25 min focus, 5 min break, 15 min break after every 4th session.
/// Breaks start on their own; the next focus session waits for the user.
/// </summary>
public sealed class FocusTimer
{
    public const int SessionsPerCycle = 4;
    private static readonly TimeSpan FocusLength = TimeSpan.FromMinutes(25);
    private static readonly TimeSpan ShortBreakLength = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LongBreakLength = TimeSpan.FromMinutes(15);

    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private TimeSpan _pausedRemaining = FocusLength;
    private DateTime _endsAt;

    public FocusPhase Phase { get; private set; } = FocusPhase.Focus;
    public bool IsRunning { get; private set; }
    /// <summary>Focus sessions finished in the current cycle (0..4).</summary>
    public int CompletedSessions { get; private set; }

    public TimeSpan PhaseLength => Phase switch
    {
        FocusPhase.ShortBreak => ShortBreakLength,
        FocusPhase.LongBreak => LongBreakLength,
        _ => FocusLength,
    };

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

    public FocusTimer() => _tick.Tick += OnTick;

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

    /// <summary>Restarts the current phase from its full length, stopped.</summary>
    public void Reset()
    {
        IsRunning = false;
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
