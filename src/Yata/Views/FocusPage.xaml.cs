using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Yata.Helpers;
using Yata.Services;

namespace Yata.Views;

public partial class FocusPage : UserControl
{
    private const string PlayGlyph = "";
    private const string PauseGlyph = "";
    private const string StopGlyph = "";
    private const string SkipGlyph = "";

    private FocusTimer? _timer;
    private bool _blinking;

    public FocusPage()
    {
        InitializeComponent();
        // Hidden (notch closed or another tab) it doesn't redraw every second, and the paused blink
        // stops, since a running animation keeps WPF rendering frames; it all catches up when shown.
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
                Refresh();
            else
                SetBlinking(false);
        };
    }

    public void Attach(FocusTimer timer)
    {
        _timer = timer;
        _timer.Changed += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        if (_timer == null || !IsVisible)
            return;

        var remaining = _timer.Remaining;
        TimeText.Text = $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
        ProgressArc.Data = BuildArc(_timer.Progress);

        string session = $"sessão {Math.Min(_timer.CompletedSessions + 1, _timer.SessionsPerCycle)} de {_timer.SessionsPerCycle}";
        PhaseText.Text = _timer.Phase switch
        {
            FocusPhase.ShortBreak => "Pausa curta",
            FocusPhase.LongBreak => "Pausa longa",
            _ => "Foco",
        };
        StatusText.Text = (_timer.State, _timer.IsBreak) switch
        {
            (FocusState.Ready, _) => Capitalize(session) + " · pronto para começar",
            (FocusState.Paused, false) => "Pausado · " + session,
            (FocusState.Paused, true) => "Pausa em espera",
            (_, true) => "Levante, alongue, beba água",
            _ => Capitalize(session),
        };

        switch (_timer.State)
        {
            case FocusState.Ready:
                SetButton(PrimaryButton, PrimaryGlyph, PrimaryLabel, PlayGlyph, "Iniciar");
                break;
            case FocusState.Running:
                SetButton(PrimaryButton, PrimaryGlyph, PrimaryLabel, PauseGlyph, "Pausar");
                break;
            case FocusState.Paused:
                SetButton(PrimaryButton, PrimaryGlyph, PrimaryLabel, PlayGlyph, "Continuar");
                break;
        }

        // Nothing to stop before starting; during a break "skip" is the way out.
        SecondaryButton.Visibility = _timer.IsActive ? Visibility.Visible : Visibility.Collapsed;
        if (_timer.IsBreak)
            SetButton(SecondaryButton, SecondaryGlyph, SecondaryLabel, SkipGlyph, "Pular pausa");
        else
            SetButton(SecondaryButton, SecondaryGlyph, SecondaryLabel, StopGlyph, "Parar");

        // Paused: dim the ring and let the time breathe, so it reads as "on hold" at a glance.
        ProgressArc.Opacity = _timer.IsPaused ? 0.4 : 1;
        SetBlinking(_timer.IsPaused);

        while (Dots.Children.Count < _timer.SessionsPerCycle)
            Dots.Children.Add(new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 6, 0) });
        while (Dots.Children.Count > _timer.SessionsPerCycle)
            Dots.Children.RemoveAt(Dots.Children.Count - 1);
        for (int i = 0; i < Dots.Children.Count; i++)
            ((Ellipse)Dots.Children[i]).SetResourceReference(Shape.FillProperty,
                i < _timer.CompletedSessions ? "TextPrimary" : "TrackBrush");
    }

    private static void SetButton(Button button, TextBlock glyph, TextBlock label, string icon, string text)
    {
        glyph.Text = icon;
        label.Text = text;
        // Screen readers (and UI tests) see the action, not the icon character.
        AutomationProperties.SetName(button, text);
    }

    private void SetBlinking(bool on)
    {
        if (on == _blinking)
            return;
        _blinking = on;
        // With motion reduced the paused time just stays dim.
        TimeText.Opacity = on && Motion.Reduced ? 0.6 : 1;
        TimeText.BeginAnimation(OpacityProperty, on && !Motion.Reduced
            ? MainWindow.Ambient(new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(900))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            })
            : null);
    }

    private static string Capitalize(string text) => char.ToUpperInvariant(text[0]) + text[1..];

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

    private void OnPrimary(object sender, RoutedEventArgs e)
    {
        switch (_timer?.State)
        {
            case FocusState.Ready:
                _timer.Start();
                break;
            case FocusState.Running:
                _timer.Pause();
                break;
            case FocusState.Paused:
                _timer.Resume();
                break;
        }
    }

    private void OnSecondary(object sender, RoutedEventArgs e)
    {
        if (_timer == null)
            return;
        if (_timer.IsBreak)
            _timer.SkipBreak();
        else
            _timer.Stop();
    }
}
