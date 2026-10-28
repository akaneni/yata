using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Yata.Interop;

namespace Yata.Services;

/// <summary>
/// Polls once per second for a full-screen app on the notch's monitor, and keeps the
/// notch above other topmost windows while nothing is full screen.
/// </summary>
public sealed class FullscreenWatcher : IDisposable
{
    private readonly IntPtr _ownWindow;
    private readonly DispatcherTimer _timer;
    private bool _isFullscreen;

    public event Action<bool>? Changed;

    public FullscreenWatcher(IntPtr ownWindow)
    {
        _ownWindow = ownWindow;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
    }

    public void Start() => _timer.Start();

    public void Dispose() => _timer.Stop();

    private void OnTick(object? sender, EventArgs e)
    {
        bool fullscreen = DetectFullscreen();
        if (fullscreen != _isFullscreen)
        {
            _isFullscreen = fullscreen;
            Changed?.Invoke(fullscreen);
        }

        if (!fullscreen)
            NativeMethods.KeepTopmost(_ownWindow);
    }

    /// <summary>
    /// Windows says the user is busy or in quiet time ("Do not disturb" and the like): reminders come
    /// without the chime.
    /// </summary>
    public static bool WindowsWantsQuiet() =>
        NativeMethods.SHQueryUserNotificationState(out int state) == 0 &&
        state is NativeMethods.QUNS_BUSY or NativeMethods.QUNS_QUIET_TIME;

    private bool DetectFullscreen()
    {
        if (NativeMethods.SHQueryUserNotificationState(out int state) == 0 &&
            (state == NativeMethods.QUNS_RUNNING_D3D_FULL_SCREEN || state == NativeMethods.QUNS_PRESENTATION_MODE))
            return true;

        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == _ownWindow ||
            foreground == NativeMethods.GetShellWindow() || foreground == NativeMethods.GetDesktopWindow())
            return false;

        // A normal maximized window overhangs the screen edges, so it can look full screen with an
        // auto-hidden taskbar. Borderless "maximized" windows (many players and games) are real full screen.
        if (NativeMethods.IsZoomed(foreground) && NativeMethods.HasCaption(foreground))
            return false;

        string className = NativeMethods.GetClassName(foreground);
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd")
            return false;

        IntPtr monitor = NativeMethods.MonitorFromWindow(foreground, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor != NativeMethods.MonitorFromWindow(_ownWindow, NativeMethods.MONITOR_DEFAULTTONEAREST))
            return false;

        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info) || !NativeMethods.GetWindowRect(foreground, out var rect))
            return false;

        var screen = info.rcMonitor;
        return rect.Left <= screen.Left && rect.Top <= screen.Top &&
               rect.Right >= screen.Right && rect.Bottom >= screen.Bottom;
    }
}
