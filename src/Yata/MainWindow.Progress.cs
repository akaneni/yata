using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Yata.Helpers;
using Yata.Interop;
using Yata.Services;
using Yata.Views;
using Glyphs = Yata.Services.Glyphs;
using MediaState = Yata.Services.MediaState;

namespace Yata;

// Media progress bar and seeking.
public partial class MainWindow
{
    private double _seekFraction;

    private void RefreshProgress()
    {
        if (_state == null || _seeking)
            return;

        if (_state.Duration <= TimeSpan.Zero)
        {
            ProgressRow.Visibility = Visibility.Hidden;
            return;
        }

        ProgressRow.Visibility = Visibility.Visible;
        var position = _state.CurrentPosition;
        SetProgress(position.TotalSeconds / _state.Duration.TotalSeconds, position, _state.Duration);
    }

    private void SetProgress(double fraction, TimeSpan position, TimeSpan duration)
    {
        double fill = Math.Clamp(fraction, 0, 1) * ProgressTrack.ActualWidth;
        ProgressFill.Width = fill;
        ProgressThumb.Margin = new Thickness(fill - ProgressThumb.Width / 2, 0, 0, 0);
        ElapsedText.Text = FormatTime(position);
        DurationText.Text = FormatTime(duration);
    }

    private void OnTrackMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_state == null || _state.Duration <= TimeSpan.Zero)
            return;

        _seeking = true;
        ProgressTrack.CaptureMouse();
        PreviewSeek(e.GetPosition(ProgressTrack).X);
        e.Handled = true;
    }

    private void OnTrackMouseMove(object sender, MouseEventArgs e)
    {
        if (_seeking)
            PreviewSeek(e.GetPosition(ProgressTrack).X);
    }

    private void OnTrackMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_seeking || _state == null)
            return;

        var target = TimeSpan.FromTicks((long)(_state.Duration.Ticks * _seekFraction));
        _seeking = false;
        ProgressTrack.ReleaseMouseCapture();
        _ = _media.SeekAsync(target);
        e.Handled = true;

        if (!NotchHost.IsMouseOver)
            ScheduleExpanded(false, 350);
    }

    private void OnTrackLostCapture(object sender, MouseEventArgs e)
    {
        if (_seeking)
        {
            _seeking = false;
            RefreshProgress();
        }
    }

    private void OnTrackSizeChanged(object sender, SizeChangedEventArgs e) => RefreshProgress();

    private void PreviewSeek(double x)
    {
        if (_state == null)
            return;

        _seekFraction = Math.Clamp(x / Math.Max(ProgressTrack.ActualWidth, 1), 0, 1);
        SetProgress(_seekFraction, TimeSpan.FromTicks((long)(_state.Duration.Ticks * _seekFraction)), _state.Duration);
    }

    private static string FormatTime(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
}
