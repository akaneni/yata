using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Yata.Helpers;

namespace Yata.Services;

/// <summary>
/// The OAuth client registered in Google Cloud as a "Desktop app". Google treats an installed app's
/// secret as not confidential (PKCE protects the flow), but it stays out of the repository so forks
/// register their own client instead of borrowing ours.
/// </summary>
internal sealed record GoogleClient(string Id, string Secret)
{
    /// <summary>The JSON downloaded from Google Cloud ("Download JSON"), for builds from source.</summary>
    public static string OverrideFile => Path.Combine(AppPaths.DataDir, "google-client.json");

    public static GoogleClient? Load() => FromFile(OverrideFile) ?? FromAssembly();

    private static GoogleClient? FromFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var root = json.RootElement;
            if (root.TryGetProperty("installed", out var installed))
                root = installed;
            return Create(root.TryGetProperty("client_id", out var id) ? id.GetString() : null,
                          root.TryGetProperty("client_secret", out var secret) ? secret.GetString() : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            Log.Warn("google", "google-client.json ilegível; usando o cliente embutido", ex);
            return null;
        }
    }

    /// <summary>Official releases: baked in at build time from the YATA_GOOGLE_CLIENT_* variables.</summary>
    private static GoogleClient? FromAssembly()
    {
        var metadata = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().ToList();
        string? Value(string key) => metadata.FirstOrDefault(m => m.Key == key)?.Value;
        return Create(Value("GoogleClientId"), Value("GoogleClientSecret"));
    }

    private static GoogleClient? Create(string? id, string? secret) =>
        string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret) ? null : new GoogleClient(id.Trim(), secret.Trim());
}

/// <summary>What's kept between runs, encrypted with DPAPI. The access token lives only in memory.</summary>
internal sealed class GoogleCredential
{
    public string RefreshToken { get; set; } = "";
    public string Email { get; set; } = "";
    /// <summary>Scopes granted so far; null for accesses saved before tasks existed (calendar only).</summary>
    public List<string>? Scopes { get; set; }
}

public sealed class GoogleAuthException : Exception
{
    /// <summary>The saved access no longer works (revoked, expired): the user has to sign in again.</summary>
    public bool NeedsReconnect { get; }

    public GoogleAuthException(string message, bool needsReconnect = false) : base(message) => NeedsReconnect = needsReconnect;
}

/// <summary>A Google API answered with an error status.</summary>
public sealed class GoogleApiException : HttpRequestException
{
    public string Body { get; }

    public GoogleApiException(HttpStatusCode status, string body)
        : base($"O Google respondeu {(int)status}.", null, status) => Body = body;

    /// <summary>The API is switched off in the Cloud project: a setup problem, not a data one.</summary>
    public bool ApiDisabled => Body.Contains("accessNotConfigured", StringComparison.Ordinal) ||
                               Body.Contains("SERVICE_DISABLED", StringComparison.Ordinal);
}

/// <summary>
/// "Sign in with Google" for an installed app, as Google recommends: the system browser, a one-shot
/// listener on 127.0.0.1 for the redirect, PKCE and a state check. Each feature asks for its own
/// scopes when it's turned on (incremental authorization), plus the e-mail address to show which
/// account is connected.
/// </summary>
public sealed class GoogleAuth
{
    public const string CalendarListScope = "https://www.googleapis.com/auth/calendar.calendarlist.readonly";
    public const string EventsScope = "https://www.googleapis.com/auth/calendar.events.readonly";
    public const string TasksScope = "https://www.googleapis.com/auth/tasks";
    public static readonly string[] CalendarScopes = { CalendarListScope, EventsScope };
    public static readonly string[] TasksScopes = { TasksScope };

