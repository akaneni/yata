using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Yata.Services;

public enum ActivityState { Running, Attention, Done, Failed }

/// <summary>
/// Something another program on this PC is doing (a build, a download, an AI agent at work), shown
/// in the bubble beside the closed notch. Sent with <c>yata-cli activity</c>; kept in memory only.
/// </summary>
public sealed record Activity(
    string Id,
    string Title,
    string? Text,
    double? Progress,
    ActivityState State,
    string Icon,
    DateTime Updated,
    DateTime Expires,
    HostWindow? Window = null)
{
    public string Glyph => State switch
    {
        ActivityState.Done => ActivityIcons.Done,
        ActivityState.Failed => ActivityIcons.Failed,
        _ => ActivityIcons.Glyph(Icon),
    };

    /// <summary>Needs the user: drawn in the warning color and announced as a reminder.</summary>
    public bool Warning => State is ActivityState.Attention or ActivityState.Failed;

    /// <summary>"Build · compiling" — what the notice and the bubble's hover say.</summary>
    public string Line => string.IsNullOrEmpty(Text) ? Title : $"{Title} · {Text}";
}

/// <summary>The icons a program may pick. A fixed set: no arbitrary glyphs or images from outside.</summary>
internal static class ActivityIcons
{
    public const string Done = "";
    public const string Failed = "";

    private static readonly Dictionary<string, string> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["activity"] = "",
        ["code"] = "",
        ["build"] = "",
        ["download"] = "",
        ["upload"] = "",
        ["sync"] = "",
        ["agent"] = "",
        ["bell"] = "",
        ["timer"] = "",
    };

    public static IEnumerable<string> Names => ByName.Keys;

    public static bool IsKnown(string name) => ByName.ContainsKey(name);

    public static string Glyph(string name) => ByName.TryGetValue(name, out var glyph) ? glyph : ByName["activity"];
}

internal enum ActivityOp { Set, End }

/// <summary>One request from another program, already checked and cleaned.</summary>
internal sealed record ActivityRequest(
    ActivityOp Op,
    string Id,
    string? Title = null,
    string? Text = null,
    double? Progress = null,
    ActivityState State = ActivityState.Running,
    string? Icon = null,
    TimeSpan? Ttl = null,
    string? Phrase = null,
    HostWindow? Window = null)
{
    /// <summary>
    /// Stock texts Yata words in its own language, for callers that don't know it (the agent
    /// hooks): used when no text is given.
    /// </summary>
    public static readonly string[] Phrases = { "working", "permission", "waiting", "done", "failed" };

    public const int MaxBytes = 4096;
    public const int MaxTitle = 60;
    public const int MaxText = 120;
    public static readonly TimeSpan MinTtl = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaxTtl = TimeSpan.FromHours(24);

    private static readonly Regex IdPattern = new("^[A-Za-z0-9._:-]{1,64}$", RegexOptions.Compiled);

    /// <summary>
    /// Reads one JSON request: <c>{"op":"set","id":"build","title":"Build","progress":0.4}</c>.
    /// Anything unexpected is refused with a reason, rather than half-applied.
    /// </summary>
    public static ActivityRequest? Parse(string json, out string? error)
    {
        error = null;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Fail("expected a JSON object", out error);

            var op = String(root, "op") switch
            {
                "set" => ActivityOp.Set,
                "end" => ActivityOp.End,
                _ => (ActivityOp?)null,
            };
            if (op == null)
                return Fail("op must be \"set\" or \"end\"", out error);

            string? id = String(root, "id");
            if (id == null || !IdPattern.IsMatch(id))
                return Fail("id must be 1-64 letters, digits or . _ : -", out error);
            if (op == ActivityOp.End)
                return new ActivityRequest(ActivityOp.End, id);

            var state = String(root, "state") switch
            {
                null or "running" => ActivityState.Running,
                "attention" => ActivityState.Attention,
                "done" => ActivityState.Done,
                "failed" => ActivityState.Failed,
                _ => (ActivityState?)null,
            };
            if (state == null)
                return Fail("state must be running, attention, done or failed", out error);

            string? icon = String(root, "icon");
            if (icon != null && !ActivityIcons.IsKnown(icon))
                return Fail("unknown icon; use one of: " + string.Join(", ", ActivityIcons.Names), out error);

            double? progress = null;
            if (root.TryGetProperty("progress", out var p) && p.ValueKind != JsonValueKind.Null)
            {
                if (p.ValueKind != JsonValueKind.Number || !p.TryGetDouble(out double value) || double.IsNaN(value) ||
                    value < 0 || value > 1)
                    return Fail("progress must be a number from 0 to 1", out error);
                progress = value;
            }

            TimeSpan? ttl = null;
            if (root.TryGetProperty("ttl", out var t) && t.ValueKind != JsonValueKind.Null)
            {
                if (t.ValueKind != JsonValueKind.Number || !t.TryGetInt32(out int seconds) ||
                    seconds < MinTtl.TotalSeconds || seconds > MaxTtl.TotalSeconds)
                    return Fail($"ttl must be {MinTtl.TotalSeconds:0} to {MaxTtl.TotalSeconds:0} seconds", out error);
                ttl = TimeSpan.FromSeconds(seconds);
            }

            string? phrase = String(root, "phrase");
            if (phrase != null && Array.IndexOf(Phrases, phrase) < 0)
                return Fail("phrase must be one of: " + string.Join(", ", Phrases), out error);

            HostWindow? window = null;
            if (root.TryGetProperty("window", out var w) && w.ValueKind != JsonValueKind.Null &&
                (window = HostWindow.Read(w)) == null)
                return Fail("window must be {\"handle\": number, \"pid\": number}", out error);

            return new ActivityRequest(ActivityOp.Set, id, Clean(String(root, "title"), MaxTitle),
                Clean(String(root, "text"), MaxText), progress, state.Value, icon, ttl, phrase, window);
        }
        catch (JsonException)
        {
            return Fail("not valid JSON", out error);
        }
        catch (InvalidOperationException)
        {
            return Fail("a field has the wrong type", out error);
        }
    }

    private static ActivityRequest? Fail(string reason, out string? error)
    {
        error = reason;
        return null;
    }

    private static string? String(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;

    /// <summary>
    /// Plain one-line text: no control characters, no direction overrides or invisible characters
    /// (which could make a line read as something else), spaces collapsed, cut to a length.
    /// </summary>
    internal static string? Clean(string? text, int max)
    {
        if (text == null)
            return null;
        var sb = new StringBuilder(text.Length);
        bool space = false;
        foreach (char c in text)
        {
            var category = char.GetUnicodeCategory(c);
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                space = sb.Length > 0;
                continue;
            }
            if (category is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                or UnicodeCategory.PrivateUse)
                continue;
            if (space)
                sb.Append(' ');
            space = false;
            sb.Append(c);
        }
        if (sb.Length > max)
        {
            int cut = char.IsHighSurrogate(sb[max - 2]) ? max - 2 : max - 1;
            sb.Length = cut;
            sb.Append('…');
        }
        return sb.ToString();
    }
}

