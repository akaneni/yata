using System;
using System.Windows;

namespace Yata.Helpers;

/// <summary>
/// Windows' "Animation effects" setting. Switched off, what only moves to look nice holds still:
/// the notch snaps instead of springing, text and pages change in place, Yata stops fidgeting.
/// Movement that says something (a reminder's caw, the water goal's shake) is short and stays.
/// </summary>
internal static class Motion
{
    public static bool Reduced { get; private set; } = !SystemParameters.ClientAreaAnimation;

    /// <summary>Length of a decorative transition: none at all when motion is reduced.</summary>
    public static TimeSpan Ms(double milliseconds) => Reduced ? TimeSpan.Zero : TimeSpan.FromMilliseconds(milliseconds);

    /// <summary>Reads the setting again, when Windows reports it changed.</summary>
    public static void Refresh() => Reduced = !SystemParameters.ClientAreaAnimation;
}
