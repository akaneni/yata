using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media;

namespace Yata.Helpers;

/// <summary>
/// How smooth the notch's animations are, for tools/measure-frames.ps1: one
/// "label;cycle;frame;intervalMs;workMs" line per frame WPF draws after a bench step, appended to the
/// file given with --frames. Every frame counts, not only the notch's spring loop: a tab switch
/// where the shape holds still is only the page sliding in. Frame 0 is the wait from the change to
/// the first frame. The interval is what the eye sees; the work is the spring loop's part of it
/// (springs, shape, layout); the rest is drawing. Off (and free) unless that argument is passed.
/// </summary>
internal static class FrameTimings
{
    // Long enough for the page slide and the tab pill (~260 ms) when the shape doesn't move.
    private static readonly TimeSpan MinWindow = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan MaxWindow = TimeSpan.FromMilliseconds(1500);

    private static string? _path;
    private static readonly StringBuilder Pending = new();
    private static bool _recording;
    private static string _label = "";
    private static int _cycle;
    private static int _frame;
    private static long _start;
    private static long _last;
    private static TimeSpan _lastRenderingTime;
    private static bool _moving;
    // The current frame's line waits for the spring loop's work, which runs after this handler.
    private static double _interval = -1;
    private static double _work;

    public static bool Enabled => _path != null;

    public static void Enable(string path) => _path = path;

    /// <summary>A bench step is about to change something: record the frames that follow.</summary>
    public static void Begin(string label, int cycle)
    {
        if (_path == null)
            return;
        End();
        _label = label;
        _cycle = cycle;
        _frame = 0;
        _moving = false;
        _start = _last = Stopwatch.GetTimestamp();
        _lastRenderingTime = TimeSpan.Zero;
        _recording = true;
        CompositionTarget.Rendering += OnRendering;
    }

    /// <summary>The notch's springs started moving.</summary>
    public static void Moving() => _moving = true;

    /// <summary>The springs settled: the recording ends once the minimum window has passed.</summary>
    public static void Settled() => _moving = false;

    /// <summary>The spring loop's time on the current frame.</summary>
    public static void Work(double ms) => _work += ms;

    private static void OnRendering(object? sender, EventArgs e)
    {
        var renderingTime = ((RenderingEventArgs)e).RenderingTime;
        if (renderingTime == _lastRenderingTime)
            return; // Rendering can fire more than once per frame.
        _lastRenderingTime = renderingTime;

        Flush();
        long now = Stopwatch.GetTimestamp();
        _interval = Stopwatch.GetElapsedTime(_last, now).TotalMilliseconds;
        _last = now;

        var elapsed = Stopwatch.GetElapsedTime(_start, now);
        if ((!_moving && elapsed >= MinWindow) || elapsed >= MaxWindow)
            End();
    }

    private static void Flush()
    {
        if (_interval < 0)
            return;
        Pending.Append(string.Create(CultureInfo.InvariantCulture,
            $"{_label};{_cycle};{_frame++};{_interval:F2};{_work:F2}{Environment.NewLine}"));
        _interval = -1;
        _work = 0;
    }

    /// <summary>The step is over: its frames go to the file (not on every frame, which would cost frames).</summary>
    public static void End()
    {
        if (!_recording)
            return;
        _recording = false;
        CompositionTarget.Rendering -= OnRendering;
        // The frame that ended the window is past it: dropped.
        _interval = -1;
        _work = 0;
        Write(Pending.ToString());
        Pending.Clear();
    }

    /// <summary>A "#text" line, e.g. the end of a run.</summary>
    public static void Note(string text)
    {
        if (_path == null)
            return;
        End();
        Write($"#{text}{Environment.NewLine}");
    }

    private static void Write(string text)
    {
        if (text.Length == 0)
            return;
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
