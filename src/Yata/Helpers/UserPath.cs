using System;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Yata.Helpers;

/// <summary>
/// Yata's folder in the user's own PATH (never the machine's), so terminals and agents can call
/// yata-cli by name. The same thing the installer's option does; the uninstaller takes it out.
/// </summary>
internal static class UserPath
{
    private const string Key = "Environment";

    public static bool Contains(string dir) =>
        Entries().Any(e => Same(e, dir));

    public static void Set(string dir, bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Key, writable: true);
        var entries = Entries(key).Where(e => !Same(e, dir)).ToList();
        if (on)
            entries.Add(dir);
        // Expandable, so entries like %USERPROFILE%\bin stay as they were written.
        key.SetValue("Path", string.Join(";", entries), RegistryValueKind.ExpandString);
        Broadcast();
    }

    private static string[] Entries()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key);
        return key == null ? Array.Empty<string>() : Entries(key);
    }

    private static string[] Entries(RegistryKey key) =>
        ((key.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string) ?? "")
        .Split(';', StringSplitOptions.RemoveEmptyEntries);

    private static bool Same(string a, string b) =>
        string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    /// <summary>Tells Explorer, so programs opened from now on get the new PATH.</summary>
    private static void Broadcast() =>
        SendMessageTimeout(new IntPtr(0xffff), 0x001A, UIntPtr.Zero, "Environment", 0x0002, 3000, out _);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, UIntPtr wParam, string lParam,
        uint flags, uint timeout, out UIntPtr result);
}
