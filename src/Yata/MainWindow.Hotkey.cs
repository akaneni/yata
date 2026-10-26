using System;
using System.Windows.Input;
using System.Windows.Interop;
using Yata.Interop;

namespace Yata;

// Ctrl+Alt+Y opens and closes the notch from anywhere.
public partial class MainWindow
{
    private const int HotkeyId = 0x5961; // "Ya"
    // Opened from the keyboard, the mouse isn't over the notch; keep it open this long.
    private static readonly TimeSpan HotkeyPin = TimeSpan.FromSeconds(10);

    private bool _hotkeyRegistered;

    /// <summary>
    /// RegisterHotKey: Windows only reports this one key combination to us. Nothing else
    /// typed is seen, unlike a keyboard hook. Fails quietly if another app already owns it.
    /// </summary>
    private void UpdateHotkey()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        bool wanted = Settings.HotkeyEnabled;
        if (wanted == _hotkeyRegistered)
            return;

        if (wanted)
        {
            _hotkeyRegistered = NativeMethods.RegisterHotKey(hwnd, HotkeyId,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT,
                (uint)KeyInterop.VirtualKeyFromKey(Key.Y));
        }
        else
        {
            NativeMethods.UnregisterHotKey(hwnd, HotkeyId);
            _hotkeyRegistered = false;
        }
    }

    private void OnHotkey()
    {
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
}
