using System;
using System.Windows.Input;
using System.Windows.Interop;
using Yata.Interop;
using Yata.Services;

namespace Yata;

// Ctrl+Alt+Y opens and closes the notch from anywhere. Ctrl+Alt+J goes to the window of the
// activity that needs you (the agent asking for a permission).
public partial class MainWindow
{
    private const int HotkeyId = 0x5961; // "Ya"
    private const int JumpHotkeyId = 0x594A; // "YJ"
    // Opened from the keyboard, the mouse isn't over the notch; keep it open this long.
    private static readonly TimeSpan HotkeyPin = TimeSpan.FromSeconds(10);
    // Pressed again within this, Ctrl+Alt+J goes on to the next activity instead of the first.
    private static readonly TimeSpan JumpCycle = TimeSpan.FromSeconds(5);

    private bool _hotkeyRegistered;
    private bool _jumpHotkeyRegistered;
    private string? _lastJumpId;
    private DateTime _lastJumpAt;

    /// <summary>
    /// RegisterHotKey: Windows only reports these key combinations to us. Nothing else
    /// typed is seen, unlike a keyboard hook. Fails quietly if another app already owns one.
    /// </summary>
    private void UpdateHotkey()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        UpdateHotkey(hwnd, HotkeyId, Key.Y, Settings.HotkeyEnabled, ref _hotkeyRegistered);
        UpdateHotkey(hwnd, JumpHotkeyId, Key.J, Settings.JumpHotkeyEnabled, ref _jumpHotkeyRegistered);
    }

    private static void UpdateHotkey(IntPtr hwnd, int id, Key key, bool wanted, ref bool registered)
    {
        if (wanted == registered)
            return;

        if (wanted)
        {
            registered = NativeMethods.RegisterHotKey(hwnd, id,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT,
                (uint)KeyInterop.VirtualKeyFromKey(key));
        }
        else
        {
            NativeMethods.UnregisterHotKey(hwnd, id);
            registered = false;
        }
    }

    private void OnHotkey(int id)
    {
        if (id == JumpHotkeyId)
        {
            OnJumpHotkey();
            return;
        }
        if (!IsVisible)
            return; // Hidden for a full-screen app.

        _hoverTimer.Stop();
        if (_expanded)
        {
            _pinnedUntil = DateTime.MinValue;
            SetExpanded(false);
        }
        else
        {
            _pinnedUntil = DateTime.Now + HotkeyPin;
            SetExpanded(true);
        }
    }

    /// <summary>
    /// The most pressing activity's window; pressed again soon after, the next one. Windows whose
    /// program closed are skipped. With none to go to, the notch opens so you see why.
    /// </summary>
    private void OnJumpHotkey()
    {
        var now = DateTime.Now;
        string? after = now - _lastJumpAt < JumpCycle ? _lastJumpId : null;
        bool forgot = false;
        while (_activities.NextWithWindow(after) is Activity activity)
        {
            if (GoToWindow(activity.Window))
            {
                _lastJumpId = activity.Id;
                _lastJumpAt = now;
                if (AnnouncedWithWindow()?.Id == activity.Id)
                    HideHud();
                if (forgot)
                    OnActivitiesChanged();
                return;
            }
            _activities.ForgetWindow(activity.Id);
            forgot = true;
        }

        _lastJumpId = null;
        if (forgot)
            OnActivitiesChanged();
        if (IsVisible && !_expanded)
        {
            _hoverTimer.Stop();
            _pinnedUntil = now + HotkeyPin;
            SetExpanded(true);
        }
    }
}
