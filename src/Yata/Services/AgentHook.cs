using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Yata.Services;

/// <summary>
/// <c>yata-cli hook claude|cursor|codex</c>: turns an AI agent's lifecycle event (the JSON its hooks
/// send) into an activity in the notch — working, waiting for you, done. One activity per agent
/// session, titled with the agent and the project folder. Nothing from the conversation itself (the
/// prompt, the commands, the answers) is ever sent: only the state, and which window the agent runs
/// in (<see cref="HostWindow"/>), so a click on the activity goes there.
///
/// A hook must never get in the agent's way: whatever happens, the caller exits 0, and prints only
/// the neutral answer some events require (<see cref="Result.Output"/>).
/// </summary>
internal static class AgentHook
{
    public static readonly string[] Agents = { "claude", "cursor", "codex" };

    /// <summary>What a hook call does: the activity request to send (if any) and what to print.</summary>
    public sealed record Result(string? Request, string? Output);

    private enum Status { Working, Permission, Waiting, Done, Failed, Ended }

    private static readonly Regex Unsafe = new("[^A-Za-z0-9._:-]", RegexOptions.Compiled);

    /// <param name="window">The agent's terminal or editor, so a click on the activity goes there.</param>
    public static Result Translate(string agent, string? payload, HostWindow? window = null)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return new Result(null, null);
        }
        if (root.ValueKind != JsonValueKind.Object)
            return new Result(null, null);

        return agent switch
        {
            "claude" => Claude(root, window),
            "cursor" => Cursor(root, window),
            "codex" => Codex(root, window),
            _ => new Result(null, null),
        };
    }

    // https://code.claude.com/docs/en/hooks — exit 0 with nothing printed changes nothing.
    private static Result Claude(JsonElement e, HostWindow? window)
    {
        Status? status = Get(e, "hook_event_name") switch
        {
            "UserPromptSubmit" or "PostToolUse" or "PostToolUseFailure" => Status.Working,
            "PermissionRequest" => Status.Permission,
            "Notification" => Get(e, "notification_type") switch
            {
                "permission_prompt" => Status.Permission,
                "idle_prompt" => Status.Waiting,
                _ => null,
            },
            "Stop" => Status.Done,
            "StopFailure" => Status.Failed,
            "SessionEnd" => Status.Ended,
            _ => null,
        };
        return new Result(Request("claude", "Claude Code", Get(e, "session_id"), Get(e, "cwd"), status, window), null);
    }

    // https://cursor.com/docs/agent/hooks — only fail-open events are used; the ones that decide
    // permissions (preToolUse, beforeShellExecution...) are left alone on purpose.
    private static Result Cursor(JsonElement e, HostWindow? window)
    {
        string? name = Get(e, "hook_event_name");
        Status? status = name switch
        {
            "beforeSubmitPrompt" or "postToolUse" or "postToolUseFailure" or "afterShellExecution"
                or "afterMCPExecution" or "afterFileEdit" => Status.Working,
            "stop" => Get(e, "status") switch
            {
                "completed" => Status.Done,
                "error" => Status.Failed,
                _ => Status.Ended, // aborted: the user stopped it and is looking at it already.
            },
            "sessionEnd" => Status.Ended,
            _ => null,
        };
        string? folder = e.TryGetProperty("workspace_roots", out var roots) && roots.ValueKind == JsonValueKind.Array
            ? roots.EnumerateArray().Select(r => r.ValueKind == JsonValueKind.String ? r.GetString() : null).FirstOrDefault()
            : null;
        // Answers that keep the agent going as if no hook were there.
        string? output = name switch
        {
            "beforeSubmitPrompt" => "{\"continue\":true}",
            "stop" => "{}",
            _ => null,
        };
        return new Result(Request("cursor", "Cursor", Get(e, "conversation_id"), folder, status, window), output);
    }

    // https://learn.chatgpt.com/docs/hooks (stdin), and the older `notify` setting, which passes
    // one JSON argument with "type": "agent-turn-complete".
    private static Result Codex(JsonElement e, HostWindow? window)
    {
        if (Get(e, "type") == "agent-turn-complete")
            return new Result(Request("codex", "Codex", Get(e, "thread-id"), Get(e, "cwd"), Status.Done, window), null);

        string? name = Get(e, "hook_event_name");
        Status? status = name switch
        {
            "UserPromptSubmit" or "PostToolUse" => Status.Working,
            "PermissionRequest" => Status.Permission,
            "Stop" => Status.Done,
            "Interrupt" or "SessionEnd" => Status.Ended,
            _ => null,
        };
        // Stop and Interrupt expect a JSON answer; an empty object asks for nothing.
        string? output = name is "Stop" or "Interrupt" ? "{}" : null;
        return new Result(Request("codex", "Codex", Get(e, "session_id"), Get(e, "cwd"), status, window), output);
    }

    private static string? Request(string agent, string product, string? session, string? folder, Status? status,
        HostWindow? window)
    {
        if (status == null)
            return null;
        string id = agent + "-" + Unsafe.Replace(session ?? "session", "");
        if (id.Length > 64)
            id = id[..64];
        if (status == Status.Ended)
            return JsonSerializer.Serialize(new Dictionary<string, object> { ["op"] = "end", ["id"] = id });

        string project = ProjectName(folder);
        var request = new Dictionary<string, object>
        {
            ["op"] = "set",
            ["id"] = id,
            ["title"] = project.Length > 0 ? $"{product} · {project}" : product,
            ["icon"] = "agent",
            ["state"] = status switch
            {
                Status.Permission or Status.Waiting => "attention",
                Status.Done => "done",
                Status.Failed => "failed",
                _ => "running",
            },
            // Yata words it in its own language (see ActivityRequest.Phrase).
            ["phrase"] = status switch
            {
                Status.Permission => "permission",
                Status.Waiting => "waiting",
                Status.Done => "done",
                Status.Failed => "failed",
                _ => "working",
            },
        };
        window?.WriteTo(request);
        return JsonSerializer.Serialize(request);
    }

    /// <summary>The project folder's name: "C:\src\yata\" → "yata".</summary>
    internal static string ProjectName(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return "";
        string trimmed = folder.TrimEnd('\\', '/');
        int cut = trimmed.LastIndexOfAny(new[] { '\\', '/' });
        return cut >= 0 ? trimmed[(cut + 1)..] : trimmed;
    }

    private static string? Get(JsonElement e, string name) =>
        e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>The configuration that registers <c>yata-cli hook</c> with an agent, to paste.</summary>
    public static string? Setup(string agent) => agent switch
    {
        "claude" => """
            Add to %USERPROFILE%\.claude\settings.json (merge with what's there), or install the plugin instead:
            /plugin marketplace add akaneni/yata, then /plugin install yata@yata
            {
              "hooks": {
                "UserPromptSubmit":  [{ "hooks": [{ "type": "command", "command": "yata-cli hook claude", "timeout": 5 }] }],
                "PostToolUse":       [{ "hooks": [{ "type": "command", "command": "yata-cli hook claude", "timeout": 5 }] }],
                "PostToolUseFailure": [{ "hooks": [{ "type": "command", "command": "yata-cli hook claude", "timeout": 5 }] }],
                "PermissionRequest": [{ "hooks": [{ "type": "command", "command": "yata-cli hook claude", "timeout": 5 }] }],
                "Notification":      [{ "hooks": [{ "type": "command", "command": "yata-cli hook claude", "timeout": 5 }] }],
                "Stop":              [{ "hooks": [{ "type": "command", "command": "yata-cli hook claude", "timeout": 5 }] }],
                "StopFailure":       [{ "hooks": [{ "type": "command", "command": "yata-cli hook claude", "timeout": 5 }] }],
                "SessionEnd":        [{ "hooks": [{ "type": "command", "command": "yata-cli hook claude", "timeout": 1 }] }]
              }
            }
            """,
        "cursor" => """
            Save as %USERPROFILE%\.cursor\hooks.json (merge with what's there):
            {
              "version": 1,
              "hooks": {
                "beforeSubmitPrompt":  [{ "command": "yata-cli hook cursor", "timeout": 5 }],
                "postToolUse":         [{ "command": "yata-cli hook cursor", "timeout": 5 }],
                "afterShellExecution": [{ "command": "yata-cli hook cursor", "timeout": 5 }],
                "afterFileEdit":       [{ "command": "yata-cli hook cursor", "timeout": 5 }],
                "stop":                [{ "command": "yata-cli hook cursor", "timeout": 5 }],
                "sessionEnd":          [{ "command": "yata-cli hook cursor", "timeout": 5 }]
              }
            }
            Cursor has no event for "waiting for your approval", so that state isn't shown.
            """,
        "codex" => """
            Save as %USERPROFILE%\.codex\hooks.json (merge with what's there), then review and trust it with /hooks:
            {
              "hooks": {
                "UserPromptSubmit":  [{ "hooks": [{ "type": "command", "command": "yata-cli hook codex", "timeout": 5 }] }],
                "PostToolUse":       [{ "hooks": [{ "type": "command", "command": "yata-cli hook codex", "timeout": 5 }] }],
                "PermissionRequest": [{ "hooks": [{ "type": "command", "command": "yata-cli hook codex", "timeout": 5 }] }],
                "Stop":              [{ "hooks": [{ "type": "command", "command": "yata-cli hook codex", "timeout": 5 }] }],
                "Interrupt":         [{ "hooks": [{ "type": "command", "command": "yata-cli hook codex", "timeout": 2 }] }],
                "SessionEnd":        [{ "hooks": [{ "type": "command", "command": "yata-cli hook codex", "timeout": 2 }] }]
              }
            }
            Older Codex without hooks: put notify = ["yata-cli", "hook", "codex"] at the top of
            %USERPROFILE%\.codex\config.toml (only "done" is shown then).
            """,
        _ => null,
    };

    /// <summary>Reads what the agent sent: stdin for hooks, or a JSON argument (Codex's notify).</summary>
    public static string? ReadPayload(IReadOnlyList<string> extra, TextReader stdin, bool stdinRedirected)
    {
        if (extra.Count > 0 && extra[^1].TrimStart().StartsWith('{'))
            return extra[^1];
        if (!stdinRedirected)
            return null;
        // Agents send a small object; anything huge (a transcript by mistake) is cut off.
        var buffer = new char[256 * 1024];
        int length = stdin.ReadBlock(buffer, 0, buffer.Length);
        return new string(buffer, 0, length).TrimStart('﻿');
    }
}
