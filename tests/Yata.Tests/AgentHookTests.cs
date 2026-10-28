using System.IO;
using System.Text.Json;
using Yata.Services;

namespace Yata.Tests;

/// <summary>AI agents' hook events (Claude Code, Cursor, Codex) turned into activities in the notch.</summary>
public class AgentHookTests
{
    private static ActivityRequest? Request(string agent, string payload)
    {
        var result = AgentHook.Translate(agent, payload);
        if (result.Request == null)
            return null;
        // Whatever the hook sends must pass Yata's own checks.
        var request = ActivityRequest.Parse(result.Request, out string? error);
        Assert.True(request != null, error);
        return request;
    }

    private static string Claude(string evt, string extra = "") =>
        $$"""{"session_id":"8f2c-41ab","cwd":"C:\\src\\yata","hook_event_name":"{{evt}}"{{extra}}}""";

    [Theory]
    [InlineData("UserPromptSubmit", ActivityState.Running, "working")]
    [InlineData("PostToolUse", ActivityState.Running, "working")]
    [InlineData("PermissionRequest", ActivityState.Attention, "permission")]
    [InlineData("Stop", ActivityState.Done, "done")]
    [InlineData("StopFailure", ActivityState.Failed, "failed")]
    public void Claude_code_states(string evt, ActivityState state, string phrase)
    {
        var request = Request("claude", Claude(evt))!;
        Assert.Equal("claude-8f2c-41ab", request.Id);
        Assert.Equal("Claude Code · yata", request.Title);
        Assert.Equal(state, request.State);
        Assert.Equal(phrase, request.Phrase);
        Assert.Equal("agent", request.Icon);
        Assert.Null(request.Text); // Nothing from the conversation, only the state.
    }

    [Fact]
    public void Claude_code_waiting_and_permission_notices()
    {
        Assert.Equal("waiting", Request("claude", Claude("Notification", ",\"notification_type\":\"idle_prompt\""))!.Phrase);
        Assert.Equal("permission", Request("claude", Claude("Notification", ",\"notification_type\":\"permission_prompt\""))!.Phrase);
        Assert.Null(Request("claude", Claude("Notification", ",\"notification_type\":\"auth_success\"")));
    }

    [Fact]
    public void Claude_code_session_end_removes_it_and_nothing_is_printed()
    {
        var result = AgentHook.Translate("claude", Claude("SessionEnd"));
        Assert.Equal(ActivityOp.End, ActivityRequest.Parse(result.Request!, out _)!.Op);
        Assert.Null(result.Output);
        Assert.Null(AgentHook.Translate("claude", Claude("Stop")).Output);
    }

    [Theory]
    [InlineData("""{"hook_event_name":"beforeSubmitPrompt","conversation_id":"c1","workspace_roots":["/c:/src/site"]}""", ActivityState.Running, """{"continue":true}""")]
    [InlineData("""{"hook_event_name":"postToolUse","conversation_id":"c1","workspace_roots":["/c:/src/site"]}""", ActivityState.Running, null)]
    [InlineData("""{"hook_event_name":"stop","status":"completed","conversation_id":"c1","workspace_roots":["/c:/src/site"]}""", ActivityState.Done, "{}")]
    [InlineData("""{"hook_event_name":"stop","status":"error","conversation_id":"c1","workspace_roots":["/c:/src/site"]}""", ActivityState.Failed, "{}")]
    public void Cursor_states_and_the_neutral_answers_it_expects(string payload, ActivityState state, string? output)
    {
        var result = AgentHook.Translate("cursor", payload);
        var request = ActivityRequest.Parse(result.Request!, out _)!;
        Assert.Equal("cursor-c1", request.Id);
        Assert.Equal("Cursor · site", request.Title);
        Assert.Equal(state, request.State);
        Assert.Equal(output, result.Output);
    }

    [Fact]
    public void Cursor_aborted_by_the_user_just_goes_away()
    {
        var result = AgentHook.Translate("cursor", """{"hook_event_name":"stop","status":"aborted","conversation_id":"c1"}""");
        Assert.Equal(ActivityOp.End, ActivityRequest.Parse(result.Request!, out _)!.Op);
        Assert.Equal("{}", result.Output);
    }

    [Fact]
    public void Cursor_permission_hooks_are_never_answered()
    {
        // These decide permissions; yata-cli isn't registered on them, and wouldn't answer if it were.
        foreach (string evt in new[] { "preToolUse", "beforeShellExecution", "beforeMCPExecution", "beforeReadFile" })
        {
            var result = AgentHook.Translate("cursor", $$"""{"hook_event_name":"{{evt}}","conversation_id":"c1"}""");
            Assert.Null(result.Request);
            Assert.Null(result.Output);
        }
    }

    [Theory]
    [InlineData("UserPromptSubmit", ActivityState.Running, null)]
    [InlineData("PermissionRequest", ActivityState.Attention, null)]
    [InlineData("PostToolUse", ActivityState.Running, null)]
    [InlineData("Stop", ActivityState.Done, "{}")]
    public void Codex_hooks(string evt, ActivityState state, string? output)
    {
        var result = AgentHook.Translate("codex", $$"""{"session_id":"s9","cwd":"D:\\work\\api","hook_event_name":"{{evt}}"}""");
        var request = ActivityRequest.Parse(result.Request!, out _)!;
        Assert.Equal("codex-s9", request.Id);
        Assert.Equal("Codex · api", request.Title);
        Assert.Equal(state, request.State);
        // Codex adds whatever UserPromptSubmit prints to the conversation: it must stay silent.
        Assert.Equal(output, result.Output);
    }

