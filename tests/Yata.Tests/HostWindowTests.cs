using Yata.Services;

namespace Yata.Tests;

/// <summary>The window an activity comes from, so a click on it goes back there.</summary>
public class HostWindowTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 15, 0, 0);
    private static readonly HostWindow Terminal = new(0x105FE, 19808);

    [Fact]
    public void The_window_travels_with_the_request()
    {
        string json = ActivityCommand.Build(new[] { "attention", "build", "--title", "Build" }, out _, Terminal)!;
        var request = ActivityRequest.Parse(json, out string? error);
        Assert.True(request != null, error);
        Assert.Equal(Terminal, request!.Window);
    }

    [Fact]
    public void Ending_doesnt_send_a_window() =>
        Assert.DoesNotContain("window", ActivityCommand.Build(new[] { "end", "build" }, out _, Terminal));

    [Fact]
    public void Agent_hooks_send_their_window()
    {
        var result = AgentHook.Translate("claude",
            """{"session_id":"s1","cwd":"C:\\src\\yata","hook_event_name":"PermissionRequest"}""", Terminal);
        Assert.Equal(Terminal, ActivityRequest.Parse(result.Request!, out _)!.Window);
    }

    [Theory]
    [InlineData("""{"op":"set","id":"x","title":"T","window":42}""")]
    [InlineData("""{"op":"set","id":"x","title":"T","window":{"handle":0,"pid":5}}""")]
    [InlineData("""{"op":"set","id":"x","title":"T","window":{"handle":5,"pid":-1}}""")]
    [InlineData("""{"op":"set","id":"x","title":"T","window":{"handle":"5","pid":5}}""")]
    [InlineData("""{"op":"set","id":"x","title":"T","window":{"handle":5}}""")]
    public void A_malformed_window_is_refused(string json) =>
        Assert.Null(ActivityRequest.Parse(json, out _));

    [Fact]
    public void Updates_without_a_window_keep_the_one_sent_before()
    {
        var board = new ActivityBoard();
        board.Apply(new ActivityRequest(ActivityOp.Set, "a", "Agent", Window: Terminal), Now);
        var (_, activity) = board.Apply(new ActivityRequest(ActivityOp.Set, "a", State: ActivityState.Attention), Now);
        Assert.Equal(Terminal, activity!.Window);
    }

    [Fact]
    public void A_dismissed_activity_comes_back_when_its_program_sends_it_again()
    {
        var board = new ActivityBoard();
        board.Apply(new ActivityRequest(ActivityOp.Set, "a", "Agent", State: ActivityState.Attention), Now);
        Assert.True(board.Remove("a"));
        Assert.Equal(0, board.Count);
        Assert.False(board.Remove("a"));
        Assert.Equal(ActivityChange.Added, board.Apply(new ActivityRequest(ActivityOp.Set, "a", "Agent"), Now).Change);
    }

    [Fact]
    public void The_jump_key_goes_to_the_most_pressing_window_then_cycles()
    {
        var board = new ActivityBoard();
        board.Apply(new ActivityRequest(ActivityOp.Set, "build", "Build", Window: new HostWindow(1, 1)), Now);
        board.Apply(new ActivityRequest(ActivityOp.Set, "script", "Script"), Now);
        board.Apply(new ActivityRequest(ActivityOp.Set, "agent", "Agent", State: ActivityState.Attention,
            Window: Terminal), Now.AddSeconds(-30));

        Assert.Equal("agent", board.NextWithWindow(null)!.Id);
        Assert.Equal("agent", board.NextWithWindow("agent")!.Id); // The build is only working.
        Assert.Equal("agent", board.NextWithWindow("gone")!.Id);

        board.Apply(new ActivityRequest(ActivityOp.Set, "agent2", "Agent 2", State: ActivityState.Attention,
            Window: new HostWindow(2, 2)), Now);
        Assert.Equal("agent2", board.NextWithWindow("agent")!.Id);
        Assert.Equal("agent", board.NextWithWindow("agent2")!.Id);

        // Nothing waits on you: then any activity with a window.
        board.ForgetWindow("agent");
        board.ForgetWindow("agent2");
        Assert.Equal("build", board.NextWithWindow(null)!.Id);
        board.ForgetWindow("build");
        Assert.Null(board.NextWithWindow(null));
    }

    [Fact]
    public void A_window_that_no_longer_matches_isnt_brought_forward()
    {
        // A handle that doesn't exist, and this test's own process: neither may be focused.
        Assert.False(new HostWindow(0x7FFFFFF0, 4242).Focus());
        Assert.False(new HostWindow(1, Environment.ProcessId).Focus());
    }

    [Fact]
    public void Finding_the_window_never_throws() => _ = HostWindow.Find();
}
