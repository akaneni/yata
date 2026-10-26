using System.IO;
using System.Windows.Threading;
using Yata.Services;

namespace Yata.Tests;

/// <summary>The pieces behind --bench: a made-up track and lyrics that never need the network.</summary>
public class BenchTests
{
    [Fact]
    public void Simulated_track_plays_from_the_start()
    {
        var media = new MediaService(Dispatcher.CurrentDispatcher);
        int changes = 0;
        media.Changed += () => changes++;

        media.Simulate("Medição", "Yata", TimeSpan.FromMinutes(10));

        Assert.Equal(1, changes);
        var state = media.Current!;
        Assert.True(state.IsPlaying);
        Assert.Equal(("Medição", "Yata"), (state.Title, state.Artist));
        Assert.Equal(TimeSpan.FromMinutes(10), state.Duration);
        Assert.True(state.CurrentPosition < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Seeded_lyrics_are_found_in_the_cache_without_a_search()
    {
        string cacheDir = Path.Combine(Path.GetTempPath(), "yata-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var lyrics = new LyricsService(cacheDir);
            // The names the bench uses; the cache key must match what a lookup of the track computes.
            lyrics.Seed("Yata", "Medição", "[00:00.00] Linha 1\n[00:04.00] Linha 2\n");
            var media = new MediaState("Medição", "Yata", "", true, TimeSpan.Zero, TimeSpan.FromMinutes(10),
                DateTimeOffset.Now, 1, null);

            // A cache miss would go to LRCLIB; cancelling up front makes any search fail the test.
            var found = await lyrics.FindAsync(media, new CancellationToken(canceled: true));

            Assert.NotNull(found);
            Assert.Equal(2, found!.Lines.Count);
        }
        finally
        {
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, recursive: true);
        }
    }
}
