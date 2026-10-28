using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Yata.Views;

/// <summary>
/// The drop shadow under a notch-shaped panel, without a DropShadowEffect. With software rendering an
/// effect blurs the whole shape again on every frame it changes, which held the notch's animations to
/// ~35 fps (tools/measure-frames.ps1). Here the blur is done once, on a small rounded rectangle, and
/// each frame only stretches its edges around the element's size: a nine-slice image.
/// Sized like the panel's body; the shadow is drawn outside those bounds.
/// </summary>
public sealed class SoftShadow : FrameworkElement
{
    // Roundness of the blurred rectangle. The notch's own radius changes as it opens (12 to 30);
    // under a blur this wide the difference doesn't show.
    private const double Corner = 20;
    // Width of the stretched middle of each edge in the source image.
    private const int Middle = 2;

    private static readonly Dictionary<(double, double), ImageSource[]> Cache = new();

    public static readonly DependencyProperty BlurRadiusProperty = DependencyProperty.Register(
        nameof(BlurRadius), typeof(double), typeof(SoftShadow),
        new FrameworkPropertyMetadata(20.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DepthProperty = DependencyProperty.Register(
        nameof(Depth), typeof(double), typeof(SoftShadow),
        new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShadowOpacityProperty = DependencyProperty.Register(
        nameof(ShadowOpacity), typeof(double), typeof(SoftShadow),
        new FrameworkPropertyMetadata(0.4, FrameworkPropertyMetadataOptions.AffectsRender));

    public SoftShadow()
    {
        IsHitTestVisible = false;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Linear);
    }

    public double BlurRadius
    {
        get => (double)GetValue(BlurRadiusProperty);
        set => SetValue(BlurRadiusProperty, value);
    }

    /// <summary>How far down the shadow falls (the light comes from above).</summary>
    public double Depth
    {
        get => (double)GetValue(DepthProperty);
        set => SetValue(DepthProperty, value);
    }

    public double ShadowOpacity
    {
        get => (double)GetValue(ShadowOpacityProperty);
        set => SetValue(ShadowOpacityProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double pad = Math.Ceiling(BlurRadius);
        var slices = Slices(pad, ShadowOpacity);
        double corner = pad + Corner;

        var outer = new Rect(-pad, -pad + Depth, RenderSize.Width + 2 * pad, RenderSize.Height + 2 * pad);
        // A panel smaller than two corners (the closed notch is 32 tall) squeezes them to fit.
        double cx = Math.Min(corner, outer.Width / 2);
        double cy = Math.Min(corner, outer.Height / 2);
        double[] xs = [outer.Left, outer.Left + cx, outer.Right - cx, outer.Right];
        double[] ys = [outer.Top, outer.Top + cy, outer.Bottom - cy, outer.Bottom];

        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                // The middle is under the panel, which is opaque.
                if (row == 1 && column == 1)
                    continue;
                double width = xs[column + 1] - xs[column];
                double height = ys[row + 1] - ys[row];
                if (width > 0 && height > 0)
                    dc.DrawImage(slices[row * 3 + column], new Rect(xs[column], ys[row], width, height));
            }
        }
    }

    /// <summary>The blurred rounded rectangle, cut in nine; made once per blur and opacity.</summary>
    private static ImageSource[] Slices(double pad, double opacity)
    {
        if (Cache.TryGetValue((pad, opacity), out var cached))
            return cached;

        int corner = (int)(pad + Corner);
        int size = 2 * corner + Middle;
        var visual = new DrawingVisual
        {
            Effect = new BlurEffect { Radius = pad, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality },
        };
        using (var dc = visual.RenderOpen())
        {
            // Transparent corners keep the bitmap's bounds fixed, so the blur isn't cropped.
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, size, size));
            var black = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * opacity), 0, 0, 0));
            dc.DrawRoundedRectangle(black, null, new Rect(pad, pad, size - 2 * pad, size - 2 * pad), Corner, Corner);
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        int[] starts = [0, corner, corner + Middle];
        int[] lengths = [corner, Middle, corner];
        var slices = new ImageSource[9];
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                var slice = new CroppedBitmap(bitmap, new Int32Rect(starts[column], starts[row], lengths[column], lengths[row]));
                slice.Freeze();
                slices[row * 3 + column] = slice;
            }
        }
        Cache[(pad, opacity)] = slices;
        return slices;
    }
}
