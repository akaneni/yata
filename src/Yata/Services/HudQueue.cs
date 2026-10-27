using System.Collections.Generic;
using System.Linq;

namespace Yata.Services;

/// <summary>
/// Decides which notice the closed notch shows. Reminders (Important) are never lost: they wait
/// while the notch can't show them and take turns with each other. Status blips (volume, Caps
/// Lock...) only matter right now: they show at once, even over a reminder, which then comes back.
/// </summary>
internal sealed class HudQueue
{
    // Enough for a burst (several reminders at the same minute) without piling up a backlog.
    public const int MaxWaiting = 5;

    private readonly LinkedList<HudMessage> _waiting = new();

    /// <summary>The notice on screen, or null.</summary>
    public HudMessage? Current { get; private set; }

    public int Waiting => _waiting.Count;

    /// <summary>A new notice. Returns what to show now, or null to leave the notch as it is.</summary>
    public HudMessage? Offer(HudMessage message, bool canShow)
    {
        if (!canShow)
        {
            if (message.Important)
                Enqueue(message, first: false);
            return null;
        }
        if (Current is { Important: true } current)
        {
            if (message.Important)
            {
                // Another reminder: it waits its turn instead of cutting this one short.
                Enqueue(message, first: false);
                return null;
            }
            // A blip answers something the user just did (pressed a volume key): it can't wait, and
            // the interrupted reminder is shown again right after it.
            Enqueue(current, first: true);
        }
        return Current = message;
    }

    /// <summary>The current notice ran its time. Returns the next one, or null.</summary>
    public HudMessage? Finish()
    {
        Current = null;
        return Next();
    }

    /// <summary>
    /// The notch opened or hid while a notice was up: a reminder goes back to the front of the line,
    /// a blip is just dropped.
    /// </summary>
    public void Interrupt()
    {
        if (Current is { Important: true } current)
            Enqueue(current, first: true);
        Current = null;
    }

    /// <summary>The notch can show notices again. Returns the next one if none is up.</summary>
    public HudMessage? Resume() => Current == null ? Next() : null;

    private HudMessage? Next()
    {
        if (_waiting.First is not { } first)
            return null;
        _waiting.RemoveFirst();
        return Current = first.Value;
    }

    private void Enqueue(HudMessage message, bool first)
    {
        // The same reminder twice (a repeated nudge while the notch was open) only needs saying once.
        if (_waiting.FirstOrDefault(m => m.Kind == message.Kind && m.Text == message.Text) is { } same)
            _waiting.Remove(same);
        if (first)
            _waiting.AddFirst(message);
        else
            _waiting.AddLast(message);
        // Over the limit, the oldest goes (the most stale), unless it's the one just put back in front.
        while (_waiting.Count > MaxWaiting)
            _waiting.Remove(first ? _waiting.First!.Next! : _waiting.First!);
    }
}
