using System.Collections.Generic;
using System.Linq;
using Forms = System.Windows.Forms;

namespace Yata.Helpers;

/// <summary>A connected display, in physical pixels (the app is per-monitor DPI aware).</summary>
internal sealed record DisplayInfo(string DeviceName, string Label, int X, int Y, int Width, int Height, bool IsPrimary);

/// <summary>The displays the notch can sit on, numbered left to right.</summary>
internal static class Monitors
{
    public static IReadOnlyList<DisplayInfo> All()
    {
        var screens = Forms.Screen.AllScreens.OrderBy(s => s.Bounds.Left).ThenBy(s => s.Bounds.Top).ToList();
        return screens.Select((s, i) => new DisplayInfo(
            s.DeviceName,
            $"Tela {i + 1} · {s.Bounds.Width}×{s.Bounds.Height}" + (s.Primary ? " (principal)" : ""),
            s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height, s.Primary)).ToList();
    }

    /// <summary>The chosen display, or the primary one when none is chosen or it's unplugged.</summary>
    public static DisplayInfo Resolve(string? deviceName)
    {
        var all = All();
        return all.FirstOrDefault(m => deviceName != null && m.DeviceName == deviceName)
            ?? all.FirstOrDefault(m => m.IsPrimary)
            ?? all[0];
    }
}
