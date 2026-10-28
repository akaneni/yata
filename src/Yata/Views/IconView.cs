using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Yata.Services;

namespace Yata.Views;

/// <summary>
/// An icon in the notch: a Segoe Fluent glyph, or an agent's mark (<see cref="BrandMarks"/>), with
/// an optional badge in its lower corner. The icon says where something comes from, the badge what
/// happened to it (done, failed, needs you), so "Claude Code is done" keeps Claude's mark. A mark
/// keeps its own colors; the foreground (the warning color when it needs you) is the glyph's and the badge's.
/// </summary>
public sealed class IconView : Grid
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(IconView), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty BadgeProperty = DependencyProperty.Register(
        nameof(Badge), typeof(string), typeof(IconView), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(IconView), new PropertyMetadata(15.0, OnChanged));

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(IconView), new PropertyMetadata(Brushes.White, OnChanged));

    private readonly TextBlock _glyph = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Image _mark = new() { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _badgeGlyph = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _badge = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };

    public IconView()
    {
        _glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        _badgeGlyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        // The notch's black around the badge cuts it out of the icon, so the two don't run together.
        _badge.SetResourceReference(Border.BackgroundProperty, "NotchBrush");
        _badge.Child = _badgeGlyph;
        Children.Add(_glyph);
        Children.Add(_mark);
        Children.Add(_badge);
        Update();
    }

    public string? Glyph
    {
        get => (string?)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>A glyph drawn small in the corner, or null for none.</summary>
    public string? Badge
    {
        get => (string?)GetValue(BadgeProperty);
        set => SetValue(BadgeProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((IconView)d).Update();

    private void Update()
    {
        double size = Size;
        Width = Height = size;
        var mark = BrandMarks.Image(Glyph);
        _mark.Source = mark;
        // A mark fills its whole box, a glyph leaves some air: a little smaller reads the same size.
        _mark.Width = _mark.Height = size * 0.9;
        _mark.Visibility = mark != null ? Visibility.Visible : Visibility.Collapsed;
        _glyph.Text = mark == null ? Glyph ?? "" : "";
        _glyph.FontSize = size;
        _glyph.Foreground = Foreground;
        _glyph.Visibility = mark == null ? Visibility.Visible : Visibility.Collapsed;

        double badge = System.Math.Round(size * 0.75);
        _badge.Width = _badge.Height = badge;
        _badge.CornerRadius = new CornerRadius(badge / 2);
        _badge.Margin = new Thickness(0, 0, -badge * 0.45, -badge * 0.35);
        _badgeGlyph.Text = Badge ?? "";
        _badgeGlyph.FontSize = System.Math.Round(badge * 0.8);
        _badgeGlyph.Foreground = Foreground;
        _badge.Visibility = string.IsNullOrEmpty(Badge) ? Visibility.Collapsed : Visibility.Visible;
    }
}
