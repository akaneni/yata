using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace Yata.Services;

/// <summary>
/// <c>Yata.exe activity ...</c>: turns the command line into the JSON request the running Yata
/// reads. Kept apart from the pipe so the parsing can be tested.
/// </summary>
internal static class ActivityCommand
{
    public const string Verb = "activity";

    public static string Usage =>
        """
        Usage:
          Yata.exe activity set <id> --title <text> [--text <text>] [--progress <0-1 or N%>] [--icon <name>] [--ttl <seconds>]
          Yata.exe activity attention <id> [same options]   needs you: shown in the warning color
          Yata.exe activity done <id> [--text <text>]       finished: shows a check, then goes away
          Yata.exe activity fail <id> [--text <text>]       failed: stays a minute
          Yata.exe activity end <id>                        remove it now
        Icons:
        """ + string.Join(", ", ActivityIcons.Names) + """

        Exit codes: 0 shown, 1 bad arguments, 2 Yata isn't running, 3 refused (the reason is printed).
        """;

    /// <summary>The JSON line for these arguments (without the leading "activity"), or an error.</summary>
    public static string? Build(IReadOnlyList<string> args, out string? error)
    {
        error = null;
        if (args.Count < 2)
            return Fail("missing the action and the id", out error);

        string? state = args[0].ToLowerInvariant() switch
        {
            "set" => "running",
            "attention" => "attention",
            "done" => "done",
            "fail" or "failed" => "failed",
            "end" => null,
            _ => "?",
        };
        if (state == "?")
            return Fail($"unknown action \"{args[0]}\"", out error);

        var request = new Dictionary<string, object>
        {
            ["op"] = state == null ? "end" : "set",
            ["id"] = args[1],
        };
        if (state != null)
            request["state"] = state;

        for (int i = 2; i < args.Count; i++)
        {
            string option = args[i];
            if (i + 1 >= args.Count)
                return Fail($"{option} needs a value", out error);
            string value = args[++i];
            switch (option)
            {
                case "--title":
                    request["title"] = value;
                    break;
                case "--text":
                    request["text"] = value;
                    break;
                case "--icon":
                    request["icon"] = value;
                    break;
                case "--progress":
                    if (ParseProgress(value) is not double progress)
                        return Fail("--progress takes 0 to 1 (0.4) or a percentage (40%)", out error);
                    request["progress"] = progress;
                    break;
                case "--ttl":
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds))
                        return Fail("--ttl takes whole seconds", out error);
                    request["ttl"] = seconds;
                    break;
                default:
                    return Fail($"unknown option {option}", out error);
            }
        }
        return JsonSerializer.Serialize(request);
    }

    internal static double? ParseProgress(string value)
    {
        bool percent = value.EndsWith('%');
        if (!double.TryParse(percent ? value[..^1] : value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            return null;
        if (percent)
            number /= 100;
        return number is >= 0 and <= 1 ? number : null;
    }

    private static string? Fail(string reason, out string? error)
    {
        error = reason;
        return null;
    }
}
