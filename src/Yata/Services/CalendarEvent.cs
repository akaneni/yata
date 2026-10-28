using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Yata.Services;

/// <param name="MeetingUrl">Video call link (Meet, Zoom, Teams, ...), always https.</param>
public sealed record CalendarEvent(string Id, string Title, DateTime Start, DateTime End, bool AllDay, string? MeetingUrl)
{
    /// <summary>Identity for reminders: a rescheduled event reminds again.</summary>
    public string Key => Id + "|" + Start.ToString("O", CultureInfo.InvariantCulture);

    public bool IsOngoing(DateTime now) => !AllDay && Start <= now && now < End;
}

public sealed record CalendarInfo(string Id, string Name, bool SelectedInGoogle, bool Primary);

/// <summary>Turns Calendar API items into <see cref="CalendarEvent"/>s.</summary>
internal static class CalendarEvents
{
    private static readonly Regex Url = new(@"https://[^\s""'<>\\]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Hosts whose links are video calls. Anything else in a description is left alone.
    private static readonly string[] MeetingHosts =
    {
        "meet.google.com", "zoom.us", "zoomgov.com", "teams.microsoft.com", "teams.cloud.microsoft", "teams.live.com",
        "webex.com", "whereby.com", "meet.jit.si",
    };

    // Outlook's Safe Links wraps every link in a company invite: the call is in its url= parameter.
    private const string SafeLinksHost = "safelinks.protection.outlook.com";

    public static CalendarEvent? Parse(JsonElement item)
    {
        if (Str(item, "status") == "cancelled")
            return null;
        // Working-location entries ("Home", "Office") aren't things to attend.
        if (Str(item, "eventType") == "workingLocation")
            return null;
        if (DeclinedBySelf(item))
            return null;

        if (!item.TryGetProperty("start", out var start) || !item.TryGetProperty("end", out var end))
            return null;

        DateTime from, to;
        bool allDay;
        if (Str(start, "dateTime") is string startTime && Str(end, "dateTime") is string endTime)
        {
            if (!DateTimeOffset.TryParse(startTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var s) ||
                !DateTimeOffset.TryParse(endTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var e))
                return null;
            from = s.LocalDateTime;
            to = e.LocalDateTime;
            allDay = false;
        }
        else if (Str(start, "date") is string startDate && Str(end, "date") is string endDate)
        {
            if (!DateTime.TryParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out from) ||
                !DateTime.TryParseExact(endDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out to))
                return null;
            allDay = true;
        }
        else
        {
            return null;
        }

        string title = Str(item, "summary")?.Trim() is { Length: > 0 } summary ? summary : Strings.Calendar_Untitled;
        string id = Str(item, "iCalUID") ?? Str(item, "id") ?? title;
        return new CalendarEvent(id, title, from, to, allDay, FindMeetingUrl(item));
    }

    /// <summary>Conference data first (what Google shows as "Join"), then links in location or description.</summary>
    public static string? FindMeetingUrl(JsonElement item)
    {
        if (item.TryGetProperty("conferenceData", out var conference) &&
            conference.TryGetProperty("entryPoints", out var entryPoints) && entryPoints.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in entryPoints.EnumerateArray())
            {
                if (Str(entry, "entryPointType") == "video" && SafeUrl(Str(entry, "uri"), anyHost: true) is string uri)
                    return uri;
            }
        }

        if (SafeUrl(Str(item, "hangoutLink"), anyHost: true) is string hangout)
            return hangout;

        foreach (var field in new[] { "location", "description" })
        {
            if (Str(item, field) is not string text)
                continue;
            foreach (Match match in Url.Matches(text))
            {
                if (SafeUrl(match.Value.TrimEnd('.', ',', ')', ';'), anyHost: false) is string url)
                    return url;
            }
        }
        return null;
    }

    /// <summary>Only https links; from free text, only known video-call hosts.</summary>
    private static string? SafeUrl(string? candidate, bool anyHost)
    {
        if (candidate == null || !Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return null;
        if (uri.Host.EndsWith("." + SafeLinksHost, StringComparison.OrdinalIgnoreCase) &&
            System.Web.HttpUtility.ParseQueryString(uri.Query)["url"] is string wrapped)
            return SafeUrl(wrapped, anyHost);
        if (!anyHost && !MeetingHosts.Any(h => uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase) ||
                                               uri.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase)))
            return null;
        return uri.AbsoluteUri;
    }

    private static bool DeclinedBySelf(JsonElement item)
    {
        if (!item.TryGetProperty("attendees", out var attendees) || attendees.ValueKind != JsonValueKind.Array)
            return false;
        return attendees.EnumerateArray().Any(a =>
            a.TryGetProperty("self", out var self) && self.ValueKind == JsonValueKind.True &&
            Str(a, "responseStatus") == "declined");
    }

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary>When to tell the user about an event. Pure, so it's testable with a fake clock.</summary>
internal static class CalendarReminders
{
    /// <summary>The "starting now" notice still makes sense this long after the start.</summary>
    public static readonly TimeSpan StartGrace = TimeSpan.FromMinutes(2);

    /// <summary>Notices due at <paramref name="now"/>; each one is recorded in <paramref name="sent"/> so it fires once.</summary>
    public static List<(CalendarEvent Event, bool Starting)> Due(
        IEnumerable<CalendarEvent> events, DateTime now, TimeSpan lead, ISet<string> sent)
    {
        var due = new List<(CalendarEvent, bool)>();
        foreach (var e in events.Where(e => !e.AllDay))
        {
            if (lead > TimeSpan.Zero && now >= e.Start - lead && now < e.Start && sent.Add(e.Key + "|soon"))
                due.Add((e, false));
            if (now >= e.Start && now < e.Start + StartGrace && sent.Add(e.Key + "|start"))
                due.Add((e, true));
        }
        return due;
    }

    /// <summary>The next timed event starting within <paramref name="window"/>, for the closed notch.</summary>
    public static CalendarEvent? NextWithin(IEnumerable<CalendarEvent> events, DateTime now, TimeSpan window) =>
        events.Where(e => !e.AllDay && e.Start > now && e.Start - now <= window).MinBy(e => e.Start);

    /// <summary>"in 8 min", "in 1h05", "now".</summary>
    public static string Until(DateTime start, DateTime now)
    {
        var left = start - now;
        if (left <= TimeSpan.Zero)
            return Strings.Calendar_Now;
        int minutes = (int)Math.Ceiling(left.TotalMinutes);
        return minutes < 60 ? string.Format(Strings.Calendar_InMinutes, minutes) : string.Format(Strings.Calendar_InHours, minutes / 60, minutes % 60);
    }
}
