using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;

namespace Yata.Services;

/// <summary>
/// <c>yata-cli activity ...</c>: turns the command line into the JSON request the running Yata reads,
/// and sends it. yata-cli.exe compiles this file; no dependency on the rest of Yata.
/// </summary>
internal static class ActivityCommand
{
    public const string Verb = "activity";

    public static string Usage =>
        """
        Usage:
          yata-cli activity set <id> --title <text> [--text <text>] [--progress <0-1 or N%>] [--icon <name>] [--ttl <seconds>]
          yata-cli activity attention <id> [same options]   needs you: warning color, stays until ended (up to 1 h)
          yata-cli activity done <id> [same options]        finished: shows a check, then goes away
          yata-cli activity fail <id> [same options]        failed: warning color, stays until ended (up to 1 h)
          yata-cli activity end <id>                        remove it now (an id already gone is fine)
        Send --title every time: a running activity nobody updates expires (10 min, or --ttl), and
        an id that is gone needs a title to come back.
        Icons: 
        """ + string.Join(", ", ActivityIcons.Names) + """

        Exit codes: 0 shown, 1 bad arguments, 2 Yata isn't running, 3 refused (the reason is printed).
        """;

    /// <summary>
    /// Sends the activity in <paramref name="args"/> (after "activity") to the running Yata. Returns
    /// the exit code from <see cref="Usage"/>; what went wrong goes to <paramref name="error"/>.
    /// </summary>
    /// <param name="window">Where the command runs, so a click on the activity goes back there.</param>
    public static async Task<int> RunAsync(IReadOnlyList<string> args, Action<string> error, string? pipe = null,
        HostWindow? window = null)
    {
        if (args.Count == 0 || args[0] is "-h" or "--help" or "help")
        {
            error(Usage);
            return args.Count == 0 ? 1 : 0;
        }
        if (Build(args, out string? problem, window) is not string json)
        {
            error($"{problem}{Environment.NewLine}{Environment.NewLine}{Usage}");
            return 1;
        }
        switch (await ActivityClient.SendAsync(json, pipe))
        {
            case null:
                error("Yata isn't running.");
                return 2;
            case "ok":
                return 0;
            case var reply:
                error(reply);
                return 3;
        }
    }

    /// <summary>The JSON line for these arguments (without the leading "activity"), or an error.</summary>
    public static string? Build(IReadOnlyList<string> args, out string? error, HostWindow? window = null)
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
        {
            request["state"] = state;
            window?.WriteTo(request);
        }

        for (int i = 2; i < args.Count; i++)
        {
            string option = args[i];
            if (i + 1 >= args.Count)
                return Fail($"{option} needs a value", out error);
            string value = args[++i];
            switch (option)
            {
                case "--title":
                    // Only invisible or control characters: nothing would be left to show.
                    if (string.IsNullOrEmpty(ActivityRequest.Clean(value, ActivityRequest.MaxTitle)))
                        return Fail("--title has no visible characters", out error);
                    request["title"] = value;
                    break;
                case "--text":
                    request["text"] = value;
                    break;
                case "--icon":
                    if (!ActivityIcons.IsKnown(value))
                        return Fail($"unknown icon \"{value}\"; use one of: {string.Join(", ", ActivityIcons.Names)}", out error);
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
                    // Checked here too, so a wrong value is exit code 1 (fix the call), even with Yata closed.
                    if (seconds < ActivityRequest.MinTtl.TotalSeconds || seconds > ActivityRequest.MaxTtl.TotalSeconds)
                        return Fail($"--ttl takes {ActivityRequest.MinTtl.TotalSeconds:0} to {ActivityRequest.MaxTtl.TotalSeconds:0} seconds", out error);
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
