using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Yata.Services;

namespace Yata.Views;

public partial class WaterPage : UserControl
{
    private const int ChartDays = 7;
    private static readonly CultureInfo PtBr = new("pt-BR");
    private static readonly Brush WaterBrush = Frozen(Color.FromRgb(0x5A, 0xB8, 0xFF));
    private static readonly Brush PastBrush = Frozen(Color.FromArgb(0x59, 0x5A, 0xB8, 0xFF));

    private WaterTracker? _water;

    public WaterPage()
    {
        InitializeComponent();
        Bar.SizeChanged += (_, _) => Refresh();
        IsVisibleChanged += (_, _) => Refresh(); // Picks up a new day.
    }

    public void Attach(WaterTracker water)
    {
        _water = water;
        _water.Changed += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        if (_water == null)
            return;

        TodayRun.Text = _water.TodayMl.ToString("N0", PtBr);
        GoalRun.Text = $" / {_water.GoalMl.ToString("N0", PtBr)} ml";
        CupText.Text = $"+{_water.CupMl} ml";
        BarFill.Width = Math.Min(1, (double)_water.TodayMl / _water.GoalMl) * Bar.ActualWidth;
        BuildChart();
    }

    private void BuildChart()
    {
        var days = _water!.LastDays(ChartDays);
        double max = Math.Max(_water.GoalMl, days.Max(d => d.Ml));

        Chart.Children.Clear();
        Chart.ColumnDefinitions.Clear();
        for (int i = 0; i < days.Count; i++)
        {
            var (day, ml) = days[i];
            Chart.ColumnDefinitions.Add(new ColumnDefinition());

            var cell = new Grid { Margin = new Thickness(3, 0, 3, 0) };
            cell.RowDefinitions.Add(new RowDefinition());
            cell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            cell.Children.Add(new Rectangle
            {
                Height = Math.Max(3, 30 * ml / max),
                RadiusX = 2,
                RadiusY = 2,
                VerticalAlignment = VerticalAlignment.Bottom,
                Fill = i == days.Count - 1 ? WaterBrush : PastBrush,
                ToolTip = $"{day:dd/MM}: {ml} ml",
            });

            var label = new TextBlock
            {
                Text = day.ToString("ddd", PtBr)[..1].ToUpper(PtBr),
                FontSize = 10,
                Margin = new Thickness(0, 3, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = (Brush)FindResource("TextSecondary"),
            };
            Grid.SetRow(label, 1);
            cell.Children.Add(label);

            Grid.SetColumn(cell, i);
            Chart.Children.Add(cell);
        }
    }

    private void OnDrink(object sender, RoutedEventArgs e) => _water?.Drink();

    private void OnUndo(object sender, RoutedEventArgs e) => _water?.Undo();

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
