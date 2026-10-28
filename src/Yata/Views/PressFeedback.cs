using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Yata.Helpers;

namespace Yata.Views;

/// <summary>
/// Buttons sink a little while pressed and come back on release: the click is felt, not only seen.
/// Set on the button styles in App.xaml. Mouse events rather than watching IsPressed, so a button in
/// a list row that goes away isn't kept alive by the watcher. Nothing moves when motion is reduced.
/// </summary>
internal static class PressFeedback
{
    private const double Pressed = 0.94;

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(PressFeedback), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ButtonBase button)
            return;
        if ((bool)e.NewValue)
        {
            button.PreviewMouseLeftButtonDown += OnDown;
            button.PreviewMouseLeftButtonUp += OnUp;
            button.MouseLeave += OnUp;
        }
        else
        {
            button.PreviewMouseLeftButtonDown -= OnDown;
            button.PreviewMouseLeftButtonUp -= OnUp;
            button.MouseLeave -= OnUp;
        }
    }

    private static void OnDown(object sender, MouseButtonEventArgs e) =>
        Scale((ButtonBase)sender, Pressed, TimeSpan.FromMilliseconds(70), new QuadraticEase { EasingMode = EasingMode.EaseOut });

    private static void OnUp(object sender, MouseEventArgs e) =>
        Scale((ButtonBase)sender, 1, TimeSpan.FromMilliseconds(180), new CubicEase { EasingMode = EasingMode.EaseOut });

    private static void Scale(ButtonBase button, double to, TimeSpan duration, IEasingFunction ease)
    {
        if (Motion.Reduced || !button.IsEnabled || ScaleOf(button) is not { } scale)
            return;
        if (to == 1 && scale.ScaleX == 1 && !scale.HasAnimatedProperties)
            return; // Left without having been pressed.
        var animation = new DoubleAnimation(to, duration) { EasingFunction = ease };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    /// <summary>The button's own scale, made on first use; none if it already moves another way (the spinning refresh).</summary>
    private static ScaleTransform? ScaleOf(ButtonBase button)
    {
        if (button.RenderTransform is ScaleTransform scale)
            return scale;
        if (button.RenderTransform != null && button.RenderTransform != Transform.Identity)
            return null;
        button.RenderTransformOrigin = new Point(0.5, 0.5);
        return (ScaleTransform)(button.RenderTransform = new ScaleTransform());
    }
}
