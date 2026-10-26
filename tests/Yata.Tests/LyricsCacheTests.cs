using System.IO;
using Yata.Services;

namespace Yata.Tests;

/// <summary>The lyrics cache keeps the most recent songs and drops the rest.</summary>
public sealed class LyricsCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "yata-tests", Guid.NewGuid().ToString("N"));

    public LyricsCacheTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Prune_keeps_the_most_recently_written()
    {
        var start = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 5; i++)
        {
            string file = Path.Combine(_dir, $"{i}.lrc");
            File.WriteAllText(file, "");
            File.SetLastWriteTimeUtc(file, start.AddDays(i));
        }

        new LyricsService(_dir).Prune(keep: 2);

        Assert.Equal(new[] { "3.lrc", "4.lrc" }, Directory.GetFiles(_dir).Select(Path.GetFileName).Order());
    }
}
