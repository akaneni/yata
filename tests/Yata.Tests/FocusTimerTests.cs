using Yata.Services;

namespace Yata.Tests;

/// <summary>
/// Usage flows of the Pomodoro: start, pause, continue, stop, breaks and the full cycle,
/// driven by a fake clock instead of real minutes.
/// </summary>
public class FocusTimerTests
{
    private DateTime _now = new(2026, 10, 26, 9, 0, 0);
    private readonly SettingsData _settings = new();
    private readonly List<HudMessage> _messages = new();
    private int _chimes;

    private FocusTimer NewTimer()
    {
        var timer = new FocusTimer(_settings, () => _now, () => _chimes++);
        timer.Message += _messages.Add;
        return timer;
    }

    private void Advance(FocusTimer timer, TimeSpan by)
    {
        _now += by;
        timer.Poll();
    }

    private static TimeSpan Min(double minutes) => TimeSpan.FromMinutes(minutes);

    [Fact]
    public void Starts_ready_with_a_full_focus_session()
    {
        var timer = NewTimer();

        Assert.Equal(FocusState.Ready, timer.State);
        Assert.Equal(FocusPhase.Focus, timer.Phase);
        Assert.Equal(Min(25), timer.Remaining);
        Assert.False(timer.IsActive);
        Assert.Equal(0, timer.Progress);
    }

    [Fact]
    public void Running_counts_down_with_the_clock()
    {
        var timer = NewTimer();
        timer.Start();
        Advance(timer, Min(10));

        Assert.Equal(FocusState.Running, timer.State);
        Assert.True(timer.IsActive);
        Assert.Equal(Min(15), timer.Remaining);
    }

    [Fact]
    public void Pause_freezes_the_time_and_continue_resumes_from_there()
    {
        var timer = NewTimer();
        timer.Start();
        Advance(timer, Min(10));

        timer.Pause();
        Advance(timer, Min(30)); // Coffee break: nothing should move.
        Assert.Equal(FocusState.Paused, timer.State);
        Assert.True(timer.IsActive);
        Assert.Equal(Min(15), timer.Remaining);

        timer.Resume();
        Advance(timer, Min(5));
        Assert.Equal(FocusState.Running, timer.State);
        Assert.Equal(Min(10), timer.Remaining);
    }

    [Fact]
    public void Stop_abandons_the_session_and_keeps_finished_ones()
    {
        var timer = NewTimer();
        CompleteFocus(timer);       // 1 finished; the break starts by itself
        timer.SkipBreak();
        timer.Start();
        Advance(timer, Min(12));

        timer.Stop();

        Assert.Equal(FocusState.Ready, timer.State);
        Assert.Equal(FocusPhase.Focus, timer.Phase);
        Assert.Equal(Min(25), timer.Remaining);
        Assert.Equal(1, timer.CompletedSessions);
        Assert.False(timer.IsActive);
    }

    [Fact]
    public void Stop_while_paused_also_goes_back_to_ready()
    {
        var timer = NewTimer();
        timer.Start();
        Advance(timer, Min(3));
        timer.Pause();

        timer.Stop();

        Assert.Equal(FocusState.Ready, timer.State);
        Assert.Equal(Min(25), timer.Remaining);
    }

    [Fact]
    public void Finishing_focus_starts_the_short_break_with_a_chime()
    {
        var timer = NewTimer();
        timer.Start();
        Advance(timer, Min(25));

        Assert.Equal(FocusPhase.ShortBreak, timer.Phase);
        Assert.Equal(FocusState.Running, timer.State);
        Assert.Equal(Min(5), timer.Remaining);
        Assert.Equal(1, timer.CompletedSessions);
        Assert.Equal(1, _chimes);
        Assert.Equal("Hora da pausa", Assert.Single(_messages).Text);
    }

    [Fact]
    public void After_a_break_the_next_focus_waits_for_the_user()
    {
        var timer = NewTimer();
        CompleteFocus(timer);
        Advance(timer, Min(5));

        Assert.Equal(FocusPhase.Focus, timer.Phase);
        Assert.Equal(FocusState.Ready, timer.State);
        Assert.Equal(Min(25), timer.Remaining);
        Assert.Equal(2, _chimes);
        Assert.Equal("De volta ao foco", _messages[^1].Text);
    }