    [Fact]
    public void Codex_interrupt_ends_it_with_an_empty_answer()
    {
        var result = AgentHook.Translate("codex", """{"session_id":"s9","hook_event_name":"Interrupt"}""");
        Assert.Equal(ActivityOp.End, ActivityRequest.Parse(result.Request!, out _)!.Op);
        Assert.Equal("{}", result.Output);
    }

    [Fact]
    public void Codex_notify_argument_means_done()
    {
        string notify = """{"type":"agent-turn-complete","thread-id":"t1","turn-id":"x","cwd":"D:\\work\\api","input-messages":["fix it"],"last-assistant-message":"Fixed."}""";
        Assert.Equal(notify, AgentHook.ReadPayload(new[] { notify }, TextReader.Null, stdinRedirected: false));
        var request = Request("codex", notify)!;
        Assert.Equal("codex-t1", request.Id);
        Assert.Equal(ActivityState.Done, request.State);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("""{"hook_event_name":"SomethingNew"}""")]
    public void Anything_unexpected_does_nothing(string payload)
    {
        var result = AgentHook.Translate("claude", payload);
        Assert.Null(result.Request);
        Assert.Null(result.Output);
    }

    [Fact]
    public void Working_lasts_an_hour_so_a_long_tool_doesnt_expire_midway()
    {
        Assert.Equal(AgentHook.AgentWorkingTtl, Request("claude", Claude("PostToolUse"))!.Ttl);
        Assert.Equal(TimeSpan.FromHours(1), AgentHook.AgentWorkingTtl);
        // Waiting on you and done have their own fixed times on the board.
        Assert.Null(Request("claude", Claude("PermissionRequest"))!.Ttl);
        Assert.Null(Request("claude", Claude("Stop"))!.Ttl);
    }

    [Fact]
    public void Odd_session_ids_still_make_a_valid_id()
    {
        var request = Request("claude", """{"session_id":"a b/c\\d<>","hook_event_name":"Stop"}""")!;
        Assert.Equal("claude-abcd", request.Id);
        var longOne = Request("claude", $$"""{"session_id":"{{new string('x', 100)}}","hook_event_name":"Stop"}""")!;
        Assert.Equal(64, longOne.Id.Length);
        Assert.Equal("Claude Code", Request("claude", """{"hook_event_name":"Stop"}""")!.Title);
    }

    [Theory]
    [InlineData(@"C:\src\yata", "yata")]
    [InlineData(@"C:\src\yata\", "yata")]
    [InlineData("/c:/src/site", "site")]
    [InlineData("", "")]
    public void Project_name_is_the_folder(string folder, string name) =>
        Assert.Equal(name, AgentHook.ProjectName(folder));

    [Fact]
    public void Hook_payload_comes_from_stdin()
    {
        string json = """{"hook_event_name":"Stop"}""";
        Assert.Equal(json, AgentHook.ReadPayload(Array.Empty<string>(), new StringReader(json), stdinRedirected: true));
        Assert.Null(AgentHook.ReadPayload(Array.Empty<string>(), new StringReader(json), stdinRedirected: false));
        // PowerShell puts a byte-order mark before what it pipes.
        Assert.Equal(json, AgentHook.ReadPayload(Array.Empty<string>(), new StringReader("﻿" + json), stdinRedirected: true));
    }

    [Fact]
    public void Setup_snippets_are_valid_json()
    {
        foreach (string agent in AgentHook.Agents)
        {
            string text = AgentHook.Setup(agent)!;
            string json = text[text.IndexOf('{')..(text.LastIndexOf('}') + 1)];
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.TryGetProperty("hooks", out _), agent);
        }
        Assert.Null(AgentHook.Setup("vim"));
    }

    [Fact]
    public void The_claude_code_plugin_registers_the_same_events_as_the_setup()
    {
        // From this source file, not the build output: builds can go to another folder (--artifacts-path).
        var dir = new FileInfo(ThisFile()).Directory;
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Yata.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        using var plugin = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "integrations", "claude-code", "hooks", "hooks.json")));
        string setup = AgentHook.Setup("claude")!;
        using var snippet = JsonDocument.Parse(setup[setup.IndexOf('{')..(setup.LastIndexOf('}') + 1)]);
        static IEnumerable<string> Events(JsonDocument d) =>
            d.RootElement.GetProperty("hooks").EnumerateObject().Select(p => p.Name).OrderBy(n => n);
        Assert.Equal(Events(snippet), Events(plugin));
    }

    private static string ThisFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    [Fact]
    public void An_unknown_phrase_is_refused()
    {
        Assert.Null(ActivityRequest.Parse("""{"op":"set","id":"x","title":"T","phrase":"sleeping"}""", out string? error));
        Assert.Contains("phrase", error);
    }
}
