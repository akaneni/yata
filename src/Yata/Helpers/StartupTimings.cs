using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Yata.Helpers;

/// <summary>
/// Milliseconds from process start to each startup phase, appended as "phase;ms" lines to the
/// file given with --timings. Off (and free) unless that argument is passed.
/// </summary>
internal static class StartupTimings
{
    private static readonly Lazy<DateTime> ProcessStart = new(() =>
    {
        using var process = Process.GetCurrentProcess();
        return process.StartTime;
    });

    private static string? _path;

    public static double SinceProcessStart => (DateTime.Now - ProcessStart.Value).TotalMilliseconds;

    public static void Enable(string path) => _path = path;

    public static void Mark(string phase)
    {
        if (_path == null)
            return;
        try
        {
            File.AppendAllText(_path, string.Create(CultureInfo.InvariantCulture, $"{phase};{SinceProcessStart:F0}{Environment.NewLine}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _path = null;
        }
    }
}
