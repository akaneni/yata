using System;
using System.Linq;
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

    private const int WeekDays = 7;
    // A meeting this close shows on the status line before a session starts.
    private static readonly TimeSpan EventLookAhead = TimeSpan.FromHours(2);

    private FocusTimer? _timer;
    private FocusLog? _log;
    private TaskList? _tasks;
    private GoogleCalendar? _calendar;
    private bool _blinking;

    public FocusPage()
    {
        InitializeComponent();
        // Hidden (notch closed or another tab) it doesn't redraw every second, and the paused blink
        // stops, since a running animation keeps WPF rendering frames; it all catches up when shown.
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                Refresh();
            }
            else
            {
                SetBlinking(false);
                ClosePicker();
            }
        };
    }

    public void Attach(FocusTimer timer, FocusLog log, TaskList tasks, GoogleCalendar calendar)
    {
        _timer = timer;
        _log = log;
        _tasks = tasks;
        _calendar = calendar;
        _timer.Changed += Refresh;
        _log.Changed += Refresh;
        _tasks.Items.CollectionChanged += (_, _) => Refresh();
        _tasks.LocalChanged += Refresh;
        _calendar.Changed += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        if (_timer == null || !IsVisible)
            return;

        var remaining = _timer.Remaining;
        TimeText.Text = $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
        ProgressArc.Data = BuildArc(_timer.Progress);

        int session = Math.Min(_timer.CompletedSessions + 1, _timer.SessionsPerCycle), sessions = _timer.SessionsPerCycle;
        PhaseText.Text = _timer.Phase switch
        {
            FocusPhase.ShortBreak => Strings.Focus_ShortBreak,
            FocusPhase.LongBreak => Strings.Focus_LongBreak,
            _ => Strings.Focus_Focus,
        };
        StatusText.Text = EventStatus() ?? (_timer.State, _timer.IsBreak) switch
        {
            (FocusState.Ready, _) => string.Format(Strings.Focus_Ready, session, sessions),
            (FocusState.Paused, false) => string.Format(Strings.Focus_Paused, session, sessions),
            (FocusState.Paused, true) => Strings.Focus_BreakOnHold,
            (_, true) => Strings.Focus_BreakTip,
            _ => string.Format(Strings.Focus_Session, session, sessions)
                 + string.Format(Strings.Focus_BreakAt, (DateTime.Now + remaining).ToString("t", Lang.Culture)),
        };
        StatusText.ToolTip = StatusText.Text;

        switch (_timer.State)
        {
            case FocusState.Ready:
                SetButton(PrimaryButton, PrimaryGlyph, PrimaryLabel, PlayGlyph, Strings.Focus_Start);
                break;
            case FocusState.Running:
                SetButton(PrimaryButton, PrimaryGlyph, PrimaryLabel, PauseGlyph, Strings.Focus_Pause);
                break;
            case FocusState.Paused:
                SetButton(PrimaryButton, PrimaryGlyph, PrimaryLabel, PlayGlyph, Strings.Focus_Resume);
                break;
        }

        // Nothing to stop before starting; during a break "skip" is the way out.
        SecondaryButton.Visibility = _timer.IsActive ? Visibility.Visible : Visibility.Collapsed;
        if (_timer.IsBreak)
            SetButton(SecondaryButton, SecondaryGlyph, SecondaryLabel, SkipGlyph, Strings.Focus_SkipBreak);
        else
            SetButton(SecondaryButton, SecondaryGlyph, SecondaryLabel, StopGlyph, Strings.Focus_Stop);

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

        ShowTask();
        ShowToday();
    }

    /// <summary>
    /// A meeting that matters to the session: before starting, how many sessions fit until it;
    /// while focusing, one that starts before the break. Null when there's none (or no calendar).
    /// </summary>
    private string? EventStatus()
    {
        if (_timer == null || _calendar == null || _timer.IsBreak || _timer.IsPaused)
            return null;
        var now = DateTime.Now;
        var window = _timer.IsRunning ? _timer.Remaining : EventLookAhead;
        if (CalendarReminders.NextWithin(_calendar.Events, now, window) is not { } next)
            return null;

        string at = next.Start.ToString("t", Lang.Culture);
        if (_timer.IsRunning)
            return string.Format(Strings.Focus_EventDuring, next.Title, at);
        int fits = (int)((next.Start - now).TotalMinutes / _timer.PhaseLength.TotalMinutes);
        return fits switch
        {
            0 => string.Format(Strings.Focus_EventNoFit, next.Title, at),
            1 => string.Format(Strings.Focus_EventFitsOne, next.Title, at),
            _ => string.Format(Strings.Focus_EventFitsMany, next.Title, at, fits),
        };
    }

    /// <summary>The picked task, if it's still on the list and not done.</summary>
    private TaskItem? CurrentTask() =>
        _log?.TaskId is { } id ? _tasks?.Items.FirstOrDefault(t => t.Id == id && !t.Done) : null;

    private void ShowTask()
    {
        var task = CurrentTask();
        if (task == null && _log?.TaskId != null)
        {
            _log.TaskId = null; // Done or removed on the Tasks page; refreshes again.
            return;
        }
        TaskCheck.Visibility = task != null ? Visibility.Visible : Visibility.Collapsed;
        TaskGlyph.Visibility = task != null ? Visibility.Collapsed : Visibility.Visible;
        TaskText.Text = task?.Text ?? Strings.Focus_PickTaskShort;
        TaskText.SetResourceReference(TextBlock.ForegroundProperty, task != null ? "TextPrimary" : "TextSecondary");
        TaskPick.ToolTip = task?.Text;
        AutomationProperties.SetName(TaskPick, task?.Text ?? Strings.Focus_PickTaskShort);
        // The break is when a session's task gets ticked off.
        TaskPrefix.Visibility = task != null && _timer?.IsBreak == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowToday()
    {
        if (_log == null)
            return;
        var today = _log.TodayTotal;
        var goal = _log.Goal;
        TodayText.Text = FocusLog.Format(today);
        GoalText.Text = today >= goal ? Strings.Focus_GoalMet : string.Format(Strings.Focus_OfGoal, FocusLog.Format(goal));

        var days = _log.LastDays(WeekDays);
        double max = Math.Max(goal.TotalMinutes, days.Max(d => d.Focus.TotalMinutes));
        Week.Show(days.Select(d => (d.Day, d.Focus.TotalMinutes)).ToList(), max,
            (day, minutes) => $"{day.ToString(Strings.Format_DayMonth, Lang.Culture)}: {FocusLog.Format(TimeSpan.FromMinutes(minutes))}");
    }

    private void OnTaskDone(object sender, RoutedEventArgs e)
    {
        if (CurrentTask() is not { } task || _tasks == null || _log == null)
            return;
        _tasks.Toggle(task);
        _log.TaskId = null;
    }

    private void OnPickTask(object sender, RoutedEventArgs e)
    {
        if (_tasks == null || _log == null)
            return;
        PickList.Children.Clear();
        var open = _tasks.Items.Where(t => !t.Done).ToList();
        if (_log.TaskId != null)
            PickList.Children.Add(PickRow(Strings.Focus_NoTask, null, secondary: true));
        foreach (var task in open)
            PickList.Children.Add(PickRow(task.Text, task.Id, secondary: false));
        if (open.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = Strings.Focus_NoOpenTasks,
                FontSize = 12,
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            PickList.Children.Add(empty);
        }
        Picker.Visibility = Visibility.Visible;
        Main.Visibility = Visibility.Hidden;
    }

    private Button PickRow(string text, Guid? id, bool secondary)
    {
        var label = new TextBlock { Text = text, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
        label.SetResourceReference(TextBlock.ForegroundProperty, secondary ? "TextSecondary" : "TextPrimary");
        var content = new DockPanel();
        if (id != null && id == _log?.TaskId)
        {
            // The task picked now.
            var check = new TextBlock { Text = "", FontSize = 11, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            check.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            DockPanel.SetDock(check, Dock.Right);
            content.Children.Add(check);
        }
        content.Children.Add(label);
        var row = new Button
        {
            Style = (Style)FindResource("LinkButton"),
            Content = content,
            Height = 28,
            Padding = new Thickness(8, 0, 8, 0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ToolTip = secondary ? null : text,
        };
        row.Click += (_, _) =>
        {
            if (_log != null)
                _log.TaskId = id;
            ClosePicker();
        };
        return row;
    }

    private void OnClosePicker(object sender, RoutedEventArgs e) => ClosePicker();

    private void ClosePicker()
    {
        Picker.Visibility = Visibility.Collapsed;
        Main.Visibility = Visibility.Visible;
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

    /// <summary>Clockwise arc from 12 o'clock covering <paramref name="progress"/> of the ring.</summary>
    internal static Geometry BuildArc(double progress, double size = 112, double stroke = 6)
    {
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
