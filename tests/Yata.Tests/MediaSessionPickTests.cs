using Yata.Services;

namespace Yata.Tests;

/// <summary>Which media session the notch follows when several players are open.</summary>
public class MediaSessionPickTests
{
    private static SessionCandidate S(bool playing = false, bool shown = false, bool current = false, bool untitled = false) =>
        new(playing, shown, current, untitled);

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

    [Fact]
    public void An_untitled_sound_does_not_hide_a_paused_player()
    {
        // Spotify paused on screen, then a game's sound (no title) plays and Windows makes it current.
        Assert.Equal(0, MediaService.Pick(new[] { S(shown: true), S(playing: true, current: true, untitled: true) }));
    }

    [Fact]
    public void Leaves_an_untitled_session_it_was_showing_for_a_titled_one()
    {
        Assert.Equal(1, MediaService.Pick(new[] { S(playing: true, shown: true, untitled: true), S() }));
    }

    [Fact]
    public void Shows_an_untitled_session_when_there_is_nothing_else()
    {
        Assert.Equal(1, MediaService.Pick(new[] { S(untitled: true), S(playing: true, untitled: true) }));
    }
}
