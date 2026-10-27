using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Yata.Helpers;

namespace Yata.Services;

/// <summary>
/// The local door other programs use to put activities in the notch (<c>Yata.exe activity ...</c>).
/// A named pipe only this Windows user can open, refused to the network; one short JSON line in,
/// one word back. Nothing else is listened to, and nothing is ever read from the other side's
/// process — only the line it sends.
/// </summary>
internal sealed class ActivityPipe : IDisposable
{
    // A handful at once is plenty for scripts; more just waits a moment.
    private const int MaxInstances = 4;
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(2);

    private readonly string _name;
    private readonly Func<string, Task<string>> _handle;
    private CancellationTokenSource? _cts;

    /// <param name="handle">Answers one request line; called off the UI thread.</param>
    public ActivityPipe(Func<string, Task<string>> handle, string? name = null)
    {
        _handle = handle;
        _name = name ?? DefaultName;
    }

    /// <summary>
    /// Per user and per Windows session, like the notch itself: pipe names are machine-wide, and
    /// another signed-in user (or another session of the same one) has a Yata of their own.
    /// </summary>
    public static string DefaultName { get; } =
        $"Yata.{WindowsIdentity.GetCurrent().User?.Value}.{Process.GetCurrentProcess().SessionId}";

    public bool IsRunning => _cts != null;

    public void Start()
    {
        if (_cts != null)
            return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(() => AcceptLoop(token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose() => Stop();

    private async Task AcceptLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = Create();
            }
            catch (IOException)
            {
                // Every instance is busy (or the name is held by a Yata closing): try again shortly.
                try { await Task.Delay(250, token); } catch (OperationCanceledException) { return; }
                continue;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                Log.Warn("activities", "Não foi possível abrir o canal de atividades", ex);
                return;
            }

            try
            {
                await server.WaitForConnectionAsync(token);
            }
            catch (OperationCanceledException)
            {
                await server.DisposeAsync();
                return;
            }
            catch (IOException)
            {
                await server.DisposeAsync();
                continue;
            }
            _ = Serve(server, token);
        }
    }

    private NamedPipeServerStream Create()
    {
        var security = new PipeSecurity();
        var user = WindowsIdentity.GetCurrent().User ?? throw new UnauthorizedAccessException("no user SID");
        security.AddAccessRule(new PipeAccessRule(user,
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance | PipeAccessRights.Synchronize,
            AccessControlType.Allow));
        // Belt and braces: the user's own account logged in from another machine is still refused.
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl, AccessControlType.Deny));
        return NamedPipeServerStreamAcl.Create(_name, PipeDirection.InOut, MaxInstances, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 1024, 1024, security);
    }

    private async Task Serve(NamedPipeServerStream server, CancellationToken token)
    {
        await using (server)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(ReadTimeout);
                string? line = await ReadLine(server, ActivityRequest.MaxBytes, timeout.Token);
                string reply = line == null ? "error: request too long or empty" : await _handle(line);
                await WriteLine(server, reply, timeout.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
            {
                // The other side went away or took too long; nothing to answer.
            }
            catch (Exception ex)
            {
                Log.Warn("activities", "Falha ao atender um pedido de atividade", ex);
            }
        }
    }

    /// <summary>One line, at most <paramref name="max"/> bytes; null if longer or empty.</summary>
    internal static async Task<string?> ReadLine(Stream stream, int max, CancellationToken token)
    {
        var buffer = new byte[max + 1];
        int length = 0;
        while (length < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(length, buffer.Length - length), token);
            if (read == 0)
                break;
            int end = Array.IndexOf(buffer, (byte)'\n', length, read);
            length += read;
            if (end >= 0)
            {
                length = end;
                break;
            }
        }
        if (length == 0 || length > max)
            return null;
        return Encoding.UTF8.GetString(buffer, 0, length).TrimEnd('\r');
    }

    internal static async Task WriteLine(Stream stream, string text, CancellationToken token)
    {
        await stream.WriteAsync(Encoding.UTF8.GetBytes(text + "\n"), token);
        await stream.FlushAsync(token);
    }

    /// <summary>
    /// Sends one request to the running Yata and returns its answer, or null if no Yata is
    /// listening. The pipe must belong to this same user, so another account can't pose as Yata.
    /// </summary>
    public static async Task<string?> SendAsync(string json, string? name = null, int timeoutMs = 2000)
    {
        await using var client = new NamedPipeClientStream(".", name ?? DefaultName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeout = new CancellationTokenSource(timeoutMs);
        try
        {
            await client.ConnectAsync(timeout.Token);
            await WriteLine(client, json, timeout.Token);
            return await ReadLine(client, 1024, timeout.Token) ?? "error: no answer";
        }
        catch (OperationCanceledException)
        {
            return client.IsConnected ? "error: no answer" : null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