/// <summary>What a request did, so the notch knows whether it's worth a notice.</summary>
internal enum ActivityChange { Added, Updated, StateChanged, Ended, Unknown, Full, NeedsTitle }

/// <summary>The activities other programs have put up. Lives in memory; nothing is saved.</summary>
internal sealed class ActivityBoard
{
    public const int MaxActivities = 8;
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);
    // Finished work stays a moment so it can be seen, then gets out of the way.
    public static readonly TimeSpan DoneTtl = TimeSpan.FromSeconds(12);
    // What needs the user (an agent waiting for a permission, a failed build) stays until it's ended,
    // however long the user takes to come back, within reason.
    public static readonly TimeSpan WarningTtl = TimeSpan.FromHours(1);

    private readonly List<Activity> _items = new();

    /// <summary>Most pressing first: needing the user, then running (latest first), then finished.</summary>
    public IReadOnlyList<Activity> Items => _items
        .OrderBy(a => a.Warning ? 0 : a.State == ActivityState.Done ? 2 : 1)
        .ThenByDescending(a => a.Updated)
        .ToList();

    public int Count => _items.Count;

    public DateTime? NextExpiry => _items.Count == 0 ? null : _items.Min(a => a.Expires);

    public (ActivityChange Change, Activity? Activity) Apply(ActivityRequest request, DateTime now)
    {
        int index = _items.FindIndex(a => a.Id == request.Id);
        if (request.Op == ActivityOp.End)
        {
            if (index < 0)
                return (ActivityChange.Unknown, null);
            var ended = _items[index];
            _items.RemoveAt(index);
            return (ActivityChange.Ended, ended);
        }

        var previous = index >= 0 ? _items[index] : null;
        if (previous == null && _items.Count >= MaxActivities)
            return (ActivityChange.Full, null);
        string? title = request.Title is { Length: > 0 } t ? t : previous?.Title;
        if (title == null)
            return (ActivityChange.NeedsTitle, null);

        var ttl = request.State switch
        {
            ActivityState.Done => DoneTtl,
            ActivityState.Attention or ActivityState.Failed => WarningTtl,
            _ => request.Ttl ?? DefaultTtl,
        };
        var activity = new Activity(
            request.Id,
            title,
            request.Text ?? previous?.Text,
            request.State == ActivityState.Done ? null : request.Progress ?? previous?.Progress,
            request.State,
            request.Icon ?? previous?.Icon ?? "activity",
            now,
            now + ttl,
            request.Window ?? previous?.Window);
        if (previous == null)
        {
            _items.Add(activity);
            return (ActivityChange.Added, activity);
        }
        _items[index] = activity;
        return (previous.State != activity.State ? ActivityChange.StateChanged : ActivityChange.Updated, activity);
    }

    /// <summary>The user put it away (the strip's ×); if its program sends it again, it comes back.</summary>
    public bool Remove(string id) => _items.RemoveAll(a => a.Id == id) > 0;

    /// <summary>Its window closed: clicking it no longer offers to go there.</summary>
    public void ForgetWindow(string id)
    {
        int index = _items.FindIndex(a => a.Id == id);
        if (index >= 0)
            _items[index] = _items[index] with { Window = null };
    }

    /// <summary>Drops what nobody updated in time (a script that died midway). True if any went.</summary>
    public bool Prune(DateTime now) => _items.RemoveAll(a => a.Expires <= now) > 0;

    public void Clear() => _items.Clear();
}
