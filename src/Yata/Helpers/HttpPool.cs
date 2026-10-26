using System;
using System.Net.Http;

namespace Yata.Helpers;

/// <summary>
/// One connection pool for every service that goes online (lyrics, weather, updates, Google).
/// Each service keeps its own client for its timeout and headers.
/// </summary>
internal static class HttpPool
{
    // Connections are renewed every few minutes: Yata stays open for days, and a pooled
    // connection would otherwise keep using an address resolved long ago.
    private static readonly SocketsHttpHandler Handler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    };

    /// <summary>Names the app and its real version, as LRCLIB and GitHub ask clients to.</summary>
    // Read here rather than from UpdateChecker.Current: UpdateChecker builds its client during its
    // own static initialization, before that property is set.
    public static string UserAgent { get; } =
        $"Yata/{typeof(HttpPool).Assembly.GetName().Version?.ToString(3) ?? "0"} (open-source Windows notch app)";

    public static HttpClient Client(TimeSpan timeout, Uri? baseAddress = null)
    {
        var client = new HttpClient(Handler, disposeHandler: false) { Timeout = timeout, BaseAddress = baseAddress };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }
}
