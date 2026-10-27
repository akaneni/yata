using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Yata.Helpers;
using Yata.Services;

namespace Yata.Views;

/// <summary>One line of the agenda: an event, or a day divider.</summary>
public sealed record AgendaRow(string Time, string Title, string Detail, string? MeetingUrl, bool IsNow, bool IsPast, bool IsHeader = false)
{
    public bool HasMeeting => MeetingUrl != null;
}

public partial class AgendaPage : UserControl
{

    // "Ongoing" and "past" drift with the clock, so the list is rebuilt while it's on screen.
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(30) };
    private GoogleCalendar? _calendar;

    public AgendaPage()
    {
        InitializeComponent();
        _refresh.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                Refresh();
                _refresh.Start();
            }
            else
            {
                _refresh.Stop();
            }
        };
    }

    public void Attach(GoogleCalendar calendar)
    {
        _calendar = calendar;
        calendar.Changed += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        if (_calendar == null)
            return;

        var now = DateTime.Now;
        var rows = _calendar.Status == CalendarStatus.Connected ? BuildRows(_calendar.Events, now) : new List<AgendaRow>();
        List.ItemsSource = rows;
        ListScroll.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        if (rows.Count == 0)
            ShowEmpty(now);
    }

    private void ShowEmpty(DateTime now)
    {
        var (text, button, frame) = _calendar!.Status switch
        {
            CalendarStatus.NotConfigured => (Strings.Agenda_NotConfigured, null, RavenFrame.Perch),
            CalendarStatus.Connecting => (Strings.Agenda_Connecting, Strings.Common_Cancel, RavenFrame.Perch),
            CalendarStatus.NeedsReconnect => (_calendar.Error ?? Strings.Agenda_ConnectAgain, Strings.Google_Reconnect, RavenFrame.Perch),
            CalendarStatus.SignedOut => (_calendar.Error ?? Strings.Agenda_SignedOut, Strings.Agenda_Connect, RavenFrame.Perch),
            _ when _calendar.Error != null => (_calendar.Error, null, RavenFrame.Perch),
            _ => (now.Hour >= 18 ? Strings.Agenda_NothingLeft : Strings.Agenda_Free, (string?)null, RavenFrame.Sleep),
        };

        EmptyText.Text = text;
        EmptyRaven.Frame = frame;
        EmptyButton.Content = button;
        EmptyButton.Visibility = button == null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Today's events that haven't ended long ago, then tomorrow's under a divider.</summary>
    internal static List<AgendaRow> BuildRows(IEnumerable<CalendarEvent> events, DateTime now)
    {
        var rows = new List<AgendaRow>();
        var today = now.Date;
        // Keep the last hour's events: "what was that meeting?" right after it ends.
        var visible = events.Where(e => e.AllDay ? e.End > today : e.End > now.AddHours(-1)).ToList();

        foreach (var group in visible.GroupBy(e => e.AllDay || e.Start < today ? today : e.Start.Date).OrderBy(g => g.Key))
        {
            if (group.Key != today)
                rows.Add(new AgendaRow("", group.Key == today.AddDays(1) ? Strings.Agenda_Tomorrow : group.Key.ToString(Strings.Format_WeekdayDate, Lang.Culture), "", null,
                    false, false, IsHeader: true));

            foreach (var e in group.OrderBy(e => !e.AllDay).ThenBy(e => e.Start))
            {
                bool past = !e.AllDay && e.End <= now;
                bool ongoing = e.IsOngoing(now);
                string detail = e.AllDay ? Strings.Agenda_AllDay
                              : ongoing ? string.Format(Strings.Agenda_NowUntil, e.End)
                              : past ? string.Format(Strings.Agenda_Ended, e.End)
                              : e.Start - now <= TimeSpan.FromHours(2) && e.Start.Date == today
                                  ? string.Format(Strings.Agenda_SoonUntil, CalendarReminders.Until(e.Start, now), e.End)
                                  : string.Format(Strings.Agenda_Until, e.End);
                rows.Add(new AgendaRow(e.AllDay ? "—" : e.Start.ToString("HH:mm"), e.Title, detail, e.MeetingUrl, ongoing, past));
            }
        }
        return rows;
    }

    private void OnEmptyButton(object sender, RoutedEventArgs e)
    {
        if (_calendar == null)
            return;
        if (_calendar.Status == CalendarStatus.Connecting)
            _calendar.CancelConnect();
        else
            _ = _calendar.ConnectAsync();
    }

    private void OnJoin(object sender, RoutedEventArgs e)
    {
        // Links were checked when the event was read: https only.
        if (sender is FrameworkElement { DataContext: AgendaRow { MeetingUrl: string url } })
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception)
            {
                // No browser registered; nothing sensible to do from the notch.
            }
        }
    }
}
