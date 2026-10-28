using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Yata.Helpers;
using Yata.Services;

namespace Yata.Views;

public partial class WaterPage : UserControl
{
    private const int ChartDays = 7;

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

        TodayRun.Text = _water.TodayMl.ToString("N0", Lang.Culture);
        GoalRun.Text = $" / {_water.GoalMl.ToString("N0", Lang.Culture)} ml";
        CupText.Text = $"+{_water.CupMl} ml";
        BarFill.Width = Math.Min(1, (double)_water.TodayMl / _water.GoalMl) * Bar.ActualWidth;
        BuildChart();
    }

    private void BuildChart()
    {
        var days = _water!.LastDays(ChartDays);
        double max = Math.Max(_water.GoalMl, days.Max(d => d.Ml));
        Chart.Show(days.Select(d => (d.Day, (double)d.Ml)).ToList(), max,
            (day, ml) => $"{day.ToString(Strings.Format_DayMonth, Lang.Culture)}: {ml} ml");
    }

    private void OnDrink(object sender, RoutedEventArgs e) => _water?.Drink();

    private void OnUndo(object sender, RoutedEventArgs e) => _water?.Undo();
}
