using Yata.Services;

namespace Yata.Tests;

/// <summary>LRC parsing and picking the line being sung.</summary>
public class LyricsTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Parses_time_stamps_and_text()
    {
        var lyrics = Lyrics.Parse("[00:01.50] First line\n[01:02.25]Second line\n")!;

        Assert.Equal(2, lyrics.Lines.Count);
        Assert.Equal(new LyricLine(S(1.5), "First line"), lyrics.Lines[0]);
        Assert.Equal(new LyricLine(S(62.25), "Second line"), lyrics.Lines[1]);
    }

    [Fact]
    public void A_line_with_several_stamps_repeats_at_each_time_in_order()
    {
        var lyrics = Lyrics.Parse("[00:30.00]Chorus\n[00:10.00][00:50.00]Hook\n")!;

        Assert.Equal(new[] { S(10), S(30), S(50) }, lyrics.Lines.Select(l => l.Time));
        Assert.Equal(new[] { "Hook", "Chorus", "Hook" }, lyrics.Lines.Select(l => l.Text));
    }

    [Fact]
    public void Ignores_metadata_and_returns_null_without_timed_lines()
    {
        Assert.Null(Lyrics.Parse("[ar: Someone]\n[ti: Song]\nplain text"));
        var lyrics = Lyrics.Parse("[ar: Someone]\n[00:05.00]Only line")!;
        Assert.Single(lyrics.Lines);
    }

    [Fact]
    public void Keeps_empty_lines_as_instrumental_breaks()
    {
        var lyrics = Lyrics.Parse("[00:05.00]Words\n[00:09.00]\n[00:12.00]More")!;

        Assert.Equal("", lyrics.Lines[1].Text);
    }

    [Theory]
    [InlineData(0, -1)]      // before the first line
    [InlineData(4.99, -1)]
    [InlineData(5, 0)]       // exactly on a stamp
    [InlineData(8, 0)]
    [InlineData(9, 1)]
    [InlineData(600, 2)]     // long after the last line
    public void Index_is_the_last_line_that_started(double position, int expected)
    {
        var lyrics = Lyrics.Parse("[00:05.00]One\n[00:09.00]Two\n[00:12.00]Three")!;

        Assert.Equal(expected, lyrics.IndexAt(S(position)));
    }

    [Theory]
    [InlineData(0, 5)]       // before the first line: wait for it
    [InlineData(5, 4)]       // on a stamp: the next one
    [InlineData(10.5, 1.5)]
    public void Time_until_the_next_line_starts(double position, double expected)
    {
        var lyrics = Lyrics.Parse("[00:05.00]One\n[00:09.00]Two\n[00:12.00]Three")!;

        Assert.Equal(S(expected), lyrics.UntilNextLine(S(position)));
    }

    [Fact]
    public void Nothing_to_wait_for_after_the_last_line()
    {
        var lyrics = Lyrics.Parse("[00:05.00]One\n[00:09.00]Two")!;

        Assert.Null(lyrics.UntilNextLine(S(9)));
    }
}
