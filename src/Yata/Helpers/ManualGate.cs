using System;

namespace Yata.Helpers;

/// <summary>
/// The "do it now" buttons (check for updates, sync, refresh): a click goes through at once, but
/// clicking again within a few seconds doesn't hit the network again. Keeps Google's quota and
/// GitHub's rate limit safe from an impatient double click.
/// </summary>
internal sealed class ManualGate
{
    public static readonly TimeSpan Gap = TimeSpan.FromSeconds(15);

    private readonly Func<DateTime> _clock;
    private DateTime _last = DateTime.MinValue;

    public ManualGate(Func<DateTime>? clock = null) => _clock = clock ?? (() => DateTime.Now);

    /// <summary>True if this click should run; false if the previous one was too recent.</summary>
    public bool TryPass()
    {
        var now = _clock();
        if (now - _last < Gap && now >= _last)
            return false;
        _last = now;
        return true;
    }
}
