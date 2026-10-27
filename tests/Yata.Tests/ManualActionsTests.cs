using Yata.Helpers;
using Yata.Services;

namespace Yata.Tests;

/// <summary>The "do it now" actions: check for updates, sync, refresh. No network involved.</summary>
public class ManualActionsTests
{
    private DateTime _now = new(2026, 10, 27, 16, 42, 0);

    [Fact]
    public void A_click_goes_through_but_not_a_second_one_right_after()
    {
        var gate = new ManualGate(() => _now);
        Assert.True(gate.TryPass());
        _now = _now.AddSeconds(5);
        Assert.False(gate.TryPass());
        _now = _now.AddSeconds(ManualGate.Gap.TotalSeconds);
        Assert.True(gate.TryPass());
    }

    [Fact]
    public void The_clock_going_back_doesnt_lock_the_button()
    {
        var gate = new ManualGate(() => _now);
        gate.TryPass();
        _now = _now.AddHours(-1); // Time zone change, or the clock was corrected.
        Assert.True(gate.TryPass());
    }

    [Fact]
    public async Task Check_now_works_with_the_daily_check_off()
    {
        var store = AppStore.InMemory(); // UpdateCheckEnabled is off by default
        var checker = new UpdateChecker(store, () => _now, () => Task.FromResult<string?>("v99.0.0"));

        Assert.Equal(UpdateCheckResult.Available, await checker.CheckNowAsync());
        Assert.Equal("99.0.0", checker.Available);
        Assert.Equal(_now, checker.LastCheck);
    }

    [Fact]
    public async Task A_version_found_by_hand_isnt_announced_again_later()
    {
        var store = AppStore.InMemory();
        var checker = new UpdateChecker(store, () => _now, () => Task.FromResult<string?>("v99.0.0"));
        var messages = new List<HudMessage>();
        checker.Message += messages.Add;

        await checker.CheckNowAsync();
        checker.Announce();

        Assert.Empty(messages);
    }

    [Fact]
    public async Task Up_to_date_and_failure()
    {
        var store = AppStore.InMemory();
        string? answer = "v0.0.1";
        var checker = new UpdateChecker(store, () => _now, () => Task.FromResult(answer));
        Assert.Equal(UpdateCheckResult.UpToDate, await checker.CheckNowAsync());

        answer = null; // Offline, rate-limited, no release yet.
        _now = _now.AddMinutes(1);
        Assert.Equal(UpdateCheckResult.Failed, await checker.CheckNowAsync());
        Assert.Equal(_now.AddMinutes(-1), checker.LastCheck); // A failure isn't an answer.
    }

    [Fact]
    public async Task Clicking_again_right_away_reuses_the_answer()
    {
        var store = AppStore.InMemory();
        int requests = 0;
        var checker = new UpdateChecker(store, () => _now, () =>
        {
            requests++;
            return Task.FromResult<string?>("v0.0.1");
        });

        await checker.CheckNowAsync();
        _now = _now.AddSeconds(3);
        Assert.Equal(UpdateCheckResult.UpToDate, await checker.CheckNowAsync());
        Assert.Equal(1, requests);
    }

    [Fact]
    public void When_reads_today_or_the_day()
    {
        Assert.Equal("hoje às 16:42", Lang.When(_now, _now.AddHours(2)));
        Assert.Equal("26 de outubro às 09:05", Lang.When(new DateTime(2026, 10, 26, 9, 5, 0), _now));
    }
}