    private const string AuthorizeUrl = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenUrl = "https://oauth2.googleapis.com/token";
    private const string RevokeUrl = "https://oauth2.googleapis.com/revoke";
    private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);
    private static readonly HttpClient Http = HttpPool.Client(TimeSpan.FromSeconds(20));

    private readonly string _file = Path.Combine(AppPaths.DataDir, "google.dat");
    private readonly GoogleClient? _client = GoogleClient.Load();
    private GoogleCredential? _credential;
    private string? _accessToken;
    private DateTime _accessExpiresUtc;

    public GoogleAuth() => _credential = SecretFile.Read<GoogleCredential>(_file);

    /// <summary>False when this build has no Google client (built from source without one).</summary>
    public bool IsConfigured => _client != null;

    public bool IsSignedIn => _credential != null && !NeedsReconnect;

    public bool NeedsReconnect { get; private set; }

    public string? Email => _credential?.Email;

    /// <summary>Whether the saved access covers these scopes.</summary>
    public bool Has(IEnumerable<string> scopes)
    {
        if (_credential == null)
            return false;
        var granted = _credential.Scopes ?? CalendarScopes.ToList();
        return scopes.All(granted.Contains);
    }

    /// <summary>
    /// Signs in asking for <paramref name="wanted"/> on top of whatever was already granted. People can
    /// untick permissions on Google's screen; the access is kept as long as it's good for something.
    /// </summary>
    /// <returns>Whether every scope in <paramref name="wanted"/> was granted.</returns>
    public async Task<bool> SignInAsync(string[] wanted, CancellationToken ct)
    {
        var client = _client ?? throw new GoogleAuthException(Strings.GoogleAuth_NoClient);

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        string redirect = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";

        string verifier = Pkce.NewVerifier();
        string state = Pkce.NewVerifier();
        string url = AuthorizeUrl + "?" + Form(
            ("client_id", client.Id),
            ("redirect_uri", redirect),
            ("response_type", "code"),
            ("scope", string.Join(' ', new[] { "openid", "email" }.Concat(wanted))),
            // The new access also covers what was granted before, so turning on tasks keeps the calendar.
            ("include_granted_scopes", "true"),
            ("code_challenge", Pkce.Challenge(verifier)),
            ("code_challenge_method", "S256"),
            ("state", state),
            // A refresh token, so the user signs in once and not every hour.
            ("access_type", "offline"),
            ("prompt", "consent"));
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(SignInTimeout);
        string code = await WaitForCodeAsync(listener, state, timeout.Token);

        JsonDocument tokens;
        try
        {
            tokens = await PostAsync(TokenUrl, ct,
                ("client_id", client.Id),
                ("client_secret", client.Secret),
                ("code", code),
                ("code_verifier", verifier),
                ("grant_type", "authorization_code"),
                ("redirect_uri", redirect));
        }
        catch (TokenEndpointException ex)
        {
            throw new GoogleAuthException(string.Format(Strings.GoogleAuth_RefusedCode, ex.Error));
        }
        using var _ = tokens;
        var root = tokens.RootElement;

        string? refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        if (string.IsNullOrEmpty(refresh))
            throw new GoogleAuthException(Strings.GoogleAuth_NoRefresh);

        // Google lets people untick individual permissions on the consent screen.
        string granted = root.TryGetProperty("scope", out var s) ? s.GetString() ?? "" : "";
        if (!HasScopes(granted, CalendarScopes) && !HasScopes(granted, TasksScopes))
        {
            // Good for nothing: don't keep an access the user didn't mean to give.
            await RevokeAsync(refresh);
            throw new GoogleAuthException(Strings.GoogleAuth_NothingGranted);
        }

        _credential = new GoogleCredential
        {
            RefreshToken = refresh,
            Email = root.TryGetProperty("id_token", out var id) ? EmailFromIdToken(id.GetString()) ?? "" : "",
            Scopes = granted.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList(),
        };
        NeedsReconnect = false;
        SetAccessToken(root);
        SecretFile.Write(_file, _credential);
        return HasScopes(granted, wanted);
    }

    /// <summary>
    /// An API call with the access token; a rejected token is refreshed once and the call retried.
    /// Returns the response body, or throws <see cref="GoogleApiException"/>.
    /// </summary>
    public async Task<string> SendAsync(HttpMethod method, string url, string? json, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            string token = await GetAccessTokenAsync(forceRefresh: attempt > 0, ct);
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            if (json != null)
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await Http.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                continue;

            string body = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
                return body;
            throw new GoogleApiException(response.StatusCode, body);
        }
    }

    /// <summary>A valid access token, refreshed when it's about to expire (or when Google rejected it).</summary>
    public async Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct)
    {
        if (!forceRefresh && _accessToken != null && DateTime.UtcNow < _accessExpiresUtc)
            return _accessToken;

        var client = _client ?? throw new GoogleAuthException(Strings.GoogleAuth_NoClient);
        var credential = _credential;
        if (credential == null || NeedsReconnect)
            throw new GoogleAuthException(Strings.GoogleAuth_NotConnected, needsReconnect: true);

        try
        {
            using var tokens = await PostAsync(TokenUrl, ct,
                ("client_id", client.Id),
                ("client_secret", client.Secret),
                ("refresh_token", credential.RefreshToken),
                ("grant_type", "refresh_token"));
            SetAccessToken(tokens.RootElement);
            return _accessToken!;
        }
        catch (TokenEndpointException ex) when (ex.Error == "invalid_grant")
        {
            // Revoked in the Google account, password changed, or expired (test-mode clients: 7 days).
            NeedsReconnect = true;
            _accessToken = null;
            throw new GoogleAuthException(Strings.GoogleAuth_Expired, needsReconnect: true);
        }
        catch (TokenEndpointException ex)
        {
            throw new GoogleAuthException(string.Format(Strings.GoogleAuth_RenewFailed, ex.Error));
        }
    }

    /// <summary>Revokes the access at Google (best effort) and forgets it locally.</summary>
    public async Task SignOutAsync()
    {
        string? refresh = _credential?.RefreshToken;
        _credential = null;
        _accessToken = null;
        NeedsReconnect = false;
        SecretFile.Delete(_file);
        if (refresh != null)
            await RevokeAsync(refresh);
    }

    private void SetAccessToken(JsonElement root)
    {
        _accessToken = root.GetProperty("access_token").GetString();
        int seconds = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out int v) ? v : 3600;
        // Renew a minute early so a request never goes out with a token about to die.
        _accessExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, seconds - 60));
    }

    internal static async Task<string> WaitForCodeAsync(TcpListener listener, string state, CancellationToken ct)
    {
        while (true)
        {
            using var connection = await listener.AcceptTcpClientAsync(ct);
            await using var stream = connection.GetStream();
            Dictionary<string, string>? query;
            using (var read = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                // A connection that never sends its request must not hold up the real redirect.
                read.CancelAfter(TimeSpan.FromSeconds(10));
                try
                {
                    query = await ReadQueryAsync(stream, read.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    continue;
                }
            }

            // Browsers also ask for /favicon.ico; and only Google's redirect carries our state.
            if (query == null || !query.TryGetValue("state", out var got) ||
                !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(got), Encoding.UTF8.GetBytes(state)))
            {
                await RespondAsync(connection, "404 Not Found", "", ct);
                continue;
            }

            if (query.TryGetValue("code", out var code) && code.Length > 0)
            {
                await RespondAsync(connection, "200 OK", ResultPage(Strings.GoogleAuth_PageConnected, Strings.GoogleAuth_PageConnectedText), ct);
                return code;
            }

            await RespondAsync(connection, "200 OK", ResultPage(Strings.GoogleAuth_PageCancelled, Strings.GoogleAuth_PageCancelledText), ct);
            throw new GoogleAuthException(query.GetValueOrDefault("error") == "access_denied"
                ? Strings.GoogleAuth_Cancelled
                : Strings.GoogleAuth_Refused);
        }
    }

    private static async Task<Dictionary<string, string>?> ReadQueryAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        string? requestLine = await reader.ReadLineAsync(ct);

        // Read the headers to the end too: closing a socket with unread data makes Windows reset
        // the connection, and the browser then drops the page it was sent (blank tab).
        for (int i = 0; i < 100; i++)
        {
            string? header = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(header))
                break;
        }

        // "GET /?state=...&code=... HTTP/1.1"
        var parts = requestLine?.Split(' ');
        return parts is { Length: 3 } && parts[0] == "GET" ? ParseRedirect(parts[1]) : null;
    }

    /// <summary>The query of a redirect to "/", or null for any other path.</summary>
    internal static Dictionary<string, string>? ParseRedirect(string target)
    {
        int q = target.IndexOf('?');
        string path = q < 0 ? target : target[..q];
        if (path != "/")
            return null;

        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        if (q < 0)
            return query;
        foreach (var pair in target[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            string key = Decode(eq < 0 ? pair : pair[..eq]);
            query[key] = eq < 0 ? "" : Decode(pair[(eq + 1)..]);
        }
        return query;

        static string Decode(string s) => Uri.UnescapeDataString(s.Replace('+', ' '));
    }

    private static async Task RespondAsync(TcpClient connection, string status, string html, CancellationToken ct)
    {
        var stream = connection.GetStream();
        byte[] body = Encoding.UTF8.GetBytes(html);
        byte[] head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\n" +
            "Cache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
        // Graceful close (FIN) so the browser reads the whole page before the socket goes away.
        connection.Client.Shutdown(SocketShutdown.Send);
    }

    private static string ResultPage(string title, string text) =>
        $"<!doctype html><html lang=\"{Lang.Culture.Name}\"><head><meta charset=\"utf-8\"><title>Yata</title>" +
        "<style>body{margin:0;height:100vh;display:grid;place-items:center;background:#000;color:#fff;" +
        "font-family:'Segoe UI Variable Text','Segoe UI',sans-serif}h1{font-size:20px;font-weight:600;margin:0 0 6px}" +
        "p{margin:0;color:#ffffff99;font-size:14px}</style></head>" +
        $"<body><main><h1>{WebUtility.HtmlEncode(title)}</h1><p>{WebUtility.HtmlEncode(text)}</p></main></body></html>";

    internal static bool HasScopes(string granted, params string[] required)
    {
        var set = granted.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        return required.All(set.Contains);
    }

    /// <summary>
    /// The id_token came straight from Google's token endpoint over TLS, so its claims can be read
    /// without checking the signature (OpenID Connect Core, 3.1.3.7).
    /// </summary>
    internal static string? EmailFromIdToken(string? idToken)
    {
        var parts = idToken?.Split('.');
        if (parts is not { Length: 3 })
            return null;
        try
        {
            string payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var json = JsonDocument.Parse(Convert.FromBase64String(payload));
            return json.RootElement.TryGetProperty("email", out var email) ? email.GetString() : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    private static async Task<JsonDocument> PostAsync(string url, CancellationToken ct, params (string Key, string Value)[] fields)
    {
        using var content = new FormUrlEncodedContent(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)));
        using var response = await Http.PostAsync(url, content, ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        JsonDocument json;
        try
        {
            json = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw new HttpRequestException($"Resposta inesperada do Google ({(int)response.StatusCode}).");
        }

        if (!response.IsSuccessStatusCode)
        {
            string error = json.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString() ?? "" : "";
            json.Dispose();
            throw new TokenEndpointException(error.Length > 0 ? error : $"HTTP {(int)response.StatusCode}");
        }
        return json;
    }

    private static async Task RevokeAsync(string token)
    {
        try
        {
            using var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("token", token) });
            using var _ = await Http.PostAsync(RevokeUrl, content);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Offline: the local copy is gone anyway; the user can also revoke at myaccount.google.com.
        }
    }

    private static string Form(params (string Key, string Value)[] fields) =>
        string.Join('&', fields.Select(f => Uri.EscapeDataString(f.Key) + "=" + Uri.EscapeDataString(f.Value)));
}

/// <summary>An OAuth error from the token endpoint, e.g. "invalid_grant".</summary>
internal sealed class TokenEndpointException : Exception
{
    public string Error { get; }

    public TokenEndpointException(string error) : base(error) => Error = error;
}

/// <summary>Proof Key for Code Exchange (RFC 7636), S256.</summary>
internal static class Pkce
{
    public static string NewVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
