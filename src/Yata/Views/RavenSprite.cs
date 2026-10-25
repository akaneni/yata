using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Yata.Views;

public enum RavenFrame { Perch, Blink, Nod, Preen, Sleep, FlyUp, FlyMid, FlyDown }

/// <summary>
/// Yata, the 1-bit pixel raven. Draws one frame, snapping each sprite pixel to whole
/// device pixels so it stays crisp at any display scale. Faces right unless mirrored.
/// </summary>
public sealed class RavenSprite : FrameworkElement
{
    public const int Columns = 16;
    public const int Rows = 14;

    public static readonly DependencyProperty FrameProperty = DependencyProperty.Register(
        nameof(Frame), typeof(RavenFrame), typeof(RavenSprite),
        new FrameworkPropertyMetadata(RavenFrame.Perch, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FacingLeftProperty = DependencyProperty.Register(
        nameof(FacingLeft), typeof(bool), typeof(RavenSprite),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Approximate size of one sprite pixel in DIPs; rounded to whole device pixels.</summary>
    public static readonly DependencyProperty PixelSizeProperty = DependencyProperty.Register(
        nameof(PixelSize), typeof(double), typeof(RavenSprite),
        new FrameworkPropertyMetadata(1.5, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(RavenSprite),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public RavenFrame Frame
    {
        get => (RavenFrame)GetValue(FrameProperty);
        set => SetValue(FrameProperty, value);
    }

    public bool FacingLeft
    {
        get => (bool)GetValue(FacingLeftProperty);
        set => SetValue(FacingLeftProperty, value);
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

    public RavenSprite()
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
        foreach (var (row, start, length) in Runs[Frame])
        {
            int x = FacingLeft ? Columns - start - length : start;
            dc.DrawRectangle(Fill, null, new Rect(x * px, row * px, length * px, px));
        }
    }

    // ---- Frames ---------------------------------------------------------------------
    // Hand-drawn 16x14 pixel art; '#' is a lit pixel. Stored as horizontal runs per row.

    private static readonly Dictionary<RavenFrame, (int Row, int Start, int Length)[]> Runs = new()
    {
        [RavenFrame.Perch] = Parse(
            "................",
            ".........####...",
            "........######..",
            "........##.####.",
            "........########",
            ".......#####....",
            "......######....",
            ".....#######....",
            "....########....",
            "...########.....",
            "..#######.......",
            ".####.#.#.......",
            "##....#.#.......",
            ".....##.##......"),
        [RavenFrame.Blink] = Parse(
            "................",
            ".........####...",
            "........######..",
            "........#######.",
            "........########",
            ".......#####....",
            "......######....",
            ".....#######....",
            "....########....",
            "...########.....",
            "..#######.......",
            ".####.#.#.......",
            "##....#.#.......",
            ".....##.##......"),
        [RavenFrame.Nod] = Parse(
            "................",
            "................",
            ".........####...",
            "........######..",
            "........##.####.",
            ".......#########",
            "......######....",
            ".....#######....",
            "....########....",
            "...########.....",
            "..#######.......",
            ".####.#.#.......",
            "##....#.#.......",
            ".....##.##......"),
        [RavenFrame.Preen] = Parse(
            "................",
            "................",
            "................",
            "................",
            "........####....",
            ".......######...",
            ".......###.##...",
            "......#######...",
            ".....#######.#..",
            "....########.#..",
            "..#########.....",
            ".####.#.#.......",
            "##....#.#.......",
            ".....##.##......"),
        [RavenFrame.Sleep] = Parse(
            "................",
            "................",
            "................",
            "................",
            "................",
            "........###.....",
            "......######....",
            ".....########...",
            "....#########...",
            "...#########....",
            "..#########.....",
            ".####.#.#.......",
            "##....#.#.......",
            ".....##.##......"),
        [RavenFrame.FlyUp] = Parse(
            "................",
            "................",
            "..##............",
            "...###..........",
            "....####........",
            ".....#####..###.",
            "......#####.#.##",
            "..##############",
            "############....",
            ".##.............",
            "................",
            "................",
            "................",
            "................"),
        [RavenFrame.FlyMid] = Parse(
            "................",
            "................",
            "................",
            "................",
            "................",
            "............###.",
            "............#.##",
            "..##############",
            "################",
            "####............",
            "................",
            "................",
            "................",
            "................"),
        [RavenFrame.FlyDown] = Parse(
            "................",
            "................",
            "................",
            "................",
            "................",
            "............###.",
            "............#.##",
            "..##############",
            "####..######....",
            "......#####.....",
            "......####......",
            ".....###........",
            ".....##.........",
            "....#..........."),
    };

    private static (int, int, int)[] Parse(params string[] rows)
    {
        var runs = new List<(int, int, int)>();
        for (int y = 0; y < rows.Length; y++)
        {
            string row = rows[y];
            int x = 0;
            while (x < row.Length)
            {
                if (row[x] != '#')
                {
                    x++;
                    continue;
                }
                int start = x;
                while (x < row.Length && row[x] == '#')
                    x++;
                runs.Add((y, start, x - start));
            }
        }
        return runs.ToArray();
    }
}
