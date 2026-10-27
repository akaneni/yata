using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Yata.Services;

public enum AgentStatus
{
    /// <summary>The agent isn't on this PC (no config folder).</summary>
    NotFound,
    Off,
    On,
    /// <summary>Claude Code has the Yata plugin, which brings its own hooks.</summary>
    OnByPlugin,
}

/// <summary>Something in an agent's config file Yata won't touch (unreadable JSON, say).</summary>
public sealed class AgentSetupException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Settings → AI agents: writes the hooks that call <c>yata-cli hook</c> into each agent's own
/// config file, and takes them out again. Only when the user flips the switch; next to whatever the
/// user has there, which stays as it was; a copy of the file is kept before each change
/// (<c>.yata-backup</c>). Turning off removes only the entries Yata added.
/// </summary>
internal sealed class AgentSetup
{
    public const string SkillName = "yata-notch";

    // Same events as `yata-cli hook setup` and the Claude Code plugin (AgentSetupTests checks).
    internal static readonly (string Event, int Timeout)[] ClaudeEvents =
    {
        ("UserPromptSubmit", 5), ("PostToolUse", 5), ("PostToolUseFailure", 5), ("PermissionRequest", 5),
        ("Notification", 5), ("Stop", 5), ("StopFailure", 5), ("SessionEnd", 1),
    };
    internal static readonly (string Event, int Timeout)[] CursorEvents =
    {
        ("beforeSubmitPrompt", 5), ("postToolUse", 5), ("afterShellExecution", 5), ("afterFileEdit", 5),
        ("stop", 5), ("sessionEnd", 5),
    };
    internal static readonly (string Event, int Timeout)[] CodexEvents =
    {
        ("UserPromptSubmit", 5), ("PostToolUse", 5), ("PermissionRequest", 5), ("Stop", 5), ("Interrupt", 2),
        ("SessionEnd", 2),
    };

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly string _home;
    private readonly string _cli;
    private readonly string _skill;

    /// <param name="home">The user's profile folder (where .claude, .cursor and .codex live).</param>
    /// <param name="cli">Full path of yata-cli.exe, written into the hooks.</param>
    /// <param name="skill">The SKILL.md text, copied where the agents look for skills.</param>
    public AgentSetup(string home, string cli, string skill)
    {
        _home = home;
        _cli = cli;
        _skill = skill;
    }

    /// <summary>Installed next to Yata.exe; missing in development builds.</summary>
    public static string InstalledCli => Path.Combine(AppContext.BaseDirectory, "yata-cli.exe");

    /// <summary>The skill Yata ships (integrations/claude-code/skills/yata-notch/SKILL.md).</summary>
    public static string EmbeddedSkill()
    {
        using var stream = typeof(AgentSetup).Assembly.GetManifestResourceStream("SKILL.md")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The agents of the user running Yata, with the installed yata-cli.</summary>
    public static AgentSetup ForThisUser() =>
        new(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), InstalledCli, EmbeddedSkill());

    /// <summary>
    /// Uninstalling (Yata.exe --remove-agent-hooks): takes Yata's hooks and skill out of the agents
    /// on Windows, so none is left pointing at a yata-cli.exe that's gone. WSL distros keep theirs:
    /// reaching them means starting each one, too much for an uninstaller.
    /// </summary>
    public static void RemoveAll()
    {
        var setup = ForThisUser();
        foreach (string agent in AgentHook.Agents)
        {
            try
            {
                if (setup.Status(agent) == AgentStatus.On)
                    setup.Set(agent, false);
            }
            catch (Exception ex) when (ex is AgentSetupException or IOException or UnauthorizedAccessException)
            {
                // Uninstalling goes on; the file stays as it was.
            }
        }
    }

    private string ClaudeDir => Path.Combine(_home, ".claude");
    private string ClaudeFile => Path.Combine(ClaudeDir, "settings.json");
    private string CursorDir => Path.Combine(_home, ".cursor");
    private string CursorFile => Path.Combine(CursorDir, "hooks.json");
    private string CodexDir => Path.Combine(_home, ".codex");
    private string CodexFile => Path.Combine(CodexDir, "hooks.json");

