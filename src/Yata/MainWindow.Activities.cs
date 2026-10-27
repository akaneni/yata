using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Yata.Helpers;
using Yata.Services;
using Yata.Views;
using Glyphs = Yata.Services.Glyphs;

namespace Yata;

// Activities from other programs, and the bubble beside the closed notch. Like the iPhone's island
// splitting in two: the notch keeps what it shows, and a second, smaller one carries what
// doesn't fit (another program's progress, the next meeting while music plays). Programs send
// activities with yata-cli.exe (src/Yata.Cli), through ActivityPipe.
public partial class MainWindow
{
    private const double BubbleSize = 32;
    // Close enough for the two shapes' ears to meet at the screen edge, as if one split from the other.
    private const double BubbleGap = 6;
    // A runaway script can't flood the notch: past this, requests are refused for a moment.
    private const int ActivityRateLimit = 30;
    private static readonly TimeSpan ActivityRateWindow = TimeSpan.FromSeconds(10);

    private readonly ActivityBoard _activities = new();
    private readonly DispatcherTimer _activityExpiry = new();
    private readonly Queue<DateTime> _activityRequests = new();
    private readonly Spring _bubble = new(0, 0.002);
    private ActivityPipe? _activityPipe;
    private BubbleItem? _bubbleItem;
    // The next event, kept even while media has the closed notch, so the bubble can carry it.
    private CalendarEvent? _upcomingEvent;

    /// <summary>What the bubble shows, and the notice hovering it brings up.</summary>
    private sealed record BubbleItem(string? Glyph, string? Label, double? Progress, bool Warning, HudMessage Detail,
        bool Dim = false);

    private const string PauseGlyph = "";

    private bool BubbleShown => _bubbleItem != null && !_expanded;

    private void StartActivities()
    {
        BubbleShape.Data = BuildNotchGeometry(BubbleSize, BubbleSize, BubbleSize / 2);
        _activityExpiry.Tick += (_, _) =>
        {
            _activityExpiry.Stop();
            if (_activities.Prune(DateTime.Now))
                Log.Info("activities", $"Atividades expiradas; restam {_activities.Count}");
            OnActivitiesChanged();
        };
        ApplyActivitySettings();
    }

    private void StopActivities()
    {
        _activityPipe?.Dispose();
        _activityExpiry.Stop();
    }

    /// <summary>Switched off, the pipe closes and whatever was up goes away.</summary>
    private void ApplyActivitySettings()
    {
        if (Settings.ActivitiesEnabled)
        {
            _activityPipe ??= new ActivityPipe(line => Dispatcher.InvokeAsync(() => HandleActivity(line)).Task);
            if (!_activityPipe.IsRunning)
            {
                _activityPipe.Start();
                Log.Info("activities", "Canal de atividades aberto");
            }
        }
        else if (_activityPipe?.IsRunning == true)
        {
            _activityPipe.Stop();
            _activities.Clear();
            OnActivitiesChanged();
            Log.Info("activities", "Canal de atividades fechado");
        }
    }

    /// <summary>One request from another program; the answer goes back to it ("ok" or why not).</summary>
    private string HandleActivity(string line)
    {
        if (!Settings.ActivitiesEnabled)
            return "off: activities are turned off in Yata's settings";

        var now = DateTime.Now;
        while (_activityRequests.Count > 0 && now - _activityRequests.Peek() > ActivityRateWindow)
            _activityRequests.Dequeue();
        if (_activityRequests.Count >= ActivityRateLimit)
            return "busy: too many requests, try again in a few seconds";
        _activityRequests.Enqueue(now);

        if (ActivityRequest.Parse(line, out string? error) is not ActivityRequest request)
            return "error: " + error;

        var (change, activity) = _activities.Apply(request, now);
        switch (change)
        {
            case ActivityChange.Full:
                return $"full: at most {ActivityBoard.MaxActivities} activities at a time";
            case ActivityChange.NeedsTitle:
                return "error: no activity with this id (it may have ended or expired); send --title to create it";
            case ActivityChange.Unknown:
                return "ok"; // Ending something already gone is what the caller wanted anyway.
        }
        if (change is ActivityChange.Added or ActivityChange.Ended)
            Log.Info("activities", $"{(change == ActivityChange.Added ? "Nova atividade" : "Atividade encerrada")}; agora {_activities.Count}");
        if (activity != null)
            Announce(change, activity);
        OnActivitiesChanged();
        return "ok";
    }

