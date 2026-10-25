using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Notch.Services;

namespace Notch.Views;

public partial class FocusPage : UserControl
{
    private static readonly Brush DotOff = (Brush)Application.Current.FindResource("TrackBrush");

    private FocusTimer? _timer;

    public FocusPage() => InitializeComponent();

    public void Attach(FocusTimer timer)
    {
        _timer = timer;
        _timer.Changed += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        if (_timer == null)
            return;

        var remaining = _timer.Remaining;
        TimeText.Text = $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
        ProgressArc.Data = BuildArc(_timer.Progress);
        PlayButton.Content = _timer.IsRunning ? "" : "";

        PhaseText.Text = _timer.Phase switch
        {
            FocusPhase.ShortBreak => "Pausa curta",
            FocusPhase.LongBreak => "Pausa longa",
            _ => "Foco",
        };
        HintText.Text = _timer.Phase == FocusPhase.Focus
            ? $"Sessão {_timer.CompletedSessions + 1} de {FocusTimer.SessionsPerCycle}"
            : "Levante, alongue, beba água";

        for (int i = 0; i < Dots.Children.Count; i++)
            ((Ellipse)Dots.Children[i]).Fill = i < _timer.CompletedSessions ? Brushes.White : DotOff;
    }

    /// <summary>Clockwise arc from 12 o'clock covering <paramref name="progress"/> of the ring.</summary>
    private static Geometry BuildArc(double progress)
    {
        const double size = 112, stroke = 6;
        double radius = (size - stroke) / 2, center = size / 2;
        progress = Math.Clamp(progress, 0, 0.9999);
        if (progress <= 0)
            return Geometry.Empty;

        double angle = progress * 2 * Math.PI;
        var start = new Point(center, center - radius);
        var end = new Point(center + radius * Math.Sin(angle), center - radius * Math.Cos(angle));

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(radius, radius), 0, progress > 0.5, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private void OnToggle(object sender, RoutedEventArgs e) => _timer?.Toggle();

    private void OnReset(object sender, RoutedEventArgs e) => _timer?.Reset();

    private void OnSkip(object sender, RoutedEventArgs e) => _timer?.Skip();
}
