using Yata.Services;

namespace Yata.Tests;

/// <summary>Switching to a player (⇄) and taking one off the notch (✕).</summary>
public class PlayerChoiceTests
{
    [Fact]
    public void A_removed_player_stays_off_while_it_keeps_playing()
    {
        var choice = new PlayerChoice();
        choice.Seen("spotify", true);
        choice.Remove("spotify");

        Assert.False(choice.Seen("spotify", true));
        Assert.True(choice.IsRemoved("spotify"));
    }

    [Fact]
    public void A_removed_player_comes_back_when_it_starts_playing_again()
    {
        var choice = new PlayerChoice();
        choice.Seen("youtube", true);
        choice.Remove("youtube");
        choice.Seen("youtube", false);

        Assert.True(choice.Seen("youtube", true));
        Assert.False(choice.IsRemoved("youtube"));
    }

    [Fact]
    public void A_chosen_player_holds_while_the_other_keeps_playing()
    {
        var choice = new PlayerChoice();
        choice.Seen("spotify", true);
        choice.Seen("youtube", false);
        choice.Choose("youtube");

        Assert.False(choice.Seen("spotify", true));
        Assert.Equal("youtube", choice.Chosen);
    }

    [Fact]
    public void Another_player_starting_ends_the_choice()
    {
        var choice = new PlayerChoice();
        choice.Seen("spotify", false);
        choice.Choose("youtube");

        Assert.True(choice.Seen("spotify", true));
        Assert.Null(choice.Chosen);
    }

    [Fact]
    public void The_chosen_player_starting_keeps_the_choice()
    {
        var choice = new PlayerChoice();
        choice.Seen("youtube", false);
        choice.Choose("youtube");

        Assert.False(choice.Seen("youtube", true));
        Assert.Equal("youtube", choice.Chosen);
    }

    [Fact]
    public void Removing_the_chosen_player_ends_the_choice()
    {
        var choice = new PlayerChoice();
        choice.Choose("youtube");
        choice.Remove("youtube");

        Assert.Null(choice.Chosen);
        Assert.True(choice.IsRemoved("youtube"));
    }

    [Fact]
    public void Closed_players_take_their_choices_with_them()
    {
        var choice = new PlayerChoice();
        choice.Seen("spotify", true);
        choice.Remove("spotify");
        choice.Choose("youtube");

        choice.Keep(new[] { "vlc" });

        Assert.False(choice.IsRemoved("spotify"));
        Assert.Null(choice.Chosen);
        // Seen again after reopening: the first sighting isn't a "start".
        Assert.False(choice.Seen("spotify", true));
    }
}

public class PlayerNamesTests
{
    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify")]
    [InlineData("Chrome", "Chrome")]
    [InlineData("MSEdge", "Edge")]
    [InlineData("308046B0AF4A39CB", "Firefox")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "Media Player")]
    [InlineData("Contoso.Podcasts_abc123!App", "Podcasts")]
    [InlineData(@"C:\Tools\mpv.exe", "Mpv")]
    public void Names_the_player(string appId, string name)
    {
        Assert.Equal(name, PlayerNames.Friendly(appId));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("A1B2C3D4E5F6A7B8")]
    public void Says_nothing_when_the_id_is_unreadable(string? appId)
    {
        Assert.Null(PlayerNames.Friendly(appId));
    }
}
