using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Yata.Services;

namespace Yata.Tests;

/// <summary>Settings → AI agents: hooks written into each agent's own config, and taken out again.</summary>
public sealed class AgentSetupTests : IDisposable
{
    private const string Cli = @"C:\Users\Ana Maria\AppData\Local\Programs\Yata\yata-cli.exe";
    private readonly string _home = Path.Combine(Path.GetTempPath(), "yata-tests", "home-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_home, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private AgentSetup Setup() => new(_home, Cli, "---\nname: yata-notch\n---\nskill");

    private string File_(string relative) => Path.Combine(_home, relative);

    private JsonObject Json(string relative) => JsonNode.Parse(File.ReadAllText(File_(relative)))!.AsObject();

    private void Write(string relative, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File_(relative))!);
        File.WriteAllText(File_(relative), text);
    }

    [Fact]
    public void An_agent_without_its_folder_is_not_found()
    {
        Assert.Equal(AgentStatus.NotFound, Setup().Status("claude"));
        Directory.CreateDirectory(File_(".claude"));
        Assert.Equal(AgentStatus.Off, Setup().Status("claude"));
    }

    [Fact]
    public void Claude_code_gets_exec_form_hooks_with_the_full_path()
    {
        Directory.CreateDirectory(File_(".claude"));
        Setup().Set("claude", true);

        Assert.Equal(AgentStatus.On, Setup().Status("claude"));
        var hooks = Json(@".claude\settings.json")["hooks"]!.AsObject();
        Assert.Equal(AgentSetup.ClaudeEvents.Select(e => e.Event).OrderBy(e => e), hooks.Select(h => h.Key).OrderBy(e => e));
        var handler = hooks["Stop"]![0]!["hooks"]![0]!.AsObject();
        Assert.Equal(Cli, handler["command"]!.GetValue<string>()); // No shell: spaces in the path are fine.
        Assert.Equal(new[] { "hook", "claude" }, handler["args"]!.AsArray().Select(a => a!.GetValue<string>()));
    }

    [Fact]
    public void What_the_user_had_stays_and_turning_off_removes_only_yatas()
    {
        Write(@".claude\settings.json", """
            {
              "model": "opus",
              "hooks": {
                "Stop": [{ "hooks": [{ "type": "command", "command": "say done" }] }]
              }
            }
            """);
        Setup().Set("claude", true);
        var on = Json(@".claude\settings.json");
        Assert.Equal("opus", on["model"]!.GetValue<string>());
        Assert.Equal(2, on["hooks"]!["Stop"]!.AsArray().Count);
        Assert.True(File.Exists(File_(@".claude\settings.json.yata-backup")));

        Setup().Set("claude", false);
        var off = Json(@".claude\settings.json");
        var stop = Assert.Single(off["hooks"]!.AsObject());
        Assert.Equal("Stop", stop.Key);
        Assert.Equal("say done", stop.Value![0]!["hooks"]![0]!["command"]!.GetValue<string>());
        Assert.Equal(AgentStatus.Off, Setup().Status("claude"));
    }

    [Fact]
    public void Turning_on_twice_doesnt_duplicate_and_updates_the_path()
    {
        Directory.CreateDirectory(File_(".claude"));
        new AgentSetup(_home, @"C:\old\yata-cli.exe", "s").Set("claude", true);
        Setup().Set("claude", true);
        var stop = Json(@".claude\settings.json")["hooks"]!["Stop"]!.AsArray();
        var group = Assert.Single(stop);
        Assert.Equal(Cli, group!["hooks"]![0]!["command"]!.GetValue<string>());
    }

    [Fact]
    public void Turning_off_with_nothing_else_leaves_no_empty_hooks()
    {
        Write(@".claude\settings.json", """{ "model": "opus" }""");
        Setup().Set("claude", true);
        Setup().Set("claude", false);
        Assert.Null(Json(@".claude\settings.json")["hooks"]);
    }

    [Fact]
    public void The_plugin_counts_as_on_and_is_left_alone()
    {
        Write(@".claude\settings.json", """{ "enabledPlugins": { "yata@yata": true } }""");
        Assert.Equal(AgentStatus.OnByPlugin, Setup().Status("claude"));
    }

    [Fact]
    public void An_unreadable_file_is_not_touched()
    {
        Write(@".claude\settings.json", "{ this is not json");
        var error = Assert.Throws<AgentSetupException>(() => Setup().Set("claude", true));
        Assert.Contains("settings.json", error.Message);
        Assert.Equal("{ this is not json", File.ReadAllText(File_(@".claude\settings.json")));
    }

    [Fact]
    public void Comments_and_trailing_commas_dont_stop_it()
    {
        Write(@".claude\settings.json", "{ // mine\n \"model\": \"opus\", }");
        Setup().Set("claude", true);
        Assert.Equal(AgentStatus.On, Setup().Status("claude"));
    }

    [Fact]
    public void Cursor_gets_a_powershell_command_that_takes_any_path()
    {
        Directory.CreateDirectory(File_(".cursor"));
        new AgentSetup(_home, @"C:\Users\O'Brien\yata-cli.exe", "s").Set("cursor", true);
        var root = Json(@".cursor\hooks.json");
        Assert.Equal(1, root["version"]!.GetValue<int>());
        Assert.Equal(AgentSetup.CursorEvents.Length, root["hooks"]!.AsObject().Count);
        Assert.Equal(@"& 'C:\Users\O''Brien\yata-cli.exe' hook cursor", root["hooks"]!["stop"]![0]!["command"]!.GetValue<string>());
        Assert.Equal(AgentStatus.On, Setup().Status("cursor"));
    }

    [Fact]
    public void Codex_calls_yata_cli_by_name()
    {
        Directory.CreateDirectory(File_(".codex"));
        Setup().Set("codex", true);
        var handler = Json(@".codex\hooks.json")["hooks"]!["Stop"]![0]!["hooks"]![0]!;
        Assert.Equal("yata-cli hook codex", handler["command"]!.GetValue<string>());
        Assert.Equal(AgentStatus.On, Setup().Status("codex"));
    }

    [Fact]
    public void The_skill_goes_where_each_agent_looks_while_it_is_on()
    {
        Directory.CreateDirectory(File_(".claude"));
        Directory.CreateDirectory(File_(".codex"));
        Setup().Set("claude", true);
        Setup().Set("codex", true);
        Assert.True(File.Exists(File_(@".claude\skills\yata-notch\SKILL.md")));
        Assert.True(File.Exists(File_(@".agents\skills\yata-notch\SKILL.md")));

        Setup().Set("codex", false);
        Assert.False(Directory.Exists(File_(@".agents\skills\yata-notch")));
        Assert.True(File.Exists(File_(@".claude\skills\yata-notch\SKILL.md")));
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("cursor")]
    [InlineData("codex")]
    public void The_same_events_as_yata_cli_hook_setup(string agent)
    {
        string text = AgentHook.Setup(agent)!;
        using var snippet = JsonDocument.Parse(text[text.IndexOf('{')..(text.LastIndexOf('}') + 1)]);
        var events = agent switch
        {
            "claude" => AgentSetup.ClaudeEvents,
            "cursor" => AgentSetup.CursorEvents,
            _ => AgentSetup.CodexEvents,
        };
        Assert.Equal(snippet.RootElement.GetProperty("hooks").EnumerateObject().Select(p => p.Name).OrderBy(n => n),
            events.Select(e => e.Event).OrderBy(n => n));
    }

    [Fact]
    public void Someone_elses_hooks_are_never_taken_for_yatas()
    {
        Assert.False(AgentSetup.IsOurs(JsonNode.Parse("""{ "command": "notify-send done" }""")!.AsObject(), "claude"));
        Assert.False(AgentSetup.IsOurs(JsonNode.Parse("""{ "command": "yata-cli hook codex" }""")!.AsObject(), "claude"));
        Assert.True(AgentSetup.IsOurs(JsonNode.Parse("""{ "command": "yata-cli hook claude" }""")!.AsObject(), "claude"));
    }
}
