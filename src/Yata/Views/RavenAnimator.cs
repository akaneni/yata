using System;
using System.Windows.Threading;

namespace Yata.Views;

/// <summary>
/// Gives a <see cref="RavenSprite"/> its behaviour: idle fidgets at random intervals,
/// sleeping, nodding to music and flapping. Timers only tick while something is happening.
/// </summary>
public sealed class RavenAnimator
{
    public enum Mode { Still, Perched, Asleep, Nodding, Flying }

    private static readonly RavenFrame[] FlapCycle = { RavenFrame.FlyUp, RavenFrame.FlyMid, RavenFrame.FlyDown, RavenFrame.FlyMid };

    private readonly RavenSprite _sprite;
    private readonly DispatcherTimer _timer = new();
    private readonly Random _random = new();
    private Mode _mode = Mode.Still;
    private int _step;
    private Action? _afterGesture;

    public RavenAnimator(RavenSprite sprite)
    {
        _sprite = sprite;
        _timer.Tick += OnTick;
    }

    public Mode Current => _mode;

    /// <summary>Sits and fidgets: blinks, glances back, preens.</summary>
    public void Perch()
    {
        if (_mode == Mode.Perched)
            return;
        Enter(Mode.Perched, RavenFrame.Perch);
        ScheduleFidget();
    }

    public void Sleep() => Enter(Mode.Asleep, RavenFrame.Sleep);

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

    public void Stop() => Enter(Mode.Still, RavenFrame.Perch);

    private void Enter(Mode mode, RavenFrame frame)
    {
        _timer.Stop();
        _afterGesture = null;
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
                Fidget();
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

    private void Fidget()
    {
        // Finishing a gesture: return to the resting pose and wait for the next one.
        if (_afterGesture != null)
        {
            var done = _afterGesture;
            _afterGesture = null;
            done();
            ScheduleFidget();
            return;
        }

        int roll = _random.Next(100);
        if (roll < 60)
            Gesture(RavenFrame.Blink, 130, () => _sprite.Frame = RavenFrame.Perch);
        else if (roll < 85)
            Gesture(RavenFrame.Perch, _random.Next(1500, 3000), () => _sprite.FacingLeft = false, faceLeft: true);
        else
            Gesture(RavenFrame.Preen, 700, () => _sprite.Frame = RavenFrame.Perch);
    }

    private void Gesture(RavenFrame frame, int durationMs, Action undo, bool faceLeft = false)
    {
        _sprite.Frame = frame;
        if (faceLeft)
            _sprite.FacingLeft = true;
        _afterGesture = undo;
        _timer.Interval = TimeSpan.FromMilliseconds(durationMs);
    }
}
