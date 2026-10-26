using Yata.Services;

namespace Yata.Tests;

/// <summary>Which media session the notch follows when several players are open.</summary>
public class MediaSessionPickTests
{
    private static SessionCandidate S(bool playing = false, bool shown = false, bool current = false) =>
        new(playing, shown, current);

    [Fact]
    public void No_sessions_means_nothing_to_show()
    {
        Assert.Equal(-1, MediaService.Pick(Array.Empty<SessionCandidate>()));
    }

    [Fact]
    public void Keeps_the_shown_player_while_it_plays()
    {
        // Windows moved "current" to another player that also plays: don't flip back and forth.
        Assert.Equal(0, MediaService.Pick(new[] { S(playing: true, shown: true), S(playing: true, current: true) }));
    }

    [Fact]
    public void Switches_to_a_player_that_starts_while_the_shown_one_is_paused()
    {
        // e.g. Spotify paused on screen, then a video starts in the browser.
        Assert.Equal(1, MediaService.Pick(new[] { S(shown: true, current: true), S(playing: true) }));
    }

    [Fact]
    public void Prefers_the_system_current_one_among_several_playing()
    {
        Assert.Equal(2, MediaService.Pick(new[] { S(shown: true), S(playing: true), S(playing: true, current: true) }));
    }

    [Fact]
    public void With_nothing_playing_stays_on_the_paused_player_shown()
    {
        Assert.Equal(1, MediaService.Pick(new[] { S(current: true), S(shown: true) }));
    }

    [Fact]
    public void Falls_back_to_the_system_current_session()
    {
        Assert.Equal(1, MediaService.Pick(new[] { S(), S(current: true) }));
        Assert.Equal(0, MediaService.Pick(new[] { S(), S() }));
    }
}
