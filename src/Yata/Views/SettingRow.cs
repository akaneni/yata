using System;
using System.Windows;
using System.Windows.Controls;

namespace Yata.Views;

/// <summary>
/// One line of the settings window: the first child (label, maybe a hint) wraps in whatever
/// width the others leave; the others share the right edge. More than one control there is for
/// rows whose control changes, like a "Permitir" button that becomes a switch.
/// </summary>
public sealed class SettingRow : Panel
{
    private const double Gap = 20;

    protected override Size MeasureOverride(Size available)
    {
        double right = 0, height = 0;
        for (int i = 1; i < InternalChildren.Count; i++)
        {
            var control = InternalChildren[i];
            control.Measure(available);
            right = Math.Max(right, control.DesiredSize.Width);
            height = Math.Max(height, control.DesiredSize.Height);
        }
        if (InternalChildren.Count == 0)
            return new Size(right, height);

        double gap = right > 0 ? Gap : 0;
        var label = InternalChildren[0];
        label.Measure(new Size(Math.Max(0, available.Width - right - gap), available.Height));
        return new Size(label.DesiredSize.Width + gap + right, Math.Max(height, label.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size final)
    {
        double right = 0;
        for (int i = 1; i < InternalChildren.Count; i++)
            right = Math.Max(right, InternalChildren[i].DesiredSize.Width);
        double gap = right > 0 ? Gap : 0;

        // Each child keeps its own alignment inside its slot (controls: right and centered).
        if (InternalChildren.Count > 0)
            InternalChildren[0].Arrange(new Rect(0, 0, Math.Max(0, final.Width - right - gap), final.Height));
        for (int i = 1; i < InternalChildren.Count; i++)
            InternalChildren[i].Arrange(new Rect(final.Width - right, 0, right, final.Height));
        return final;
    }
}
