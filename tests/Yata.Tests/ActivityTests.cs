using Yata.Services;

namespace Yata.Tests;

/// <summary>Activities from other programs: what is accepted, how long they live, and the pipe.</summary>
public class ActivityTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 15, 0, 0);

    private static ActivityRequest Parse(string json)
    {
        var request = ActivityRequest.Parse(json, out string? error);
        Assert.True(request != null, error);
        return request!;
    }

    private static string Refusal(string json)
    {
        Assert.Null(ActivityRequest.Parse(json, out string? error));
        return error!;
    }

    [Fact]
    public void A_full_request_is_read()
    {
        var request = Parse("""{"op":"set","id":"build","title":"Build","text":"compiling","progress":0.4,"icon":"code","ttl":60,"state":"attention"}""");
        Assert.Equal(new ActivityRequest(ActivityOp.Set, "build", "Build", "compiling", 0.4, ActivityState.Attention, "code",
            TimeSpan.FromSeconds(60)), request);
    }

    [Theory]
    [InlineData("""not json""")]
    [InlineData("""[1,2]""")]
    [InlineData("""{"op":"run","id":"x"}""")]
    [InlineData("""{"op":"set","id":"has space","title":"T"}""")]
    [InlineData("""{"op":"set","id":"x","title":"T","progress":1.5}""")]
    [InlineData("""{"op":"set","id":"x","title":"T","progress":"half"}""")]
    [InlineData("""{"op":"set","id":"x","title":"T","state":"sleeping"}""")]
    [InlineData("""{"op":"set","id":"x","title":"T","icon":"skull"}""")]
    [InlineData("""{"op":"set","id":"x","title":"T","ttl":1}""")]
    [InlineData("""{"op":"set","id":"x","title":42}""")]
    public void Anything_unexpected_is_refused_with_a_reason(string json) =>
        Assert.False(string.IsNullOrEmpty(Refusal(json)));

    [Fact]
    public void Text_is_one_plain_line()
    {
        // Line breaks, tabs, a right-to-left override and a private-use glyph (an icon-font character).
        var request = Parse("{\"op\":\"set\",\"id\":\"x\",\"title\":\"  Deploy\\n\\tdone \\u202Egnp.exe \\uE73E \"}");
        Assert.Equal("Deploy done gnp.exe", request.Title);
    }

    [Fact]
    public void Long_text_is_cut_with_an_ellipsis()
    {
        string cleaned = ActivityRequest.Clean(new string('a', 200), ActivityRequest.MaxTitle)!;
        Assert.Equal(ActivityRequest.MaxTitle, cleaned.Length);
        Assert.EndsWith("…", cleaned);
    }

    [Fact]
    public void Cutting_never_splits_an_emoji()
    {
        string cleaned = ActivityRequest.Clean(new string('a', 58) + "😀😀", ActivityRequest.MaxTitle)!;
        Assert.False(char.IsHighSurrogate(cleaned[^2]));
    }

    [Fact]
    public void A_new_activity_needs_a_title_but_updates_keep_it()
    {
        var board = new ActivityBoard();
        Assert.Equal(ActivityChange.NeedsTitle, board.Apply(new ActivityRequest(ActivityOp.Set, "x"), Now).Change);

        board.Apply(new ActivityRequest(ActivityOp.Set, "x", "Build", "starting", Icon: "code"), Now);
        var (change, activity) = board.Apply(new ActivityRequest(ActivityOp.Set, "x", Progress: 0.5), Now.AddSeconds(5));
        Assert.Equal(ActivityChange.Updated, change);
        Assert.Equal("Build", activity!.Title);
        Assert.Equal("starting", activity.Text);
        Assert.Equal("code", activity.Icon);
        Assert.Equal(0.5, activity.Progress);
    }

    [Fact]
    public void Reaching_another_state_is_reported_as_such()
    {
        var board = new ActivityBoard();
        board.Apply(new ActivityRequest(ActivityOp.Set, "x", "Agent"), Now);
        var (change, activity) = board.Apply(new ActivityRequest(ActivityOp.Set, "x", State: ActivityState.Attention), Now);
        Assert.Equal(ActivityChange.StateChanged, change);
        Assert.True(activity!.Warning);
    }

    [Fact]
    public void Done_drops_the_progress_and_goes_away_soon()
    {
        var board = new ActivityBoard();
        board.Apply(new ActivityRequest(ActivityOp.Set, "x", "Build", Progress: 0.9, Ttl: TimeSpan.FromHours(1)), Now);
        var (_, done) = board.Apply(new ActivityRequest(ActivityOp.Set, "x", State: ActivityState.Done), Now);
        Assert.Null(done!.Progress);
        Assert.Equal(ActivityIcons.Done, done.Glyph);

        Assert.False(board.Prune(Now + ActivityBoard.DoneTtl - TimeSpan.FromSeconds(1)));
        Assert.True(board.Prune(Now + ActivityBoard.DoneTtl));
        Assert.Equal(0, board.Count);
    }

    [Fact]
    public void An_activity_nobody_updates_expires()
    {
        var board = new ActivityBoard();
        board.Apply(new ActivityRequest(ActivityOp.Set, "x", "Script"), Now);
        Assert.Equal(Now + ActivityBoard.DefaultTtl, board.NextExpiry);
        Assert.True(board.Prune(Now + ActivityBoard.DefaultTtl));
    }

    [Theory]
    [InlineData(ActivityState.Attention)]
    [InlineData(ActivityState.Failed)]
    public void What_needs_the_user_stays_up_to_an_hour_whatever_the_ttl(ActivityState state)
    {
        var board = new ActivityBoard();
        board.Apply(new ActivityRequest(ActivityOp.Set, "agent", "Claude Code", State: state, Ttl: TimeSpan.FromSeconds(5)), Now);
        Assert.False(board.Prune(Now + ActivityBoard.DefaultTtl));
        Assert.True(board.Prune(Now + ActivityBoard.WarningTtl));
    }

    [Fact]
    public void Needing_the_user_comes_first_then_the_latest()
    {
        var board = new ActivityBoard();
        board.Apply(new ActivityRequest(ActivityOp.Set, "old", "Old"), Now);
        board.Apply(new ActivityRequest(ActivityOp.Set, "help", "Help", State: ActivityState.Attention), Now.AddSeconds(1));
        board.Apply(new ActivityRequest(ActivityOp.Set, "new", "New"), Now.AddSeconds(2));
        board.Apply(new ActivityRequest(ActivityOp.Set, "done", "Done", State: ActivityState.Done), Now.AddSeconds(3));
        Assert.Equal(new[] { "help", "new", "old", "done" }, board.Items.Select(a => a.Id));
    }

    [Fact]
    public void There_is_a_limit_on_how_many_are_up()
    {
        var board = new ActivityBoard();
        for (int i = 0; i < ActivityBoard.MaxActivities; i++)
            board.Apply(new ActivityRequest(ActivityOp.Set, $"a{i}", "A"), Now);
        Assert.Equal(ActivityChange.Full, board.Apply(new ActivityRequest(ActivityOp.Set, "one-more", "A"), Now).Change);
        // Updating one that's already up still works.
        Assert.Equal(ActivityChange.Updated, board.Apply(new ActivityRequest(ActivityOp.Set, "a0", Text: "x"), Now).Change);
    }

    [Fact]
    public void End_removes_it()
    {
        var board = new ActivityBoard();
        board.Apply(new ActivityRequest(ActivityOp.Set, "x", "Build"), Now);
        Assert.Equal(ActivityChange.Ended, board.Apply(Parse("""{"op":"end","id":"x"}"""), Now).Change);
        Assert.Equal(ActivityChange.Unknown, board.Apply(Parse("""{"op":"end","id":"x"}"""), Now).Change);
    }

    // ---- Command line ------------------------------------------------------------------

    [Fact]
    public void The_command_line_becomes_a_request_the_notch_accepts()
    {
        string? json = ActivityCommand.Build(
            new[] { "set", "build", "--title", "Build", "--text", "compiling", "--progress", "40%", "--icon", "code", "--ttl", "120" },
            out string? error);
        Assert.Null(error);
        Assert.Equal(new ActivityRequest(ActivityOp.Set, "build", "Build", "compiling", 0.4, ActivityState.Running, "code",
            TimeSpan.FromSeconds(120)), Parse(json!));
    }

    [Theory]
    [InlineData("done", ActivityState.Done)]
    [InlineData("fail", ActivityState.Failed)]
    [InlineData("attention", ActivityState.Attention)]
    public void Each_action_sets_its_state(string action, ActivityState state) =>
        Assert.Equal(state, Parse(ActivityCommand.Build(new[] { action, "x" }, out _)!).State);

    [Fact]
    public void End_on_the_command_line()
    {
        Assert.Equal(ActivityOp.End, Parse(ActivityCommand.Build(new[] { "end", "x" }, out _)!).Op);
    }

    [Theory]
    [InlineData("")]
    [InlineData("set")]
    [InlineData("launch x")]
    [InlineData("set x --title")]
    [InlineData("set x --colour red")]
    [InlineData("set x --progress 140%")]
    [InlineData("set x --ttl soon")]
    [InlineData("set x --icon skull")]
    public void Bad_arguments_say_what_is_wrong(string line)
    {
        Assert.Null(ActivityCommand.Build(line.Split(' ', StringSplitOptions.RemoveEmptyEntries), out string? error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public async Task Exit_codes_tell_scripts_what_happened()
    {
        string name = $"Yata.Tests.{Guid.NewGuid():N}";
        var errors = new List<string>();
        Assert.Equal(1, await ActivityCommand.RunAsync(new[] { "set" }, errors.Add, name));
        Assert.Equal(2, await ActivityCommand.RunAsync(new[] { "end", "x" }, errors.Add, name));
        Assert.Contains("isn't running", errors[^1]);

        string reply = "ok";
        using var pipe = new ActivityPipe(_ => Task.FromResult(reply), name);
        pipe.Start();
        Assert.Equal(0, await ActivityCommand.RunAsync(new[] { "end", "x" }, errors.Add, name));
        reply = "error: nope";
        Assert.Equal(3, await ActivityCommand.RunAsync(new[] { "end", "x" }, errors.Add, name));
        Assert.Equal("error: nope", errors[^1]);
    }

    [Fact]
    public async Task Help_is_not_an_error()
    {
        string? shown = null;
        Assert.Equal(0, await ActivityCommand.RunAsync(new[] { "--help" }, text => shown = text));
        Assert.Contains("yata-cli activity set", shown);
    }

    [Theory]
    [InlineData("0.25", 0.25)]
    [InlineData("25%", 0.25)]
    [InlineData("1", 1.0)]
    public void Progress_as_a_fraction_or_a_percentage(string value, double expected) =>
        Assert.Equal(expected, ActivityCommand.ParseProgress(value));

    // ---- Pipe ----------------------------------------------------------------------------

    [Fact]
    public async Task A_request_goes_through_the_pipe_and_the_answer_comes_back()
    {
        string name = $"Yata.Tests.{Guid.NewGuid():N}";
        string? received = null;
        using var pipe = new ActivityPipe(line => { received = line; return Task.FromResult("ok"); }, name);
        pipe.Start();

        string? reply = await ActivityClient.SendAsync("""{"op":"end","id":"x"}""", name);
        Assert.Equal("ok", reply);
        Assert.Equal("""{"op":"end","id":"x"}""", received);
    }

    [Fact]
    public async Task Nobody_listening_is_reported_as_null()
    {
        Assert.Null(await ActivityClient.SendAsync("{}", $"Yata.Tests.{Guid.NewGuid():N}", timeoutMs: 300));
    }

    [Fact]
    public async Task An_oversized_request_is_refused()
    {
        using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(new string('x', 5000) + "\n"));
        Assert.Null(await ActivityClient.ReadLine(stream, ActivityRequest.MaxBytes, CancellationToken.None));
    }
}
