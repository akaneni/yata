using System;

namespace Notch.Helpers;

/// <summary>Damped spring used for the notch's size and expand animations.</summary>
internal sealed class Spring
{
    public double Value;
    public double Velocity;
    public double Target;
    public double Stiffness = 380;
    public double DampingRatio = 0.72;

    private readonly double _epsilon;

    public Spring(double value, double epsilon)
    {
        Value = Target = value;
        _epsilon = epsilon;
    }

    /// <summary>Advances the spring; returns false once it has settled on the target.</summary>
    public bool Step(double seconds)
    {
        double damping = 2 * Math.Sqrt(Stiffness) * DampingRatio;
        while (seconds > 0)
        {
            double dt = Math.Min(seconds, 1.0 / 240);
            double acceleration = Stiffness * (Target - Value) - damping * Velocity;
            Velocity += acceleration * dt;
            Value += Velocity * dt;
            seconds -= dt;
        }

        if (Math.Abs(Target - Value) < _epsilon && Math.Abs(Velocity) < _epsilon * 10)
        {
            Value = Target;
            Velocity = 0;
            return false;
        }
        return true;
    }
}
