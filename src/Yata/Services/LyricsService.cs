using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Yata.Helpers;

namespace Yata.Services;

public sealed record LyricLine(TimeSpan Time, string Text);

public sealed class Lyrics
{
    public IReadOnlyList<LyricLine> Lines { get; }

    public Lyrics(IReadOnlyList<LyricLine> lines) => Lines = lines;

    /// <summary>Index of the line being sung at <paramref name="position"/>, or -1 before the first line.</summary>
    public int IndexAt(TimeSpan position)
    {
        int lo = 0, hi = Lines.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (Lines[mid].Time <= position)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return found;
    }

    public static Lyrics? Parse(string lrc)
    {
        var lines = new List<LyricLine>();
        foreach (var raw in lrc.Split('\n'))
        {
            var stamps = TimeStamp.Matches(raw);
            if (stamps.Count == 0)
                continue;

            string text = raw[(stamps[^1].Index + stamps[^1].Length)..].Trim();
            foreach (Match stamp in stamps)
            {
                double seconds = int.Parse(stamp.Groups[1].Value, CultureInfo.InvariantCulture) * 60 +
                                 double.Parse(stamp.Groups[2].Value, CultureInfo.InvariantCulture);
                lines.Add(new LyricLine(TimeSpan.FromSeconds(seconds), text));
            }
        }

        return lines.Count == 0 ? null : new Lyrics(lines.OrderBy(l => l.Time).ToList());
    }

    private static readonly Regex TimeStamp = new(@"\[(\d+):(\d+(?:\.\d+)?)\]", RegexOptions.Compiled);
}

/// <summary>
/// Looks up time-synced lyrics on LRCLIB (https://lrclib.net), a free, open, account-less service.
/// Only artist, title and duration are sent. Results, including misses, are cached on disk.
/// </summary>
public sealed class LyricsService
{
    private static readonly TimeSpan MissExpiry = TimeSpan.FromDays(3);
    private static readonly HttpClient Http = CreateClient();

    private readonly string _cacheDir;

    public LyricsService() : this(Path.Combine(AppPaths.DataDir, "lyrics"))
    {
    }

    internal LyricsService(string cacheDir) => _cacheDir = cacheDir;

    public async Task<Lyrics?> FindAsync(MediaState media, CancellationToken ct)
    {
        var (artist, title, fromBrowser) = TrackQuery.From(media);
        if (title.Length == 0)
            return null;

        string cacheFile = Path.Combine(_cacheDir, CacheKey(artist, title) + ".lrc");
        if (TryReadCache(cacheFile, out var cached))
            return cached.Length == 0 ? null : Lyrics.Parse(cached);

        // Music videos run longer than the album track, so only prefer the closest duration there.
        double maxDurationDiff = fromBrowser ? double.MaxValue : 4;
        string? lrc = await SearchAsync(artist, title, media.Duration, maxDurationDiff, ct);
        WriteCache(cacheFile, lrc ?? "");
        return lrc == null ? null : Lyrics.Parse(lrc);
    }

    /// <summary>
    /// Caches lyrics as if LRCLIB had returned them, so measurements (--bench) never touch the
    /// network. The names must already be clean: <see cref="TrackQuery"/> would leave them as they are.
    /// </summary>
    internal void Seed(string artist, string title, string lrc) =>
        WriteCache(Path.Combine(_cacheDir, CacheKey(artist, title) + ".lrc"), lrc);

    private static async Task<string?> SearchAsync(string artist, string title, TimeSpan duration, double maxDurationDiff, CancellationToken ct)
    {
        var queries = new List<string>();
        if (artist.Length > 0)
            queries.Add($"api/search?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}");
        queries.Add($"api/search?q={Uri.EscapeDataString((artist + " " + title).Trim())}");

        foreach (var query in queries)
        {
            try
            {
                using var response = await Http.GetAsync(query, ct);
                if (!response.IsSuccessStatusCode)
                    continue;

                await using var body = await response.Content.ReadAsStreamAsync(ct);
                using var json = await JsonDocument.ParseAsync(body, cancellationToken: ct);
                string? best = PickBest(json.RootElement, duration, maxDurationDiff);
                if (best != null)
                    return best;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                // Offline or service hiccup: try the next query, otherwise report "not found".
            }
        }
        return null;
    }

    /// <summary>The synced result whose duration is closest to the playing track.</summary>
    private static string? PickBest(JsonElement results, TimeSpan duration, double maxDurationDiff)
    {
        if (results.ValueKind != JsonValueKind.Array)
            return null;

        string? best = null;
        double bestDiff = double.MaxValue;
        foreach (var item in results.EnumerateArray())
        {
            if (!item.TryGetProperty("syncedLyrics", out var synced) || synced.ValueKind != JsonValueKind.String)
                continue;

            double diff = 0;
            if (duration > TimeSpan.Zero && item.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number)
                diff = Math.Abs(d.GetDouble() - duration.TotalSeconds);

            // A different cut of the song (radio edit, live) would drift out of sync.
            if (diff > maxDurationDiff)
                continue;

            if (diff < bestDiff)
            {
                bestDiff = diff;
                best = synced.GetString();
            }
        }
        return best;
    }

    private static bool TryReadCache(string file, out string content)
    {
        content = "";
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists || (info.Length == 0 && DateTime.UtcNow - info.LastWriteTimeUtc > MissExpiry))
                return false;
            content = File.ReadAllText(file);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private void WriteCache(string file, string content)
    {
        try
        {
            Directory.CreateDirectory(_cacheDir);
            File.WriteAllText(file, content);
        }
        catch (IOException)
        {
            // Cache is best effort.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string CacheKey(string artist, string title)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes((artist + "\n" + title).ToLowerInvariant()));
        return Convert.ToHexString(bytes);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { BaseAddress = new Uri("https://lrclib.net/"), Timeout = TimeSpan.FromSeconds(10) };
        // LRCLIB asks clients to identify themselves.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Yata/0.1 (open-source Windows notch app)");
        return client;
    }
}

/// <summary>Turns what a player reports into a clean artist/title pair for searching.</summary>
internal static class TrackQuery
{
    private static readonly Regex Noise = new(
        @"\s*[\(\[][^\)\]]*\b(official|video|lyrics?|letra|audio|visuali[sz]er|clipe|clip|remaster(ed)?|hd|4k|mv)\b[^\)\]]*[\)\]]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Featuring = new(@"\s+[\(\[]?(ft\.?|feat\.?|featuring)\s.*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex VersionSuffix = new(
        @"\s+-\s+.*\b(remaster(ed)?|live|ao vivo|version|vers[aã]o|edit|mix|mono|stereo)\b.*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ChannelNoise = new(@"(\s*-\s*Topic|VEVO|\s+Official)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static (string Artist, string Title, bool FromBrowser) From(MediaState media)
    {
        string artist = media.Artist.Trim();
        string title = media.Title.Trim();

        // Browsers report the channel as artist and "Artist - Song" as the title.
        bool isBrowser = Regex.IsMatch(media.SourceApp, "chrome|msedge|firefox|brave|opera|vivaldi", RegexOptions.IgnoreCase);
        int dash = title.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0 && (isBrowser || artist.Length == 0))
        {
            artist = title[..dash];
            title = title[(dash + 3)..];
        }

        title = Featuring.Replace(Noise.Replace(VersionSuffix.Replace(title, ""), ""), "").Trim();
        artist = ChannelNoise.Replace(artist, "").Trim();
        return (artist, title, isBrowser);
    }
}
