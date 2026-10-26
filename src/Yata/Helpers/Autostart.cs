using System;
using Microsoft.Win32;

namespace Yata.Helpers;

/// <summary>
/// "Start with Windows" through the per-user Run key: no admin rights, and the user can
/// see or remove it in Task Manager's startup apps.
/// </summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Yata";

    /// <summary>Tells Yata it was opened at sign-in, so it can leave the network for later.</summary>
    public const string Argument = "--autostart";

    private static string Path => $"\"{Environment.ProcessPath}\"";

    private static string Command => $"{Path} {Argument}";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            // Entries written before --autostart existed are just the quoted path.
            return key?.GetValue(ValueName) is string value &&
                   (string.Equals(value, Command, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, Path, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Brings an entry from before --autostart up to date; leaves everything else alone.</summary>
    public static void Upgrade()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(ValueName) is string value && string.Equals(value, Path, StringComparison.OrdinalIgnoreCase))
            key.SetValue(ValueName, Command);
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, Command);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
