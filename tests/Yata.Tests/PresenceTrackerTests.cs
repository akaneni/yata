using Yata.Services;

namespace Yata.Tests;

/// <summary>Idle / asleep states, going away and the "while you were away" report.</summary>
public class PresenceTrackerTests
{
    // Defaults: Yata shows up after 2 min, dozes off after 10.
    private readonly SettingsData _settings = new();
    private readonly List<AwayReport> _reports = new();
    private readonly List<TimeSpan> _wentAway = new();

    private PresenceTracker NewTracker()
    {
        var presence = new PresenceTracker(_settings);
        presence.Returned += _reports.Add;
        presence.WentAway += _wentAway.Add;
        return presence;
    }

    private static TimeSpan Min(double minutes) => TimeSpan.FromMinutes(minutes);

    [Theory]
    [InlineData(0.5, Presence.Active)]
    [InlineData(2, Presence.Idle)]
    [InlineData(9.9, Presence.Idle)]
    [InlineData(10, Presence.Asleep)]
    public void State_follows_inactivity(double idleMinutes, Presence expected)
    {
        var presence = NewTracker();
        presence.Simulate(Min(idleMinutes));

        Assert.Equal(expected, presence.State);
    }

    [Fact]
    public void Sleep_always_comes_after_idle()
    {
        _settings.CompanionIdleMinutes = 15;
        _settings.CompanionSleepMinutes = 5;
        var presence = NewTracker();

        Assert.True(presence.SleepAfter > presence.IdleAfter);
    }

    [Fact]
    public void Goes_away_once_after_five_minutes()
    {
        var presence = NewTracker();
        presence.Simulate(Min(4));
        Assert.Empty(_wentAway);

        presence.Simulate(Min(5));
        presence.Simulate(Min(8));

        Assert.Equal(new[] { Min(5) }, _wentAway);
        Assert.True(presence.IsAway);
    }

    [Fact]
    public void Coming_back_reports_what_was_missed()
    {
        var presence = NewTracker();
        presence.Simulate(Min(1));
        presence.NoteIfUnseen(new HudMessage(Glyphs.Water, "Hora de beber água", "750/2000 ml"));
        presence.Simulate(Min(12));
        presence.Simulate(TimeSpan.Zero);

        var report = Assert.Single(_reports);
        Assert.Equal(Min(12), report.Duration);
        var item = Assert.Single(report.Items);
        Assert.Equal("Hora de beber água · 750/2000 ml", item.Text);
        Assert.False(presence.IsAway);
        Assert.Equal(Presence.Active, presence.State);
    }

    [Fact]
    public void Notices_seen_at_the_screen_are_not_reported()
    {
        var presence = NewTracker();
        presence.Simulate(TimeSpan.FromSeconds(10));
        presence.NoteIfUnseen(new HudMessage(Glyphs.Bell, "Reunião"));
        presence.Simulate(Min(3));
        presence.Simulate(TimeSpan.Zero);

        var report = Assert.Single(_reports);
        Assert.Empty(report.Items);
    }

    [Fact]
    public void A_short_pause_is_not_a_return()
    {
        var presence = NewTracker();
        presence.Simulate(TimeSpan.FromSeconds(40));
        presence.Simulate(TimeSpan.Zero);

        Assert.Empty(_reports);
    }
}