    public AgentStatus Status(string agent)
    {
        try
        {
            return agent switch
            {
                "claude" => !Directory.Exists(ClaudeDir) ? AgentStatus.NotFound
                    : HasPlugin(Read(ClaudeFile)) ? AgentStatus.OnByPlugin
                    : HasOurs(Read(ClaudeFile), agent, grouped: true) ? AgentStatus.On : AgentStatus.Off,
                "cursor" => !Directory.Exists(CursorDir) ? AgentStatus.NotFound
                    : HasOurs(Read(CursorFile), agent, grouped: false) ? AgentStatus.On : AgentStatus.Off,
                "codex" => !Directory.Exists(CodexDir) ? AgentStatus.NotFound
                    : HasOurs(Read(CodexFile), agent, grouped: true) ? AgentStatus.On : AgentStatus.Off,
                _ => AgentStatus.NotFound,
            };
        }
        catch (AgentSetupException)
        {
            return AgentStatus.Off; // Shown as off; turning it on reports the problem.
        }
    }

    /// <summary>Turns one agent's hooks on or off. Throws <see cref="AgentSetupException"/> if its file can't be read.</summary>
    public void Set(string agent, bool on)
    {
        switch (agent)
        {
            case "claude":
                // Exec form: Claude Code spawns the .exe itself, no shell, so no PATH or quoting involved.
                Update(ClaudeFile, root => SetGrouped(root, agent, ClaudeEvents, on, ClaudeHandler));
                break;
            case "cursor":
                // Cursor runs hook commands through PowerShell on Windows.
                Update(CursorFile, root =>
                {
                    root["version"] ??= 1;
                    SetFlat(root, agent, CursorEvents, on);
                });
                break;
            case "codex":
                // Codex doesn't say which shell runs hooks on Windows; a bare name on PATH works in all of them.
                Update(CodexFile, root => SetGrouped(root, agent, CodexEvents, on, CodexHandler));
                break;
            default:
                throw new ArgumentException($"Unknown agent {agent}", nameof(agent));
        }
        SyncSkills();
    }

    /// <summary>
    /// Claude Code reads ~/.claude/skills (its plugin brings the skill along); Codex reads
    /// ~/.agents/skills; Cursor reads both. Each copy stays while an agent that uses it is on.
    /// </summary>
    private void SyncSkills()
    {
        var claude = Status("claude");
        SetSkill(Path.Combine(_home, ".claude", "skills", SkillName), claude == AgentStatus.On);
        SetSkill(Path.Combine(_home, ".agents", "skills", SkillName),
            Status("cursor") == AgentStatus.On || Status("codex") == AgentStatus.On);
    }

