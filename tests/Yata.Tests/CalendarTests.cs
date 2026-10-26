using System.Text;
using System.Text.Json;
using Yata.Services;
using Yata.Views;

namespace Yata.Tests;

/// <summary>Google Calendar: reading API items, finding call links, when to remind, and the OAuth pieces.</summary>
public class CalendarTests
{
    private static readonly DateTime Nine = new(2026, 10, 26, 9, 0, 0);

    private static CalendarEvent? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return CalendarEvents.Parse(doc.RootElement);
    }

    private static CalendarEvent Event(string title, DateTime start, int minutes = 30, string? url = null) =>
        new(title, title, start, start.AddMinutes(minutes), false, url);

    // ---- Parsing ------------------------------------------------------------------------

    [Fact]
    public void Timed_event_is_converted_to_local_time()
    {
        var e = Parse("""
            {"id":"a","summary":" Daily ","start":{"dateTime":"2026-10-26T12:00:00Z"},"end":{"dateTime":"2026-10-26T12:15:00Z"}}
            """)!;

        Assert.Equal("Daily", e.Title);
        Assert.False(e.AllDay);
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero).LocalDateTime, e.Start);
        Assert.Equal(TimeSpan.FromMinutes(15), e.End - e.Start);
    }

    [Fact]
    public void All_day_event_uses_dates()
    {
        var e = Parse("""
            {"id":"b","summary":"Feriado","start":{"date":"2026-11-02"},"end":{"date":"2026-11-03"}}
            """)!;

        Assert.True(e.AllDay);
        Assert.Equal(new DateTime(2026, 11, 2), e.Start);
    }

    [Fact]
    public void Untitled_event_gets_a_placeholder()
    {
        var e = Parse("""{"id":"c","start":{"dateTime":"2026-10-26T09:00:00-03:00"},"end":{"dateTime":"2026-10-26T10:00:00-03:00"}}""")!;
        Assert.Equal("(Sem título)", e.Title);
    }

    [Theory]
    [InlineData("""{"status":"cancelled","start":{"date":"2026-10-26"},"end":{"date":"2026-10-27"}}""")]
    [InlineData("""{"eventType":"workingLocation","start":{"date":"2026-10-26"},"end":{"date":"2026-10-27"}}""")]
    [InlineData("""{"attendees":[{"self":true,"responseStatus":"declined"}],"start":{"date":"2026-10-26"},"end":{"date":"2026-10-27"}}""")]
    [InlineData("""{"summary":"sem horário"}""")]
    public void Events_not_worth_showing_are_skipped(string json) => Assert.Null(Parse(json));

    [Fact]
    public void Event_declined_by_someone_else_still_shows()
    {
        var e = Parse("""
            {"attendees":[{"responseStatus":"declined"},{"self":true,"responseStatus":"accepted"}],
             "start":{"date":"2026-10-26"},"end":{"date":"2026-10-27"}}
            """);
        Assert.NotNull(e);
    }

    // ---- Call links ---------------------------------------------------------------------

    private static string? Link(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return CalendarEvents.FindMeetingUrl(doc.RootElement);
    }

    [Fact]
    public void Conference_video_entry_wins()
    {
        Assert.Equal("https://meet.google.com/abc-defg-hij", Link("""
            {"conferenceData":{"entryPoints":[{"entryPointType":"phone","uri":"tel:+55-11"},
                                              {"entryPointType":"video","uri":"https://meet.google.com/abc-defg-hij"}]},
             "hangoutLink":"https://meet.google.com/other"}
            """));
    }

    [Fact]
    public void Zoom_link_is_found_in_the_description_html()
    {
        Assert.Equal("https://empresa.zoom.us/j/123?pwd=x", Link("""
            {"description":"Pauta<br><a href=\"https://empresa.zoom.us/j/123?pwd=x\">Entrar</a>"}
            """));
    }

    [Fact]
    public void Teams_link_in_location_is_found()
    {
        Assert.StartsWith("https://teams.microsoft.com/l/meetup-join/", Link("""
            {"location":"Sala 3 / https://teams.microsoft.com/l/meetup-join/19%3a, ramal 22"}
            """));
    }

    [Theory]
    [InlineData("""{"description":"https://evil.example.com/meet.google.com"}""")]
    [InlineData("""{"description":"http://meet.google.com/abc"}""")]
    [InlineData("""{"description":"https://zoom.us.evil.com/j/1"}""")]
    [InlineData("""{"hangoutLink":"javascript:alert(1)"}""")]
    public void Unknown_or_unsafe_links_are_ignored(string json) => Assert.Null(Link(json));

    // ---- Reminders ----------------------------------------------------------------------

    [Fact]
    public void Reminds_once_before_and_once_at_start()
    {
        var sent = new HashSet<string>();
        var events = new[] { Event("Daily", Nine) };
        var lead = TimeSpan.FromMinutes(5);

        Assert.Empty(CalendarReminders.Due(events, Nine.AddMinutes(-6), lead, sent));

        var soon = CalendarReminders.Due(events, Nine.AddMinutes(-5), lead, sent);
        Assert.Single(soon);
        Assert.False(soon[0].Starting);
        Assert.Empty(CalendarReminders.Due(events, Nine.AddMinutes(-3), lead, sent));

        var start = CalendarReminders.Due(events, Nine.AddSeconds(10), lead, sent);
        Assert.Single(start);
        Assert.True(start[0].Starting);
        Assert.Empty(CalendarReminders.Due(events, Nine.AddMinutes(1), lead, sent));
    }

    [Fact]
    public void Event_already_well_underway_when_first_seen_is_not_announced()
    {
        var sent = new HashSet<string>();
        Assert.Empty(CalendarReminders.Due(new[] { Event("Daily", Nine) }, Nine.AddMinutes(10), TimeSpan.FromMinutes(5), sent));
    }

    [Fact]
    public void Zero_lead_only_announces_the_start()
    {
        var sent = new HashSet<string>();
        var events = new[] { Event("Daily", Nine) };
        Assert.Empty(CalendarReminders.Due(events, Nine.AddMinutes(-1), TimeSpan.Zero, sent));
        Assert.Single(CalendarReminders.Due(events, Nine, TimeSpan.Zero, sent));
    }

    [Fact]
    public void Rescheduled_event_reminds_again()
    {
        var sent = new HashSet<string>();
        var lead = TimeSpan.FromMinutes(5);
        CalendarReminders.Due(new[] { Event("Daily", Nine) }, Nine, lead, sent);

        var moved = Event("Daily", Nine.AddHours(1));
        Assert.Single(CalendarReminders.Due(new[] { moved }, Nine.AddMinutes(56), lead, sent));
    }

    [Fact]
    public void All_day_events_never_remind()
    {
        var holiday = new CalendarEvent("h", "Feriado", Nine.Date, Nine.Date.AddDays(1), true, null);
        Assert.Empty(CalendarReminders.Due(new[] { holiday }, Nine.Date, TimeSpan.FromMinutes(5), new HashSet<string>()));
    }

    [Fact]
    public void Next_event_within_window_is_the_earliest_upcoming_one()
    {
        var events = new[] { Event("Almoço", Nine.AddHours(3)), Event("Daily", Nine.AddMinutes(10)), Event("Agora", Nine.AddMinutes(-5)) };
        Assert.Equal("Daily", CalendarReminders.NextWithin(events, Nine, TimeSpan.FromMinutes(15))?.Title);
        Assert.Null(CalendarReminders.NextWithin(events, Nine, TimeSpan.FromMinutes(5)));
    }

    [Theory]
    [InlineData(0, "agora")]
    [InlineData(30, "em 1 min")]
    [InlineData(8 * 60, "em 8 min")]
    [InlineData(65 * 60, "em 1h05")]
    public void Until_reads_naturally(int seconds, string expected) =>
        Assert.Equal(expected, CalendarReminders.Until(Nine.AddSeconds(seconds), Nine));

    // ---- Agenda page --------------------------------------------------------------------

    [Fact]
    public void Agenda_lists_today_then_tomorrow_under_a_divider()
    {
        var rows = AgendaPage.BuildRows(new[]
        {
            Event("Ontem cedo", Nine.AddHours(-3)),
            Event("Daily", Nine.AddMinutes(-10), 30, "https://meet.google.com/x"),
            Event("1:1", Nine.AddHours(2)),
            Event("Planejamento", Nine.AddDays(1)),
        }, Nine);

        Assert.Equal(new[] { "Daily", "1:1", "Amanhã", "Planejamento" }, rows.Select(r => r.Title));
        Assert.True(rows[0].IsNow);
        Assert.True(rows[0].HasMeeting);
        Assert.True(rows[2].IsHeader);
    }

    // ---- OAuth pieces -------------------------------------------------------------------

    [Fact]
    public void Pkce_challenge_matches_rfc7636_example() =>
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", Pkce.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));

    [Fact]
    public void Pkce_verifier_is_long_and_url_safe()
    {
        string verifier = Pkce.NewVerifier();
        Assert.InRange(verifier.Length, 43, 128);
        Assert.Matches("^[A-Za-z0-9_-]+$", verifier);
        Assert.NotEqual(verifier, Pkce.NewVerifier());
    }

    [Fact]
    public void Redirect_query_is_decoded()
    {
        var query = GoogleAuth.ParseRedirect("/?state=abc&code=4%2F0Ab_x&scope=email+openid")!;
        Assert.Equal("abc", query["state"]);
        Assert.Equal("4/0Ab_x", query["code"]);
        Assert.Equal("email openid", query["scope"]);
    }

    [Fact]
    public async Task Browser_gets_the_whole_page_and_the_code_reaches_the_app()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            var waiting = GoogleAuth.WaitForCodeAsync(listener, "s3", new CancellationTokenSource(10_000).Token);
            using var http = new System.Net.Http.HttpClient();

            // Like a browser: a favicon request and a forged redirect come first and are ignored.
            Assert.Equal(System.Net.HttpStatusCode.NotFound, (await http.GetAsync($"http://127.0.0.1:{port}/favicon.ico")).StatusCode);
            Assert.Equal(System.Net.HttpStatusCode.NotFound, (await http.GetAsync($"http://127.0.0.1:{port}/?state=other&code=x")).StatusCode);

            string page = await http.GetStringAsync($"http://127.0.0.1:{port}/?state=s3&code=4%2Fabc&scope=email");
            Assert.Contains("Conta Google conectada", page);
            Assert.EndsWith("</html>", page);
            Assert.Equal("4/abc", await waiting);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void Other_paths_are_not_the_redirect() => Assert.Null(GoogleAuth.ParseRedirect("/favicon.ico"));

    [Fact]
    public void Granted_scopes_must_include_the_calendar()
    {
        Assert.True(GoogleAuth.HasScopes($"openid {GoogleAuth.EventsScope} email {GoogleAuth.CalendarListScope}",
            GoogleAuth.CalendarListScope, GoogleAuth.EventsScope));
        Assert.False(GoogleAuth.HasScopes("openid email", GoogleAuth.CalendarListScope, GoogleAuth.EventsScope));
    }

    [Fact]
    public void Email_is_read_from_the_id_token()
    {
        string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"email":"corvo@example.com","sub":"1"}"""))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal("corvo@example.com", GoogleAuth.EmailFromIdToken($"header.{payload}.signature"));
        Assert.Null(GoogleAuth.EmailFromIdToken("not-a-jwt"));
    }
}
