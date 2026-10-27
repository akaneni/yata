using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Yata.Views;

/// <summary>
/// Offers to take back something just removed, for a few seconds. A newer removal replaces the
/// offer: only the last one can be undone.
/// </summary>
public partial class UndoToast : UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(6) };
    private Action? _undo;

    public UndoToast()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Dismiss();
    }

    public void Show(string message, Action undo)
    {
        Message.Text = message;
        _undo = undo;
        Visibility = Visibility.Visible;
        _timer.Stop();
        _timer.Start();
    }

    public void Dismiss()
    {
        _timer.Stop();
        _undo = null;
        Visibility = Visibility.Collapsed;
    }

    private void OnUndo(object sender, RoutedEventArgs e)
    {
        var undo = _undo;
        Dismiss();
        undo?.Invoke();
    }
}