    /// <summary>
    /// New work gets a brief notice; reaching a state that matters (needs you, done, failed) gets a
    /// notice that waits to be seen. Progress alone stays in the bubble, so scripts can't nag.
    /// </summary>
    private void Announce(ActivityChange change, Activity activity)
    {
        bool important = activity.State != ActivityState.Running && change is ActivityChange.Added or ActivityChange.StateChanged;
        if (!important && change != ActivityChange.Added)
            return;
        ShowHud(ActivityNotice(activity, important));
    }

    private static HudMessage ActivityNotice(Activity activity, bool important)
    {
        string? value = activity.Progress is double p ? Percent(p) : null;
        return new HudMessage(activity.Glyph, activity.Line, value, activity.Progress, activity.Warning,
            DurationMs: important ? 4500 : 2500, Important: important, Kind: HudKind.Activity);
    }

    private static string Percent(double progress) =>
        (progress * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    private void OnActivitiesChanged()
    {
        _activityExpiry.Stop();
        if (_activities.NextExpiry is DateTime expiry)
        {
            var wait = expiry - DateTime.Now;
            _activityExpiry.Interval = wait > TimeSpan.Zero ? wait + TimeSpan.FromMilliseconds(50) : TimeSpan.FromMilliseconds(50);
            _activityExpiry.Start();
        }
        UpdateBubble();
    }

    // ---- The bubble ------------------------------------------------------------------

    private void UpdateBubble()
    {
        var item = PickBubble(DateTime.Now);
        if (item == _bubbleItem)
            return;
        bool appearing = (item == null) != (_bubbleItem == null);
        _bubbleItem = item;
        if (item != null)
        {
            BubbleGlyph.Text = item.Glyph ?? "";
            BubbleGlyph.Visibility = item.Glyph == null ? Visibility.Collapsed : Visibility.Visible;
            BubbleGlyph.SetResourceReference(TextBlock.ForegroundProperty, item.Warning ? "WarningBrush" : "TextPrimary");
            BubbleLabel.Text = item.Label ?? "";
            // "57s" needs a smaller size than "25" to keep clear of the ring.
            BubbleLabel.FontSize = item.Label?.Length > 2 ? 8 : 10;
            BubbleLabel.Visibility = item.Label == null ? Visibility.Collapsed : Visibility.Visible;
            BubbleTrack.Visibility = item.Progress == null ? Visibility.Collapsed : Visibility.Visible;
            BubbleArc.Data = item.Progress is double progress ? FocusPage.BuildArc(progress, 22, 2) : null;
            BubbleArc.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, item.Warning ? "WarningBrush" : "TextPrimary");
            // Paused reads differently from running at a glance, as in the notch: dimmed.
            BubbleContent.Opacity = item.Dim ? 0.55 : 1;
            System.Windows.Automation.AutomationProperties.SetName(Bubble, item.Detail.Text);
        }
        // The countdown changes the bubble every second; only showing or hiding it moves the notch.
        if (appearing)
            UpdateTargets();
    }

