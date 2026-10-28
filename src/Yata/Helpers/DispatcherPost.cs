using System;
using System.Windows.Threading;

namespace Yata.Helpers;

internal static class DispatcherPost
{
    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread, for events that come from other threads
    /// (media sessions, Core Audio, WMI, the network). An exception in it is logged under
    /// <paramref name="area"/>: with a plain InvokeAsync it would stay in the operation's Task, and
    /// nobody would ever know (the player frozen on a song, with nothing in the log). Only logged:
    /// one failed read isn't worth closing the app over.
    /// </summary>
    public static void Post(this Dispatcher dispatcher, string area, Action action) =>
        dispatcher.InvokeAsync(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error(area, "Erro ao tratar um evento do sistema", ex);
            }
        });
}
