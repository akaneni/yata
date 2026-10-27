using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Yata.Views;

/// <summary>
/// The notch hides its scrollbars, so a list fades out at an edge that has more beyond it.
/// The mask is in absolute units, sized to the viewport: relative to the bounding box it would
/// stretch over the whole list, like the lyrics' fade once did.
/// </summary>
public static class ScrollFade
{
    private const double Edge = 28;

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(ScrollFade), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer scroll)
            return;
        scroll.ScrollChanged -= OnScrollChanged;
        if ((bool)e.NewValue)
            scroll.ScrollChanged += OnScrollChanged;
        else
            scroll.OpacityMask = null;
    }

    // Also raised when the list or the viewport changes size, not only on scrolling.
    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        var scroll = (ScrollViewer)sender;
        bool above = scroll.VerticalOffset > 0.5;
        bool below = scroll.VerticalOffset < scroll.ScrollableHeight - 0.5;
        double height = scroll.ActualHeight;
        if (!above && !below || height <= 2 * Edge)
        {
            scroll.OpacityMask = null;
            return;
        }

        var mask = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, height),
        };
        mask.GradientStops.Add(new GradientStop(above ? Colors.Transparent : Colors.Black, 0));
        mask.GradientStops.Add(new GradientStop(Colors.Black, Edge / height));
        mask.GradientStops.Add(new GradientStop(Colors.Black, 1 - Edge / height));
        mask.GradientStops.Add(new GradientStop(below ? Colors.Transparent : Colors.Black, 1));
        mask.Freeze();
        scroll.OpacityMask = mask;
    }
}
