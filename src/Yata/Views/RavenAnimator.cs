using System;
using System.Collections.Generic;
using System.Windows.Threading;

namespace Yata.Views;

/// <summary>
/// Gives a <see cref="RavenSprite"/> its behaviour: idle fidgets at random intervals,
/// sleeping, nodding to music, flapping and short reactions (caw, hop, shake).
/// One timer drives everything, and it only ticks while something is happening.
/// </summary>
public sealed class RavenAnimator
{
    public enum Mode { Still, Perched, Asleep, Nodding, Flying, Reacting }

    /// <summary>One pose held for a while; <paramref name="Left"/> mirrors it (glancing back).</summary>
    public readonly record struct Step(RavenFrame Frame, int Ms, bool Left = false);

    private static readonly RavenFrame[] FlapCycle = { RavenFrame.FlyUp, RavenFrame.FlyMid, RavenFrame.FlyDown, RavenFrame.FlyMid };

    // Gestures. Each ends back on the plain perch; durations are hand-tuned, not physics.
    internal static readonly Step[] BlinkSteps = { new(RavenFrame.Blink, 130) };
    internal static readonly Step[] PreenSteps = { new(RavenFrame.Preen, 700) };
    internal static readonly Step[] StretchSteps = { new(RavenFrame.Stretch, 900), new(RavenFrame.Blink, 130) };
    internal static readonly Step[] HopSteps = { new(RavenFrame.Crouch, 90), new(RavenFrame.Hop, 160), new(RavenFrame.Crouch, 90) };
    internal static readonly Step[] LandSteps = { new(RavenFrame.Crouch, 120) };
    internal static readonly Step[] CawSteps =
    {
        new(RavenFrame.Caw, 170), new(RavenFrame.Perch, 140), new(RavenFrame.Caw, 170), new(RavenFrame.Perch, 400),
    };
    // Shaking off water: puffed up, head whipping side to side, drops flying.
    internal static readonly Step[] ShakeSteps =
    {
        new(RavenFrame.Crouch, 110),
        new(RavenFrame.Fluff, 80), new(RavenFrame.FluffSplash, 80, Left: true),
        new(RavenFrame.Fluff, 80), new(RavenFrame.FluffSplash, 80, Left: true),
        new(RavenFrame.Fluff, 80), new(RavenFrame.FluffSplash, 80, Left: true),
        new(RavenFrame.Perch, 250), new(RavenFrame.Blink, 130),
    };

    // Asleep: a slow breath, well under a frame per second.
    private static readonly TimeSpan BreathOut = TimeSpan.FromMilliseconds(1900);
    private static readonly TimeSpan BreathIn = TimeSpan.FromMilliseconds(1300);

    private readonly RavenSprite _sprite;
    private readonly DispatcherTimer _timer = new();
    private readonly Random _random = new();
    private readonly Queue<Step> _steps = new();
    private Mode _mode = Mode.Still;
    private int _step;
    private bool _gesturing;

    public RavenAnimator(RavenSprite sprite)
    {
        _sprite = sprite;
        _timer.Tick += OnTick;
    }

    public Mode Current => _mode;

    /// <summary>Sits and fidgets: blinks, glances back, preens; now and then looks up, stretches or hops.</summary>
    public void Perch()
    {
        if (_mode == Mode.Perched)
            return;
        Enter(Mode.Perched, RavenFrame.Perch);
        ScheduleFidget();
    }

    /// <summary>Dozes, breathing slowly.</summary>
    public void Sleep()
    {
        if (_mode == Mode.Asleep)
            return;
        Enter(Mode.Asleep, RavenFrame.Sleep);
        _timer.Interval = BreathOut;
        _timer.Start();
    }

    /// <summary>Bobs its head, roughly to a mid-tempo beat.</summary>
    public void Nod()
    {
        if (_mode == Mode.Nodding)
            return;
        Enter(Mode.Nodding, RavenFrame.Perch);
        _timer.Interval = TimeSpan.FromMilliseconds(330);
        _timer.Start();
    }

    public void Fly()
    {
        if (_mode == Mode.Flying)
            return;
        Enter(Mode.Flying, RavenFrame.FlyUp);
        _timer.Interval = TimeSpan.FromMilliseconds(85);
        _timer.Start();
    }

    /// <summary>Touches down (knees bend), then perches.</summary>
    public void Land() => React(LandSteps);

    /// <summary>Two caws: a reminder is due.</summary>
    public void Caw() => React(CawSteps);

    /// <summary>A little hop of joy.</summary>
    public void Hop() => React(HopSteps);

    /// <summary>Shakes off water, for the water goal.</summary>
    public void Shake() => React(ShakeSteps);

    public void Stop() => Enter(Mode.Still, RavenFrame.Perch);

    /// <summary>Plays a short sequence, then goes back to perching.</summary>
    private void React(Step[] steps)
    {
        Enter(Mode.Reacting, RavenFrame.Perch);
        Queue(steps);
        Advance();
    }

    private void Enter(Mode mode, RavenFrame frame)
    {
        _timer.Stop();
        _steps.Clear();
        _gesturing = false;
        _mode = mode;
        _step = 0;
        _sprite.Frame = frame;
        _sprite.FacingLeft = false;
    }

    private void ScheduleFidget()
    {
        _timer.Interval = TimeSpan.FromMilliseconds(_random.Next(2500, 6500));
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        switch (_mode)
        {
            case Mode.Perched:
                if (_steps.Count > 0)
                    Advance();
                else if (_gesturing)
                    Rest();
                else
                    Fidget();
                break;
            case Mode.Reacting:
                if (_steps.Count > 0)
                    Advance();
                else
                    Perch();
                break;
            case Mode.Asleep:
                bool inhale = _sprite.Frame == RavenFrame.Sleep;
                _sprite.Frame = inhale ? RavenFrame.SleepIn : RavenFrame.Sleep;
                _timer.Interval = inhale ? BreathIn : BreathOut;
                break;
            case Mode.Nodding:
                _sprite.Frame = ++_step % 2 == 0 ? RavenFrame.Perch : RavenFrame.Nod;
                break;
            case Mode.Flying:
                _sprite.Frame = FlapCycle[++_step % FlapCycle.Length];
                break;
            default:
                _timer.Stop();
                break;
        }
    }

    /// <summary>A gesture finished: back to the resting pose until the next one.</summary>
    private void Rest()
    {
        _gesturing = false;
        _sprite.Frame = RavenFrame.Perch;
        _sprite.FacingLeft = false;
        ScheduleFidget();
    }

    private void Fidget()
    {
        // Mostly small things; the bigger gestures stay rare so they don't get tiresome.
        int roll = _random.Next(100);
        if (roll < 55)
            Queue(BlinkSteps);
        else if (roll < 77)
            Queue(new Step(RavenFrame.Perch, _random.Next(1500, 3000), Left: true));
        else if (roll < 87)
            Queue(PreenSteps);
        else if (roll < 94)
            Queue(new Step(RavenFrame.LookUp, _random.Next(1200, 2200)));
        else if (roll < 97)
            Queue(StretchSteps);
        else
            Queue(HopSteps);
        _gesturing = true;
        Advance();
    }

    private void Queue(params Step[] steps)
    {
        foreach (var step in steps)
            _steps.Enqueue(step);
    }

    private void Advance()
    {
        var step = _steps.Dequeue();
        _sprite.Frame = step.Frame;
        _sprite.FacingLeft = step.Left;
        _timer.Interval = TimeSpan.FromMilliseconds(step.Ms);
        _timer.Start();
    }
}
