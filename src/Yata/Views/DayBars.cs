using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Yata.Helpers;

namespace Yata.Views;

/// <summary>
/// The last days as small bars with the weekday's initial under each, today's in its own color:
/// the week on the Water and Focus pages. Each bar sits in a cell with 3 px on either side.
/// </summary>
public sealed class DayBars : Grid
{
    private string _shown = "";

    /// <summary>Height of a full bar (the larger of the goal and the best day).</summary>
    public double BarHeight { get; set; } = 30;

    /// <summary>Resource keys of the bar colors.</summary>
    public string TodayBrush { get; set; } = "TextPrimary";
    public string PastBrush { get; set; } = "TrackBrush";

    /// <param name="days">Oldest first, ending today.</param>
    /// <param name="max">The value of a full bar.</param>
    /// <param name="tooltip">Text for a day's bar, e.g. "12/10: 1,500 ml".</param>
    public void Show(IReadOnlyList<(DateTime Day, double Value)> days, double max, Func<DateTime, double, string> tooltip)
    {
        // Pages refresh every second while the focus timer runs; the bars are rebuilt only when a
        // height or a tooltip changes (minutes, not seconds).
        string shown = string.Join(";", days.Select(d => $"{d.Day:yyyyMMdd}:{BarFor(d.Value, max):F0}:{tooltip(d.Day, d.Value)}"));
        if (shown == _shown)
            return;
        _shown = shown;

        Children.Clear();
        ColumnDefinitions.Clear();
        for (int i = 0; i < days.Count; i++)
        {
            var (day, value) = days[i];
            ColumnDefinitions.Add(new ColumnDefinition());

            var cell = new Grid { Margin = new Thickness(3, 0, 3, 0) };
            cell.RowDefinitions.Add(new RowDefinition());
            cell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var bar = new Rectangle
            {
                Height = BarFor(value, max),
                RadiusX = 2,
                RadiusY = 2,
                VerticalAlignment = VerticalAlignment.Bottom,
                ToolTip = tooltip(day, value),
            };
            bar.SetResourceReference(Shape.FillProperty, i == days.Count - 1 ? TodayBrush : PastBrush);
            cell.Children.Add(bar);

            var label = new TextBlock
            {
                Text = day.ToString("ddd", Lang.Culture)[..1].ToUpper(Lang.Culture),
                FontSize = 10,
                Margin = new Thickness(0, 3, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            SetRow(label, 1);
            cell.Children.Add(label);

            SetColumn(cell, i);
            Children.Add(cell);
        }
    }

    private double BarFor(double value, double max) => Math.Max(3, BarHeight * value / Math.Max(max, 1));
}
