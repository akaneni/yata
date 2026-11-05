using System;
using System.Linq;

namespace Yata.Services;

/// <summary>A media session's app id (AUMID) as a name for a tooltip: "Spotify", "Chrome", "Media Player".</summary>
internal static class PlayerNames
{
    private static readonly (string Match, string Name)[] Known =
    {
        ("spotify", "Spotify"),
        ("msedge", "Edge"),
        ("chrome", "Chrome"),
        ("firefox", "Firefox"),
        ("308046b0af4a39cb", "Firefox"), // Firefox's app id is a hash of its install path's default.
        ("opera", "Opera"),
        ("brave", "Brave"),
        ("vivaldi", "Vivaldi"),
        ("zunemusic", "Media Player"),
        ("zunevideo", "Movies & TV"),
        ("vlc", "VLC"),
        ("foobar2000", "foobar2000"),
        ("applemusic", "Apple Music"),
        ("itunes", "iTunes"),
        ("deezer", "Deezer"),
        ("tidal", "TIDAL"),
    };

    /// <summary>The name, or null when the id says nothing readable (a bare hash).</summary>
    public static string? Friendly(string? appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
            return null;
        string id = appId.Trim();
        foreach (var (match, name) in Known)
            if (id.Contains(match, StringComparison.OrdinalIgnoreCase))
                return name;

        // "Company.App_publisherhash!App" (a Store app): the package name's last part.
        if (id.Contains('!'))
        {
            string package = id[..id.IndexOf('!')];
            int underscore = package.IndexOf('_');
            if (underscore > 0)
                package = package[..underscore];
            return Readable(package.Split('.').Last());
        }
        // "C:\...\app.exe" or "app.exe": the file name.
        string file = id.Split('\\', '/').Last();
        if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            file = file[..^4];
        return Readable(file);
    }

    private static string? Readable(string name)
    {
        if (name.Length == 0 || (name.Length >= 12 && name.All(Uri.IsHexDigit)))
            return null;
        return char.ToUpperInvariant(name[0]) + name[1..];
    }
}
