using System.Collections.Generic;
using System.Linq;

namespace Yata.Services;

/// <summary>
/// What the user said about the open players (by app id), on top of <see cref="MediaService.Pick"/>:
/// one switched to with ⇄ stays on the notch until another player starts playing; one taken off with ✕
/// stays off until it starts playing again itself. "Starts playing" is a change from not playing to
/// playing: a new intent, not the playback that was already going on when the choice was made.
/// </summary>
internal sealed class PlayerChoice
{
    private readonly Dictionary<string, bool> _playing = new();
    private readonly HashSet<string> _removed = new();

    /// <summary>The player switched to, while it holds; null follows <see cref="MediaService.Pick"/>.</summary>
    public string? Chosen { get; private set; }

    public bool IsRemoved(string id) => _removed.Contains(id);

    /// <summary>A player's play/pause state, as seen now. Returns whether a choice changed.</summary>
    public bool Seen(string id, bool playing)
    {
        bool started = playing && _playing.TryGetValue(id, out bool was) && !was;
        _playing[id] = playing;
        if (!started)
            return false;
        bool changed = _removed.Remove(id);
        if (Chosen != null && Chosen != id)
        {
            Chosen = null;
            changed = true;
        }
        return changed;
    }

    public void Choose(string id)
    {
        Chosen = id;
        _removed.Remove(id);
    }

    public void Remove(string id)
    {
        _removed.Add(id);
        if (Chosen == id)
            Chosen = null;
    }

    /// <summary>Players that closed: their choices go with them.</summary>
    public void Keep(IReadOnlyCollection<string> open)
    {
        foreach (string id in _playing.Keys.Where(id => !open.Contains(id)).ToList())
            _playing.Remove(id);
        _removed.RemoveWhere(id => !open.Contains(id));
        if (Chosen != null && !open.Contains(Chosen))
            Chosen = null;
    }
}
