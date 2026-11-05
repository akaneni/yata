using System.Windows;
using Yata.Services;

namespace Yata;

// Several players at once (Spotify and a YouTube tab, say): ⇄ over the cover switches the notch to the
// next one, ✕ takes the shown one off the notch (the app keeps running; it comes back when it plays again).
public partial class MainWindow
{
    /// <summary>⇄ with two or more players to show; ✕ whenever one is shown.</summary>
    private void UpdatePlayerButtons()
    {
        string? next = _media.NextSource;
        SwitchPlayerButton.Visibility = next != null ? Visibility.Visible : Visibility.Collapsed;
        SwitchPlayerButton.ToolTip = next == null ? null
            : PlayerNames.Friendly(next) is { } nextName ? string.Format(Strings.Media_SwitchTo, nextName)
            : Strings.Media_SwitchPlayer;

        var state = _media.Current;
        bool shown = state != null && state.SourceApp.Length > 0;
        RemovePlayerButton.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        RemovePlayerButton.ToolTip = !shown ? null
            : PlayerNames.Friendly(state!.SourceApp) is { } name ? string.Format(Strings.Media_RemovePlayer, name)
            : Strings.Media_RemoveThisPlayer;
    }

    private void OnSwitchPlayerClick(object sender, RoutedEventArgs e) => _media.SwitchToNext();

    private void OnRemovePlayerClick(object sender, RoutedEventArgs e) => _media.RemoveCurrent();
}
