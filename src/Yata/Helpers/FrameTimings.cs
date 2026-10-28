using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Yata.Helpers;

/// <summary>
/// How smooth the notch's animations are, for tools/measure-frames.ps1: one
/// "label;cycle;frame;intervalMs;workMs" line per frame of the notch's spring loop, appended to the
/// file given with --frames. Frame 0 is the wait from the change to the first frame. The interval is
/// what the eye sees; the work is the UI thread's part of it (springs, shape, layout); the rest is
/// drawing. Off (and free) unless that argument is passed.
/// </summary>
internal static class FrameTimings
{
    private static string? _path;
    private static readonly StringBuilder Pending = new();
    private static string _label = "outro";
    private static int _cycle;
    private static int _frame;
    private static long _last;

    public static bool Enabled => _path != null;

    public static void Enable(string path) => _path = path;

    /// <summary>What the next animation is (a bench step); ends the one still running, if any.</summary>
    public static void Begin(string label, int cycle)
    {
        if (_path == null)
            return;
        End();
        _label = label;
        _cycle = cycle;
        _frame = 0;
        _last = Stopwatch.GetTimestamp();
    }

    /// <summary>A frame was drawn; <paramref name="workMs"/> is the UI thread's time on it.</summary>
    public static void Frame(double workMs)
    {
        if (_path == null)
            return;
        long now = Stopwatch.GetTimestamp();
        double interval = Stopwatch.GetElapsedTime(_last, now).TotalMilliseconds;
        _last = now;
        Pending.Append(string.Create(CultureInfo.InvariantCulture,
            $"{_label};{_cycle};{_frame++};{interval:F2};{workMs:F2}{Environment.NewLine}"));
    }

    /// <summary>The animation settled: its frames go to the file (not on every frame, which would cost frames).</summary>
    public static void End()
    {
        if (_path == null || Pending.Length == 0)
            return;
        Write(Pending.ToString());
        Pending.Clear();
        _label = "outro";
    }

    /// <summary>A "#text" line, e.g. the end of a run or what was measured.</summary>
    public static void Note(string text)
    {
        if (_path == null)
            return;
        End();
        Write($"#{text}{Environment.NewLine}");
    }

    private static void Write(string text)
    {
        try
        {
            File.AppendAllText(_path!, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _path = null;
        }
    }
}
