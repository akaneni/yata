using Yata.Services;

namespace Yata.Tests;

/// <summary>Focus time per day on the Focus page: what counts, the running session, the week and the task.</summary>
public class FocusLogTests
{
    // The timer runs on UTC and the log on the wall clock; tests move both together.
    private DateTime _now = new(2026, 10, 26, 9, 0, 0);
    private readonly AppStore _store = AppStore.InMemory();
    private readonly FocusTimer _timer;
    private readonly FocusLog _log;

    public FocusLogTests()
    {
        _timer = new FocusTimer(_store.Settings, () => _now);
        _log = new FocusLog(_store, _timer, () => _now);
    }

    private void Wait(double minutes)
    {
        _now += TimeSpan.FromMinutes(minutes);
        _timer.Poll();
    }

    private static TimeSpan Min(double minutes) => TimeSpan.FromMinutes(minutes);

    [Fact]
    public void A_finished_session_counts_whole()
    {
        _timer.Start();
        Wait(25);

        Assert.True(_timer.IsBreak);
        Assert.Equal(Min(25), _log.TodayTotal);
    }

    [Fact]
    public void Breaks_dont_count()
    {
        _timer.Start();
        Wait(25);
        Wait(5);

        Assert.Equal(FocusState.Ready, _timer.State);
        Assert.Equal(Min(25), _log.TodayTotal);
    }

    [Fact]
    public void Pausing_and_stopping_count_what_ran_once()
    {
        _timer.Start();
        Wait(10);
        _timer.Pause();
        Wait(30); // Paused: not focus.
        _timer.Resume();
        Wait(5);
        _timer.Stop();

        Assert.Equal(Min(15), _log.TodayTotal);
    }

    [Fact]
    public void Time_away_given_back_doesnt_count()
    {
        _timer.Start();
        Wait(12);
        _timer.PauseAfterIdle(Min(4)); // The last 4 minutes nobody was there.

        Assert.Equal(Min(8), _log.TodayTotal);
    }

    [Fact]
    public void Today_includes_the_session_running_now()
    {
        _timer.Start();
        Wait(7);

        Assert.Equal(Min(7), _log.TodayTotal);
        Assert.Equal(Min(7), _log.LastDays(7)[^1].Focus);
    }

    [Fact]
    public void Each_day_counts_on_its_own_and_the_week_ends_today()
    {
        _timer.Start();
        Wait(25);
        _timer.SkipBreak();
        _now = _now.AddDays(1);
        _timer.Start();
        Wait(10);
        _timer.Stop();

        var week = _log.LastDays(7);
        Assert.Equal(7, week.Count);
        Assert.Equal(_now.Date, week[^1].Day);
        Assert.Equal(Min(10), week[^1].Focus);
        Assert.Equal(Min(25), week[^2].Focus);
        Assert.Equal(TimeSpan.Zero, week[0].Focus);
    }

    [Fact]
    public void Goal_comes_from_the_settings()
    {
        Assert.Equal(TimeSpan.FromHours(2), _log.Goal);
        _store.Settings.FocusGoalMinutes = 90;
        Assert.Equal(Min(90), _log.Goal);
    }

    [Fact]
    public void The_task_is_kept_in_the_data()
    {
        var id = Guid.NewGuid();
        int changes = 0;
        _log.Changed += () => changes++;

        _log.TaskId = id;
        _log.TaskId = id;

        Assert.Equal(id, _store.Data.Focus.TaskId);
        Assert.Equal(1, changes);
    }

    [Theory]
    [InlineData(0, "0 min")]
    [InlineData(25, "25 min")]
    [InlineData(60, "1h")]
    [InlineData(100, "1h 40")]
    [InlineData(125, "2h 05")]
    public void Formats_minutes_and_hours(int minutes, string expected) =>
        Assert.Equal(expected, FocusLog.Format(Min(minutes)));
}
