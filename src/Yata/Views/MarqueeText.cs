using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Yata.Views;

/// <summary>
/// Hosts a trimmed <see cref="TextBlock"/> and, on request, scrolls it once to reveal the end
/// of a text that doesn't fit, then settles back to the ellipsis. Scrolling once per change
/// (not forever) keeps the notch from repainting while nothing new is happening.
/// </summary>
public sealed class MarqueeText : Decorator
{
    private const double Speed = 32;          // DIPs per second
    private static readonly TimeSpan Hold = TimeSpan.FromSeconds(1.4);

    public static readonly DependencyProperty OffsetProperty = DependencyProperty.Register(
        nameof(Offset), typeof(double), typeof(MarqueeText),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsArrange));

    private bool _scrolling;

    public MarqueeText() => ClipToBounds = true;

    public double Offset
    {
        get => (double)GetValue(OffsetProperty);
        set => SetValue(OffsetProperty, value);
    }

    /// <summary>Scrolls through the text once if it overflows; restarts if already scrolling.</summary>
    public void Run()
    {
        Stop();
        if (Child == null || !IsVisible || ActualWidth <= 0)
            return;

        Child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double overflow = Child.DesiredSize.Width - ActualWidth;
        if (overflow <= 1)
            return;

        _scrolling = true;
        InvalidateMeasure();

        var travel = TimeSpan.FromSeconds(overflow / Speed);
        var scroll = new DoubleAnimationUsingKeyFrames();
        scroll.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        scroll.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(Hold)));
        scroll.KeyFrames.Add(new EasingDoubleKeyFrame(overflow, KeyTime.FromTimeSpan(Hold + travel),
            new SineEase { EasingMode = EasingMode.EaseInOut }));
        scroll.KeyFrames.Add(new LinearDoubleKeyFrame(overflow, KeyTime.FromTimeSpan(Hold + travel + Hold)));
        // Nothing moves during the holds; 30 fps is smooth enough for slow text.
        Timeline.SetDesiredFrameRate(scroll, 30);
        scroll.Completed += (_, _) =>
        {
            if (!_scrolling)
                return;
            Stop();
            Child?.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
        };
        BeginAnimation(OffsetProperty, scroll);
    }

    public void Stop()
    {
        if (!_scrolling)
            return;
        _scrolling = false;
        BeginAnimation(OffsetProperty, null);
        Offset = 0;
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child == null)
            return default;
        // While scrolling the text is laid out at full length; otherwise it trims to the space.
        Child.Measure(_scrolling ? new Size(double.PositiveInfinity, constraint.Height) : constraint);
        return new Size(Math.Min(Child.DesiredSize.Width, constraint.Width), Child.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child == null)
            return arrangeSize;
        double width = _scrolling ? Child.DesiredSize.Width : arrangeSize.Width;
        Child.Arrange(new Rect(-Offset, 0, width, arrangeSize.Height));
        return arrangeSize;
    }
}
