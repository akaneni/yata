using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Yata.Services;

/// <summary>
/// The sending side of the activity pipe, and the line format both sides share. No dependency on
/// the rest of Yata: yata-cli.exe compiles this same file.
/// </summary>
internal static class ActivityClient
{
    /// <summary>
    /// Per user and per Windows session, like the notch itself: pipe names are machine-wide, and
    /// another signed-in user (or another session of the same one) has a Yata of their own.
    /// </summary>
    public static string DefaultName { get; } =
        $"Yata.{WindowsIdentity.GetCurrent().User?.Value}.{Process.GetCurrentProcess().SessionId}";

    /// <summary>
    /// Sends one request to the running Yata and returns its answer, or null if no Yata is
    /// listening. The pipe must belong to this same user, so another account can't pose as Yata.
    /// </summary>
    public static async Task<string?> SendAsync(string json, string? name = null, int timeoutMs = 2000)
    {
        // Without Yata running, connecting would wait the whole timeout; an agent hook calls this on
        // every tool use, so it has to give up at once. Yata holds this name while it runs.
        if (name == null && !IsYataRunning())
            return null;
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

    private static bool IsYataRunning()
    {
        if (!Mutex.TryOpenExisting(SingleInstanceName, out var mutex))
            return false;
        mutex.Dispose();
        return true;
    }

    /// <summary>The single-instance mutex App.xaml.cs creates.</summary>
    public const string SingleInstanceName = "Yata.SingleInstance";

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
}