    private void SetSkill(string dir, bool on)
    {
        string file = Path.Combine(dir, "SKILL.md");
        if (on)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(file, _skill, new UTF8Encoding(false));
        }
        else if (File.Exists(file))
        {
            File.Delete(file);
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
        }
    }

    // ---- The files ------------------------------------------------------------------------

    private static JsonObject Read(string file)
    {
        if (!File.Exists(file))
            return new JsonObject();
        string text = File.ReadAllText(file);
        if (string.IsNullOrWhiteSpace(text))
            return new JsonObject();
        try
        {
            return JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }) as JsonObject ?? throw new AgentSetupException(string.Format(Strings.Agents_NotAnObject, file));
        }
        catch (JsonException ex)
        {
            throw new AgentSetupException(string.Format(Strings.Agents_Unreadable, file), ex);
        }
    }

    private static void Update(string file, Action<JsonObject> change)
    {
        var root = Read(file);
        string before = root.ToJsonString();
        change(root);
        if (root.ToJsonString() == before)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        if (File.Exists(file))
            File.Copy(file, file + ".yata-backup", overwrite: true);
        // Written next to it, then moved over: a crash midway can't leave half a file.
        string temp = file + ".yata-tmp";
        File.WriteAllText(temp, root.ToJsonString(Indented), new UTF8Encoding(false));
        File.Move(temp, file, overwrite: true);
    }

    private static bool HasPlugin(JsonObject root) =>
        root["enabledPlugins"] is JsonObject plugins && plugins["yata@yata"] is JsonValue value &&
        value.TryGetValue(out bool enabled) && enabled;

    // ---- Claude Code and Codex: event → [ { matcher?, hooks: [handler] } ] -----------------

    private JsonObject ClaudeHandler(int timeout) => new()
    {
        ["type"] = "command",
        ["command"] = _cli,
        ["args"] = new JsonArray("hook", "claude"),
        ["timeout"] = timeout,
    };

    private static JsonObject CodexHandler(int timeout) => new()
    {
        ["type"] = "command",
        ["command"] = "yata-cli hook codex",
        ["timeout"] = timeout,
    };

    private static void SetGrouped(JsonObject root, string agent, (string Event, int Timeout)[] events, bool on,
        Func<int, JsonObject> handler)
    {
        // Out first, then back in: turning on again also fixes an old path.
        if (root["hooks"] is JsonObject hooks)
        {
            foreach (var (name, list) in hooks.ToList())
            {
                if (list is not JsonArray groups)
                    continue;
                foreach (var group in groups.OfType<JsonObject>().ToList())
                {
                    if (group["hooks"] is not JsonArray handlers)
                        continue;
                    foreach (var h in handlers.OfType<JsonObject>().Where(h => IsOurs(h, agent)).ToList())
                        handlers.Remove(h);
                    if (handlers.Count == 0)
                        groups.Remove(group);
                }
                if (groups.Count == 0)
                    hooks.Remove(name);
            }
            if (hooks.Count == 0 && !on)
                root.Remove("hooks");
        }
        if (!on)
            return;

        var target = root["hooks"] as JsonObject ?? new JsonObject();
        root["hooks"] = target;
        foreach (var (name, timeout) in events)
        {
            var groups = target[name] as JsonArray ?? new JsonArray();
            target[name] = groups;
            groups.Add(new JsonObject { ["hooks"] = new JsonArray(handler(timeout)) });
        }
    }

    // ---- Cursor: event → [handler] -------------------------------------------------------

    private void SetFlat(JsonObject root, string agent, (string Event, int Timeout)[] events, bool on)
    {
        if (root["hooks"] is JsonObject hooks)
        {
            foreach (var (name, list) in hooks.ToList())
            {
                if (list is not JsonArray handlers)
                    continue;
                foreach (var h in handlers.OfType<JsonObject>().Where(h => IsOurs(h, agent)).ToList())
                    handlers.Remove(h);
                if (handlers.Count == 0)
                    hooks.Remove(name);
            }
        }
        if (!on)
            return;

        var target = root["hooks"] as JsonObject ?? new JsonObject();
        root["hooks"] = target;
        // PowerShell runs it: & and single quotes take any path, spaces included.
        string command = $"& '{_cli.Replace("'", "''")}' hook cursor";
        foreach (var (name, timeout) in events)
        {
            var handlers = target[name] as JsonArray ?? new JsonArray();
            target[name] = handlers;
            handlers.Add(new JsonObject { ["command"] = command, ["timeout"] = timeout });
        }
    }

    // ---- Recognizing Yata's own entries ----------------------------------------------------

    private static bool HasOurs(JsonObject root, string agent, bool grouped) =>
        root["hooks"] is JsonObject hooks && hooks.Any(e => e.Value is JsonArray list &&
            (grouped
                ? list.OfType<JsonObject>().Any(g => g["hooks"] is JsonArray hs && hs.OfType<JsonObject>().Any(h => IsOurs(h, agent)))
                : list.OfType<JsonObject>().Any(h => IsOurs(h, agent))));

    /// <summary>A handler that runs yata-cli for this agent: "yata-cli hook codex", the exec form, or the PowerShell one.</summary>
    internal static bool IsOurs(JsonObject handler, string agent)
    {
        string command = handler["command"] is JsonValue c && c.TryGetValue(out string? text) ? text : "";
        if (!command.Contains("yata-cli", StringComparison.OrdinalIgnoreCase))
            return false;
        if (handler["args"] is JsonArray args)
            return args.Count >= 2 && args[0]?.ToString() == "hook" && args[1]?.ToString() == agent;
        return command.EndsWith("hook " + agent, StringComparison.Ordinal);
    }
}
