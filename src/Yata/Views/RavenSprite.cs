using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Yata.Views;

public enum RavenFrame
{
    Perch, Blink, Nod, Preen, Sleep, FlyUp, FlyMid, FlyDown,
    Caw, LookUp, Crouch, Hop, Stretch, Fluff, FluffSplash, SleepIn,
}

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

    /// <summary>Draws a small envelope at Yata's feet (or under him in flight): he brought news.</summary>
    public static readonly DependencyProperty LetterProperty = DependencyProperty.Register(
        nameof(Letter), typeof(bool), typeof(RavenSprite),
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

    public bool Letter
    {
        get => (bool)GetValue(LetterProperty);
        set => SetValue(LetterProperty, value);
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
        Draw(dc, Runs[Frame], px);
        if (Letter)
            Draw(dc, Envelope, px);
    }

    private void Draw(DrawingContext dc, (int Row, int Start, int Length)[] runs, double px)
    {
        foreach (var (row, start, length) in runs)
        {
            int x = FacingLeft ? Columns - start - length : start;
            dc.DrawRectangle(Fill, null, new Rect(x * px, row * px, length * px, px));
        }
    }

    // For tests: whether a pixel is lit, before mirroring.
    internal static bool IsLit(RavenFrame frame, int column, int row) => IsLit(Runs[frame], column, row);

    internal static bool IsLetterLit(int column, int row) => IsLit(Envelope, column, row);

    internal static bool HasFrame(RavenFrame frame) => Runs.ContainsKey(frame);

    private static bool IsLit((int Row, int Start, int Length)[] runs, int column, int row) =>
        runs.Any(r => r.Row == row && column >= r.Start && column < r.Start + r.Length);

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
        [RavenFrame.Caw] = Parse(
            "................",
            ".........####...",
            "........######.#",
            "........##.####.",
            "........####....",
            ".......########.",
            "......######....",
            ".....#######....",
            "....########....",
            "...########.....",
            "..#######.......",
            ".####.#.#.......",
            "##....#.#.......",
            ".....##.##......"),
        [RavenFrame.LookUp] = Parse(
            ".............##.",
            ".........#####..",
            "........#####...",
            "........#.###...",
            "........#####...",
            ".......#####....",
            "......######....",
            ".....#######....",
            "....########....",
            "...########.....",
            "..#######.......",
            ".####.#.#.......",
            "##....#.#.......",
            ".....##.##......"),
        // Knees bent: before a hop and on landing.
        [RavenFrame.Crouch] = Parse(
            "................",
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
            ".########.......",
            "##....##........",
            ".....##.##......"),
        [RavenFrame.Hop] = Parse(
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
            ".....#..#.......",
            "................"),
        [RavenFrame.Stretch] = Parse(
            ".#.#............",
            ".####....####...",
            "..####..######..",
            "...####.##.####.",
            "....###.########",
            ".....#######....",
            "......######....",
            ".....#######....",
            "....########....",
            "...########.....",
            "..#######.......",
            ".####.#.#.......",
            "##....#.#.......",
            ".....##.##......"),
        // Feathers puffed out while shaking off water; the two differ only in the drops.
        [RavenFrame.Fluff] = Parse(
            "...#............",
            ".........####...",
            ".......#######..",
            ".......##.####..",
            "......##########",
            "...#.#######....",
            "....#########...",
            "..###########...",
            "...##########...",
            ".###########....",
            "..#########.....",
            ".####.#.#.......",
            "##....#.#.....#.",
            ".....##.##......"),
        [RavenFrame.FluffSplash] = Parse(
            "............#...",
            ".#.......####...",
            ".......#######..",
            ".......##.####..",
            "......##########",
            "...#.#######...#",
            "....#########...",
            "..###########...",
            "...##########...",
            ".###########....",
            "..#########.....",
            ".####.#.#.......",
            "##....#.#.......",
            "#....##.##......"),
        // Asleep, breathing in: the back rises a pixel.
        [RavenFrame.SleepIn] = Parse(
            "................",
            "................",
            "................",
            "................",
            "........###.....",
            "......######....",
            ".....########...",
            "....#########...",
            "....#########...",
            "...#########....",
            "..#########.....",
            ".####.#.#.......",
            "##....#.#.......",
            ".....##.##......"),
    };

    // 5x4 envelope in the bottom-right corner: clear of the feet when perched and of the
    // wings in flight, so one spot works for every frame.
    private static readonly (int Row, int Start, int Length)[] Envelope = Parse(
        "................",
        "................",
        "................",
        "................",
        "................",
        "................",
        "................",
        "................",
        "................",
        "................",
        "...........#####",
        "...........##.##",
        "...........#.#.#",
        "...........#####");

    private static (int, int, int)[] Parse(params string[] rows)
    {
        // A mistyped row would silently shift pixels; fail loudly instead (the tests load every frame).
        if (rows.Length != Rows || rows.Any(r => r.Length != Columns))
            throw new ArgumentException($"Each frame must be {Columns}x{Rows} pixels.", nameof(rows));

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
