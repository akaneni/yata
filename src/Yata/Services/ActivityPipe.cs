using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Yata.Helpers;

namespace Yata.Services;

/// <summary>
/// The local door other programs use to put activities in the notch (<c>yata-cli activity ...</c>).
/// A named pipe only this Windows user can open, refused to the network; one short JSON line in,
/// one word back. Nothing else is listened to, and nothing is ever read from the other side's
/// process — only the line it sends. The sending side is <see cref="ActivityClient"/>.
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
        _name = name ?? ActivityClient.DefaultName;
    }

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
                string? line = await ActivityClient.ReadLine(server, ActivityRequest.MaxBytes, timeout.Token);
                string reply = line == null ? "error: request too long or empty" : await _handle(line);
                await ActivityClient.WriteLine(server, reply, timeout.Token);
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
}
