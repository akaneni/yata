using System;
using System.Globalization;
using System.Windows.Threading;
using Yata.Interop;

namespace Yata.Services;

/// <summary>
/// Caps Lock and keyboard layout changes. Polls the toggle/layout state instead of
/// installing a keyboard hook, so the app never sees what is typed.
/// </summary>
internal sealed class KeyboardWatcher : IDisposable
{
    private const int VK_CAPITAL = 0x14;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool _capsLock;
    private IntPtr _window;
    private IntPtr _layout;

    public event Action<HudMessage>? Message;

    public KeyboardWatcher() => _timer.Tick += OnTick;

    public void Start()
    {
        _capsLock = IsCapsLockOn();
        _timer.Start();
    }

    public void Dispose() => _timer.Stop();

    private void OnTick(object? sender, EventArgs e)
    {
        bool capsLock = IsCapsLockOn();
        if (capsLock != _capsLock)
        {
            _capsLock = capsLock;
            Message?.Invoke(capsLock
                ? new HudMessage(Glyphs.Lock, "Caps Lock ativado")
                : new HudMessage(Glyphs.Keyboard, "Caps Lock desativado"));
        }

        // Layouts are per window; only a change within the same window is a user switch.
        IntPtr window = NativeMethods.GetForegroundWindow();
        IntPtr layout = NativeMethods.GetKeyboardLayout(NativeMethods.GetWindowThreadProcessId(window, out _));
        if (window == _window && layout != _layout && layout != IntPtr.Zero && _layout != IntPtr.Zero)
        {
            var (name, code) = DescribeLayout(layout);
            Message?.Invoke(new HudMessage(Glyphs.Keyboard, name, code));
        }
        _window = window;
        _layout = layout;
    }

    private static bool IsCapsLockOn() => (NativeMethods.GetKeyState(VK_CAPITAL) & 1) != 0;

    private static (string Name, string Code) DescribeLayout(IntPtr layout)
    {
        int languageId = (int)(layout.ToInt64() & 0xFFFF);
        try
        {
            var culture = CultureInfo.GetCultureInfo(languageId);
            string name = culture.NativeName;
            return (char.ToUpper(name[0], culture) + name[1..], culture.Name.ToUpperInvariant());
        }
        catch (CultureNotFoundException)
        {
            return ("Teclado", $"{languageId:X4}");
        }
    }
}