    [Fact]
    public void Every_fourth_session_earns_a_long_break_then_the_cycle_restarts()
    {
        var timer = NewTimer();
        for (int i = 1; i <= 3; i++)
        {
            CompleteFocus(timer);
            Assert.Equal(FocusPhase.ShortBreak, timer.Phase);
            Advance(timer, Min(5));
        }

        CompleteFocus(timer);
        Assert.Equal(FocusPhase.LongBreak, timer.Phase);
        Assert.Equal(Min(15), timer.Remaining);
        Assert.Equal(4, timer.CompletedSessions);

        Advance(timer, Min(15));
        Assert.Equal(FocusPhase.Focus, timer.Phase);
        Assert.Equal(0, timer.CompletedSessions);
    }

    [Fact]
    public void Skip_break_ends_it_quietly_and_does_nothing_during_focus()
    {
        var timer = NewTimer();
        timer.SkipBreak(); // Focus, not started: must not count a session.
        Assert.Equal(FocusPhase.Focus, timer.Phase);
        Assert.Equal(0, timer.CompletedSessions);

        CompleteFocus(timer);
        int chimesBefore = _chimes;
        timer.SkipBreak();

        Assert.Equal(FocusPhase.Focus, timer.Phase);
        Assert.Equal(FocusState.Ready, timer.State);
        Assert.Equal(chimesBefore, _chimes);
    }

    [Fact]
    public void Break_can_be_paused_and_continued()
    {
        var timer = NewTimer();
        CompleteFocus(timer);
        Advance(timer, Min(2));

        timer.Pause();
        Advance(timer, Min(10));
        Assert.Equal(FocusPhase.ShortBreak, timer.Phase);
        Assert.Equal(Min(3), timer.Remaining);

        timer.Resume();
        Advance(timer, Min(3));
        Assert.Equal(FocusPhase.Focus, timer.Phase);
    }

    [Fact]
    public void Walking_away_pauses_and_gives_back_the_unattended_minutes()
    {
        var timer = NewTimer();
        timer.Start();
        Advance(timer, Min(15)); // Last 5 of these were away from the PC.

        Assert.True(timer.PauseAfterIdle(Min(5)));

        Assert.Equal(FocusState.Paused, timer.State);
        Assert.Equal(Min(15), timer.Remaining);
    }

    [Fact]
    public void Walking_away_never_refunds_more_than_the_whole_session()
    {
        var timer = NewTimer();
        timer.Start();
        Advance(timer, Min(2));

        timer.PauseAfterIdle(Min(40));

        Assert.Equal(Min(25), timer.Remaining);
    }

    [Fact]
    public void Walking_away_does_nothing_unless_running()
    {
        var timer = NewTimer();
        Assert.False(timer.PauseAfterIdle(Min(5)));
        timer.Start();
        timer.Pause();
        Assert.False(timer.PauseAfterIdle(Min(5)));
    }

    [Fact]
    public void Buttons_out_of_order_are_ignored()
    {
        var timer = NewTimer();
        timer.Pause();  // Nothing running.
        timer.Resume(); // Nothing paused.
        Assert.Equal(FocusState.Ready, timer.State);

        timer.Start();
        Advance(timer, Min(5));
        timer.Start();  // Already running: must not restart the countdown.
        Assert.Equal(Min(20), timer.Remaining);
    }

    [Fact]
    public void New_lengths_apply_to_a_session_not_started_yet()
    {
        var timer = NewTimer();
        _settings.FocusMinutes = 50;
        timer.ApplySettings();

        Assert.Equal(Min(50), timer.Remaining);
    }

    [Fact]
    public void A_running_session_keeps_its_countdown_but_never_exceeds_a_shorter_length()
    {
        var timer = NewTimer();
        timer.Start();
        Advance(timer, Min(5));
        timer.Pause();

        _settings.FocusMinutes = 30;
        timer.ApplySettings();
        Assert.Equal(Min(20), timer.Remaining);

        _settings.FocusMinutes = 10;
        timer.ApplySettings();
        Assert.Equal(Min(10), timer.Remaining);
    }

    [Fact]
    public void Fewer_sessions_per_cycle_caps_the_finished_count()
    {
        var timer = NewTimer();
        for (int i = 0; i < 3; i++)
        {
            CompleteFocus(timer);
            timer.SkipBreak();
        }

        _settings.SessionsPerCycle = 2;
        timer.ApplySettings();

        Assert.Equal(2, timer.CompletedSessions);
    }

    private void CompleteFocus(FocusTimer timer)
    {
        timer.Start();
        Advance(timer, timer.Remaining);
    }
}
