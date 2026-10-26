using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Yata.Helpers;

/// <summary>
/// Local diagnostic log in %LOCALAPPDATA%\Yata\logs, so a problem can be attached to an issue.
/// It never leaves the PC, and never records what the user plays, writes or keeps: only which
/// part of the app did what, and errors with personal bits (paths, URLs, e-mails) blanked out.
/// </summary>
internal static class Log
{
    private static readonly Lazy<LogWriter> Default = new(() => new LogWriter(Path.Combine(AppPaths.DataDir, "logs")));

    public static string Directory => Default.Value.Directory;

    public static void Info(string area, string message) => Default.Value.Write(LogLevel.Info, area, message);

    public static void Warn(string area, string message, Exception? error = null) =>
        Default.Value.Write(LogLevel.Warn, area, message, error);

    public static void Error(string area, string message, Exception? error = null) =>
        Default.Value.Write(LogLevel.Error, area, message, error);

    /// <summary>Writes crash-*.log right away (the process is about to end) and returns its path.</summary>
    public static string? Crash(Exception error) => Default.Value.Crash(error);

    /// <summary>The newest crash file not announced yet, marked as announced; null if none.</summary>
    public static string? TakeUnseenCrash() => Default.Value.TakeUnseenCrash();

    public static void Flush() => Default.Value.Flush(TimeSpan.FromSeconds(1));
}

internal enum LogLevel { Info, Warn, Error }

internal sealed class LogWriter
{
    private const string SeenFile = "crash-seen.txt";

    private readonly Func<DateTime> _clock;
    private readonly long _maxBytesPerDay;
    private readonly TimeSpan _keep;
    private readonly Channel<object> _queue = Channel.CreateUnbounded<object>(new UnboundedChannelOptions { SingleReader = true });
    // The last lines, copied into a crash file to show what led up to it.
    private readonly Queue<string> _recent = new();
    private readonly object _recentLock = new();
    private bool _crashed;
    private bool _cleaned;
    private string? _file;
    private long _fileSize;
    private bool _fullNoted;

    public LogWriter(string directory, Func<DateTime>? clock = null, long maxBytesPerDay = 1 << 20, int keepDays = 7)
    {
        Directory = directory;
        _clock = clock ?? (() => DateTime.Now);
        _maxBytesPerDay = maxBytesPerDay;
        _keep = TimeSpan.FromDays(keepDays);
        Task.Run(DrainAsync);
    }

    public string Directory { get; }

    public void Write(LogLevel level, string area, string message, Exception? error = null)
    {
        var now = _clock();
        var line = new StringBuilder();
        line.Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append(level switch { LogLevel.Warn => " WARN  ", LogLevel.Error => " ERROR ", _ => " INFO  " })
            .Append('[').Append(area).Append("] ")
            .Append(LogText.Sanitize(message));
        if (error != null)
            line.Append(Environment.NewLine).Append(LogText.Describe(error));
        string text = line.ToString();

        lock (_recentLock)
        {
            _recent.Enqueue(text);
            while (_recent.Count > 50)
                _recent.Dequeue();
        }
        _queue.Writer.TryWrite(new Entry(now, text));
    }