    /// <summary>
    /// One bubble, like the iPhone's island, so things take turns: what needs the user wins; then,
    /// while media has the notch, the next meeting and the focus countdown it pushed out; then the
    /// latest activity. Hovering says how many others are waiting ("+2"), so nothing is hidden silently.
    /// </summary>
    private BubbleItem? PickBubble(DateTime now)
    {
        var activities = _activities.Items;
        bool eventWaits = _state != null && _upcomingEvent != null;
        bool focusWaits = _state != null && _focus.IsActive;
        int others = activities.Count + (eventWaits ? 1 : 0) + (focusWaits ? 1 : 0) - 1;

        var top = activities.FirstOrDefault();
        var item = top is { Warning: true } ? FromActivity(top)
                 : eventWaits ? EventBubble(_upcomingEvent!, now)
                 : focusWaits ? FocusBubble()
                 : top != null ? FromActivity(top)
                 : null;
        if (item == null || others <= 0)
            return item;
        string more = $"+{others}";
        return item with { Detail = item.Detail with { Value = item.Detail.Value == null ? more : $"{item.Detail.Value} · {more}" } };
    }

    /// <summary>The ring fills as the meeting gets closer, around the minutes left.</summary>
    private static BubbleItem EventBubble(CalendarEvent next, DateTime now)
    {
        var left = next.Start - now;
        int minutes = Math.Max(0, (int)Math.Ceiling(left.TotalMinutes));
        double progress = Math.Clamp(1 - left / UpcomingWindow, 0, 1);
        var detail = new HudMessage(Glyphs.Calendar, $"{next.Title} {CalendarReminders.Until(next.Start, now)}",
            DurationMs: 3000);
        return new BubbleItem(null, minutes.ToString(CultureInfo.InvariantCulture), progress, false, detail);
    }

    /// <summary>
    /// The ring empties as the phase runs out, around the minutes left (the seconds, in the last
    /// minute). Hovering gives the exact time.
    /// </summary>
    private BubbleItem FocusBubble()
    {
        var remaining = _focus.Remaining;
        string phase = _focus.Phase switch
        {
            FocusPhase.ShortBreak => Strings.Focus_ShortBreak,
            FocusPhase.LongBreak => Strings.Focus_LongBreak,
            _ => Strings.Focus_Focus,
        };
        string exact = $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
        var detail = new HudMessage(_focus.IsPaused ? PauseGlyph : Glyphs.Timer, $"{phase} · {exact}", DurationMs: 3000);
        if (_focus.IsPaused)
            return new BubbleItem(PauseGlyph, null, 1 - _focus.Progress, false, detail, Dim: true);
        int minutes = (int)Math.Ceiling(remaining.TotalMinutes);
        // At most a couple of characters fit inside the ring: hours past 99 minutes, seconds at the end.
        string label = remaining < TimeSpan.FromMinutes(1) ? $"{Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds))}s"
                     : minutes > 99 ? $"{(int)Math.Round(remaining.TotalHours)}h"
                     : minutes.ToString(CultureInfo.InvariantCulture);
        return new BubbleItem(null, label, 1 - _focus.Progress, false, detail);
    }

    private static BubbleItem FromActivity(Activity activity) =>
        new(activity.Glyph, null, activity.Progress, activity.Warning, ActivityNotice(activity, false));

    /// <summary>Hovering (or clicking) the bubble says what it is, in the notch beside it.</summary>
    private void OnBubbleMouseEnter(object sender, MouseEventArgs e)
    {
        if (_bubbleItem?.Detail is HudMessage detail && !_hudActive)
            ShowHud(detail);
    }

    private void OnBubbleClick(object sender, MouseButtonEventArgs e)
    {
        if (_bubbleItem?.Detail is HudMessage detail)
            ShowHud(detail);
        e.Handled = true;
    }

    /// <summary>Slides out from under the notch to its place beside it, following its width.</summary>
    private void ApplyBubbleFrame(double notchWidth, double compactOpacity)
    {
        double shown = Math.Max(_bubble.Value, 0);
        double opacity = Math.Clamp(shown, 0, 1) * compactOpacity;
        Bubble.Opacity = opacity;
        Bubble.Visibility = opacity > 0.001 ? Visibility.Visible : Visibility.Collapsed;
        Bubble.IsHitTestVisible = shown > 0.7 && compactOpacity > 0.7;
        BubbleShift.X = (notchWidth / 2 + BubbleGap + BubbleSize / 2) * shown;
    }
}
