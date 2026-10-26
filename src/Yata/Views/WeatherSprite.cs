using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Yata.Services;

namespace Yata.Views;

/// <summary>
/// 1-bit pixel weather icons in Yata's style (white on the notch's black), drawn like
/// <see cref="RavenSprite"/>: each sprite pixel snapped to whole device pixels.
/// </summary>
public sealed class WeatherSprite : FrameworkElement
{
    public const int Columns = 10;
    public const int Rows = 9;

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(WeatherIcon), typeof(WeatherSprite),
        new FrameworkPropertyMetadata(WeatherIcon.Clear, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PixelSizeProperty = DependencyProperty.Register(
        nameof(PixelSize), typeof(double), typeof(WeatherSprite),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(WeatherSprite),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public WeatherIcon Icon
    {
        get => (WeatherIcon)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public double PixelSize
    {
        get => (double)GetValue(PixelSizeProperty);
        set => SetValue(PixelSizeProperty, value);
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public WeatherSprite()
    {
        SnapsToDevicePixels = true;
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
    }

    private double SnappedPixel()
    {
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        return Math.Max(1, Math.Round(PixelSize * scale)) / scale;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double px = SnappedPixel();
        return new Size(Columns * px, Rows * px);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double px = SnappedPixel();
        foreach (var (row, start, length) in Runs[Icon])
            dc.DrawRectangle(Fill, null, new Rect(start * px, row * px, length * px, px));
    }

    // Hand-drawn 10x9 pixel art; '#' is a lit pixel. Stored as horizontal runs per row.

    private static readonly Dictionary<WeatherIcon, (int Row, int Start, int Length)[]> Runs = new()
    {
        [WeatherIcon.Clear] = Parse(
            "....#.....",
            ".#.....#..",
            "...###....",
            "..#####...",
            "#.#####.#.",
            "..#####...",
            "...###....",
            ".#.....#..",
            "....#....."),
        [WeatherIcon.ClearNight] = Parse(
            "...####...",
            "..###.....",
            ".###......",
            ".###......",
            ".###......",
            ".###......",
            "..###.....",
            "...####...",
            ".........."),
        [WeatherIcon.PartlyCloudy] = Parse(
            "......#..#",
            ".......##.",
            "...###.##.",
            "..#####...",
            ".#######..",
            "#########.",
            "#########.",
            ".#######..",
            ".........."),
        [WeatherIcon.Cloudy] = Parse(
            "..........",
            "...###....",
            "..#####...",
            ".#######..",
            "#########.",
            "#########.",
            "#########.",
            ".#######..",
            ".........."),
        [WeatherIcon.Fog] = Parse(
            "..........",
            "########..",
            "..........",
            "..########",
            "..........",
            "########..",
            "..........",
            "..########",
            ".........."),
        [WeatherIcon.Rain] = Parse(
            "...###....",
            "..#####...",
            ".#######..",
            "#########.",
            ".#######..",
            "..........",
            ".#..#..#..",
            "#..#..#...",
            ".........."),
        [WeatherIcon.Storm] = Parse(
            "...###....",
            "..#####...",
            ".#######..",
            "#########.",
            ".###.###..",
            "....#.....",
            "...###....",
            "....#.....",
            "...#......"),
        [WeatherIcon.Snow] = Parse(
            "...###....",
            "..#####...",
            ".#######..",
            "#########.",
            ".#######..",
            "..........",
            ".#...#...#",
            "...#...#..",
            ".#...#...."),
    };

    private static (int Row, int Start, int Length)[] Parse(params string[] rows) =>
        rows.SelectMany((line, row) =>
        {
            var runs = new List<(int, int, int)>();
            for (int x = 0; x < line.Length; x++)
            {
                if (line[x] != '#')
                    continue;
                int start = x;
                while (x < line.Length && line[x] == '#')
                    x++;
                runs.Add((row, start, x - start));
            }
            return runs;
        }).ToArray();
}
