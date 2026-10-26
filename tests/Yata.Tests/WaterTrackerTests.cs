using Yata.Services;

namespace Yata.Tests;

/// <summary>Daily water count, the goal celebration and reminders.</summary>
public class WaterTrackerTests
{
    private DateTime _now = new(2026, 10, 26, 10, 0, 0);
    private readonly AppStore _store = AppStore.InMemory();
    private readonly List<HudMessage> _messages = new();

    private WaterTracker NewTracker()
    {
        // Defaults: 2000 ml goal, 250 ml cups, a reminder every 60 min.
        var water = new WaterTracker(_store, () => _now);
        water.Message += _messages.Add;
        return water;
    }

    [Fact]
    public void Drinking_adds_a_cup_and_undo_never_goes_negative()
    {
        var water = NewTracker();
        water.Drink();
        water.Drink();
        Assert.Equal(500, water.TodayMl);

        water.Undo();
        water.Undo();
        water.Undo();
        Assert.Equal(0, water.TodayMl);
    }

    [Fact]
    public void Each_day_counts_on_its_own()
    {
        var water = NewTracker();
        water.Drink();
        _now = _now.AddDays(1);
        water.Drink();
        water.Drink();

        Assert.Equal(500, water.TodayMl);
        var days = water.LastDays(2);
        Assert.Equal(new[] { 250, 500 }, days.Select(d => d.Ml));
    }

    [Fact]
    public void Reaching_the_goal_celebrates_once()
    {
        var water = NewTracker();
        for (int i = 0; i < 7; i++)
            water.Drink();
        Assert.Empty(_messages);

        water.Drink(); // 2000 ml
        water.Drink(); // past the goal: no second celebration

        var message = Assert.Single(_messages);
        Assert.True(message.Celebrate);
        Assert.True(message.Important); // waits for the notch to close
        Assert.Equal("2000/2000 ml", message.Value);
    }

    [Fact]
    public void Reminds_after_the_interval_since_the_last_drink()
    {
        var water = NewTracker();
        water.Drink();

        _now = _now.AddMinutes(59);
        water.Poll();
        Assert.Empty(_messages);

        _now = _now.AddMinutes(2);
        water.Poll();
        var message = Assert.Single(_messages);
        Assert.Equal("Hora de beber água", message.Text);
        Assert.False(message.Celebrate);
    }

    [Fact]
    public void No_reminders_at_night_or_after_the_goal()
    {
        var water = NewTracker();
        _now = new DateTime(2026, 10, 26, 23, 0, 0);
        water.Poll();

        _now = new DateTime(2026, 10, 27, 10, 0, 0);
        for (int i = 0; i < 8; i++)
            water.Drink();
        _messages.Clear(); // the goal celebration
        _now = _now.AddHours(3);
        water.Poll();

        Assert.Empty(_messages);
    }
}