    /// <summary>Waits until everything written so far is on disk, or the timeout passes.</summary>
    public bool Flush(TimeSpan timeout)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return _queue.Writer.TryWrite(done) && done.Task.Wait(timeout);
    }

    public string? Crash(Exception error)
    {
        lock (_recentLock)
        {
            if (_crashed)
                return null; // The dispatcher and the AppDomain both report the same fall.
            _crashed = true;
        }

        Write(LogLevel.Error, "app", "O Yata vai fechar por um erro não tratado", error);
        Flush(TimeSpan.FromSeconds(1));
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var now = _clock();
            string path = Path.Combine(Directory, $"crash-{now:yyyyMMdd-HHmmss}.log");
            var text = new StringBuilder()
                .AppendLine($"Yata {AppVersion()} fechou em {now:yyyy-MM-dd HH:mm:ss}")
                .AppendLine($"{RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}, .NET {Environment.Version}")
                .AppendLine()
                .AppendLine(LogText.Describe(error))
                .AppendLine()
                .AppendLine("Últimas linhas do registro:");
            lock (_recentLock)
                foreach (string line in _recent)
                    text.AppendLine(line);
            File.WriteAllText(path, text.ToString());
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public string? TakeUnseenCrash()
    {
        try
        {
            if (!System.IO.Directory.Exists(Directory))
                return null;
            string? newest = System.IO.Directory.GetFiles(Directory, "crash-*.log")
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                .FirstOrDefault();
            if (newest == null)
                return null;

            string seenPath = Path.Combine(Directory, SeenFile);
            string name = Path.GetFileName(newest);
            if (File.Exists(seenPath) && string.CompareOrdinal(File.ReadAllText(seenPath).Trim(), name) >= 0)
                return null;
            File.WriteAllText(seenPath, name);
            return newest;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The time travels with the line: the day's file is chosen by when it happened, not when it's written.
    private sealed record Entry(DateTime Time, string Text);

    private async Task DrainAsync()
    {
        await foreach (object item in _queue.Reader.ReadAllAsync())
        {
            if (item is TaskCompletionSource done)
                done.TrySetResult();
            else
                Append((Entry)item);
        }
    }

    private void Append(Entry item)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            if (!_cleaned)
            {
                _cleaned = true;
                RemoveOldFiles();
            }

            string file = Path.Combine(Directory, $"yata-{item.Time:yyyy-MM-dd}.log");
            if (file != _file)
            {
                _file = file;
                _fileSize = File.Exists(file) ? new FileInfo(file).Length : 0;
                _fullNoted = false;
            }

            string entry = item.Text + Environment.NewLine;
            int bytes = Encoding.UTF8.GetByteCount(entry);
            if (_fileSize + bytes > _maxBytesPerDay)
            {
                if (_fullNoted)
                    return;
                _fullNoted = true;
                entry = "(limite diário do registro atingido; o resto do dia não é gravado)" + Environment.NewLine;
                bytes = Encoding.UTF8.GetByteCount(entry);
            }
            File.AppendAllText(file, entry);
            _fileSize += bytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nowhere to report a failing log; the app goes on without it.
        }
    }

    private void RemoveOldFiles()
    {
        var cutoff = _clock() - _keep;
        foreach (string pattern in new[] { "yata-*.log", "crash-*.log" })
        {
            foreach (string file in System.IO.Directory.GetFiles(Directory, pattern))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                        File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Open in an editor, say; it goes on a later day.
                }
            }
        }
    }

    private static string AppVersion() =>
        typeof(LogWriter).Assembly.GetName().Version?.ToString(3) ?? "?";
}

/// <summary>Turns messages and exceptions into log text without personal details.</summary>
internal static class LogText
{
    private static readonly Regex Url = new(@"\b(https?)://([^/\s'""?#]+)[^\s'""]*", RegexOptions.Compiled);
    private static readonly Regex WindowsPath = new(@"(?<![\w])(?:[A-Za-z]:\\|\\\\)[^\s'""<>|]*", RegexOptions.Compiled);
    private static readonly Regex Email = new(@"[\w.+-]+@[\w-]+(?:\.[\w-]+)+", RegexOptions.Compiled);
    // Stack frames of a debug build carry the source path; the file name and line are enough.
    private static readonly Regex SourcePath = new(@" in (?:[^\r\n]*[\\/])?([^\\/\r\n]+):line ", RegexOptions.Compiled);

    public static string Sanitize(string text)
    {
        text = Url.Replace(text, m => $"{m.Groups[1].Value}://{m.Groups[2].Value}/…");
        text = Email.Replace(text, "<e-mail>");
        return WindowsPath.Replace(text, "<caminho>");
    }

    /// <summary>Type, HRESULT, cleaned message and stack of an exception and its inner ones.</summary>
    public static string Describe(Exception error)
    {
        var text = new StringBuilder();
        int depth = 0;
        for (var current = error; current != null && depth < 4; current = current.InnerException, depth++)
        {
            if (depth > 0)
                text.AppendLine().Append("  causado por: ");
            text.Append(current.GetType().FullName)
                .Append(" (0x").Append(current.HResult.ToString("X8", CultureInfo.InvariantCulture)).Append("): ")
                .Append(Sanitize(current.Message));
            if (current.StackTrace is { } stack)
                text.AppendLine().Append(SourcePath.Replace(stack, " in $1:line "));
        }
        return text.ToString();
    }
}

/// <summary>Counts errors in a sliding window, to tell a hiccup from a loop that keeps failing.</summary>
internal sealed class ErrorBurst
{
    private readonly int _limit;
    private readonly TimeSpan _window;
    private readonly Func<DateTime> _clock;
    private readonly Queue<DateTime> _times = new();

    public ErrorBurst(int limit, TimeSpan window, Func<DateTime>? clock = null)
    {
        _limit = limit;
        _window = window;
        _clock = clock ?? (() => DateTime.Now);
    }

    /// <summary>Records one error; false once more than the limit happened within the window.</summary>
    public bool Record()
    {
        var now = _clock();
        _times.Enqueue(now);
        while (_times.Peek() < now - _window)
            _times.Dequeue();
        return _times.Count <= _limit;
    }
}
