using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Yata.Helpers;
using Yata.Interop;
using Yata.Services;
using Yata.Views;
using Glyphs = Yata.Services.Glyphs;
using MediaState = Yata.Services.MediaState;

namespace Yata;

// Synced lyrics: list, highlight, scrolling.
public partial class MainWindow
{
    // Highlight a line slightly early so it doesn't feel late against the audio.
    private static readonly TimeSpan LyricLead = TimeSpan.FromMilliseconds(200);
    private static readonly Color LyricDimColor = Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF);
    private const double LyricZoom = 1.04;

    private readonly LyricsService _lyricsService = new();
    private string? _lyricsTrack;
    private CancellationTokenSource? _lyricsCts;
    private int _lyricIndex = -1;

    private async void LoadLyrics(MediaState? media)
    {
        _lyricsCts?.Cancel();
        SetLyrics(null);
        if (media == null || !Settings.LyricsEnabled)
            return;

        var cts = _lyricsCts = new CancellationTokenSource();
        try
        {
            // Players send the new title before the new duration; wait for the timeline to settle
            // so the duration match doesn't use the previous track's length.
            await Task.Delay(700, cts.Token);
            var lyrics = await _lyricsService.FindAsync(_state ?? media, cts.Token);
            if (!cts.IsCancellationRequested)
                SetLyrics(lyrics);
        }
        catch (Exception)
        {
            // Cancelled by a track change, or lyrics unavailable; the player works without them.
        }
    }

    private void SetLyrics(Lyrics? lyrics)
    {
        _lyrics = lyrics;
        _lyricIndex = -1;
        LyricsLines.Children.Clear();
        LyricsScroll.BeginAnimation(TranslateTransform.YProperty, null);
        LyricsScroll.Y = 0;

        if (lyrics != null)
        {
            for (int i = 0; i < lyrics.Lines.Count; i++)
            {
                var text = lyrics.Lines[i].Text;
                var line = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(text) ? "♪" : text,
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    // Right margin leaves room for the current line's slight zoom.
                    Margin = new Thickness(0, 4, 14, 4),
                    Foreground = new SolidColorBrush(LyricDimColor),
                    RenderTransformOrigin = new Point(0, 0.5),
                    RenderTransform = new ScaleTransform(),
                    Cursor = Cursors.Hand,
                    Tag = i,
                };
                line.MouseLeftButtonUp += OnLyricClick;
                LyricsLines.Children.Add(line);
            }
        }

        UpdateTargets();
        UpdateLyricsTimer();
        UpdateLyricLine();
        UpdateCompactText();
    }

    private void UpdateLyricsTimer()
    {
        bool run = _lyrics != null && _state?.IsPlaying == true && IsVisible;
        if (run)
            _lyricsTimer.Start();
        else
            _lyricsTimer.Stop();
    }

    private void UpdateLyricLine()
    {
        if (_lyrics == null || _state == null)
            return;

        int index = _lyrics.IndexAt(_state.CurrentPosition + LyricLead);
        if (index == _lyricIndex)
            return;

        if (_lyricIndex >= 0 && _lyricIndex < LyricsLines.Children.Count)
            HighlightLyric((TextBlock)LyricsLines.Children[_lyricIndex], false);
        if (index >= 0)
            HighlightLyric((TextBlock)LyricsLines.Children[index], true);

        _lyricIndex = index;
        ScrollLyrics(animate: true);
        UpdateCompactText();
    }

    /// <summary>The current line brightens and grows a little; eased only while the lyrics are on screen.</summary>
    private void HighlightLyric(TextBlock line, bool current)
    {
        var brush = (SolidColorBrush)line.Foreground;
        var scale = (ScaleTransform)line.RenderTransform;
        Color color = current ? Colors.White : LyricDimColor;
        double size = current ? LyricZoom : 1;

        if (ExpandedView.Visibility != Visibility.Visible)
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            brush.Color = color;
            scale.ScaleX = scale.ScaleY = size;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(320);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(color, duration) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(size, duration) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(size, duration) { EasingFunction = ease });
    }

    private void ScrollLyrics(bool animate)
    {
        if (LyricsLines.Children.Count == 0 || LyricsPanel.ActualHeight <= 0)
            return;

        var line = (FrameworkElement)LyricsLines.Children[Math.Max(_lyricIndex, 0)];
        if (line.ActualHeight <= 0)
            return;

        double center = line.TranslatePoint(new Point(0, 0), LyricsLines).Y + line.ActualHeight / 2;
        double target = LyricsPanel.ActualHeight / 2 - center;

        if (animate)
        {
            LyricsScroll.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(450))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        }
        else
        {
            LyricsScroll.BeginAnimation(TranslateTransform.YProperty, null);
            LyricsScroll.Y = target;
        }
    }

    private void OnLyricsLayoutChanged(object sender, SizeChangedEventArgs e) => ScrollLyrics(animate: false);

    private void OnLyricClick(object sender, MouseButtonEventArgs e)
    {
        if (_lyrics == null || sender is not TextBlock { Tag: int index })
            return;

        _ = _media.SeekAsync(_lyrics.Lines[index].Time);
        e.Handled = true;
    }
}
