using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Notch.Helpers;
using Notch.Interop;
using Notch.Services;
using MediaState = Notch.Services.MediaState;

namespace Notch;

public partial class MainWindow : Window
{
    // Width of the concave "ears" that blend the notch into the top edge of the screen.
    private const double Ear = 10;

    private enum NotchPage { Media, Focus, Water, Tasks }

    private static readonly Size CompactIdle = new(170, 32);
    private static readonly Size CompactIdleTimer = new(250, 32);
    private static readonly Size CompactMedia = new(260, 32);
    private static readonly Size CompactLyrics = new(380, 32);
    // Expanded heights include the 42px header with the page tabs.
    private static readonly Size ExpandedIdle = new(380, 170);
    private static readonly Size ExpandedMedia = new(520, 196);
    private static readonly Size ExpandedLyrics = new(880, 196);
    private static readonly Size ExpandedFocus = new(440, 196);
    private static readonly Size ExpandedWater = new(440, 196);
    private static readonly Size ExpandedTasks = new(440, 270);
    private const double HudMinWidth = 340;
    private const double HudHeight = 36;
    private const double CompactRadius = 12;
    private const double ExpandedRadius = 30;

    private static readonly int[] EqualizerPeriods = { 420, 560, 360, 500 };
    private static readonly CultureInfo PtBr = new("pt-BR");

    // Highlight a line slightly early so it doesn't feel late against the audio.
    private static readonly TimeSpan LyricLead = TimeSpan.FromMilliseconds(200);
    private static readonly Brush LyricDim = Freeze(new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush WarningBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)));

    private readonly MediaService _media;
    private readonly LyricsService _lyricsService = new();
    private readonly DispatcherTimer _lyricsTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _hudTimer = new() { Interval = TimeSpan.FromMilliseconds(1800) };
    private readonly AudioWatcher _audio;
    private readonly BrightnessWatcher _brightness;
    private readonly PowerWatcher _power;
    private readonly NetworkWatcher _network;
    private readonly KeyboardWatcher _keyboard = new();
    private readonly AppStore _store = AppStore.Load();
    private readonly FocusTimer _focus = new();
    private readonly WaterTracker _water;
    private readonly TaskList _tasks;
    private readonly SolidColorBrush _accent = new(ColorExtractor.Default);
    private readonly DispatcherTimer _hoverTimer = new();
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private readonly Spring _width = new(CompactIdle.Width, 0.1);
    private readonly Spring _height = new(CompactIdle.Height, 0.1);
    private readonly Spring _radius = new(CompactRadius, 0.1);
    private readonly Spring _expand = new(0, 0.002);
    private readonly Spring _hud = new(0, 0.002);

    private FullscreenWatcher? _fullscreen;
    private MediaState? _state;
    private bool _expanded;
    private bool _pendingExpand;
    private bool _animating;
    private TimeSpan _lastFrame;
    private bool _equalizerRunning;
    private bool _seeking;
    private double _seekFraction;

    private Lyrics? _lyrics;
    private string? _lyricsTrack;
    private CancellationTokenSource? _lyricsCts;
    private int _lyricIndex = -1;

    private bool _hudActive;
    private HudMessage? _pendingHud;

    private NotchPage _page = NotchPage.Media;
    private bool _focusWasActive;
    // True while the task box is being typed in; the notch accepts focus only then.
    private bool _keyboardMode;
    private IntPtr _previousForeground;

    public MainWindow()
    {
        Resources["AccentBrush"] = _accent;
        InitializeComponent();

        _media = new MediaService(Dispatcher);
        _media.Changed += OnMediaChanged;

        _hoverTimer.Tick += OnHoverTimer;
        _clockTimer.Tick += (_, _) => { UpdateClock(); RefreshProgress(); };
        _lyricsTimer.Tick += (_, _) => UpdateLyricLine();
        _hudTimer.Tick += (_, _) => HideHud();

        _audio = new AudioWatcher(Dispatcher);
        _brightness = new BrightnessWatcher(Dispatcher);
        _power = new PowerWatcher(Dispatcher);
        _network = new NetworkWatcher(Dispatcher);
        _audio.Message += ShowHud;
        _brightness.Message += ShowHud;
        _power.Message += ShowHud;
        _network.Message += ShowHud;
        _keyboard.Message += ShowHud;

        _water = new WaterTracker(_store);
        _tasks = new TaskList(_store);
        _focus.Message += ShowHud;
        _water.Message += ShowHud;
        _tasks.Message += ShowHud;
        _focus.Changed += UpdateCompactTimer;
        FocusPage.Attach(_focus);
        WaterPage.Attach(_water);
        TasksPage.Attach(_tasks);
        TasksPage.KeyboardRequested += EnterKeyboardMode;
        TasksPage.KeyboardReleased += ExitKeyboardMode;
        Deactivated += OnDeactivated;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += OnClosed;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        UpdateClock();
        UpdateTargets();
        ApplyFrame();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        // Tool window: hidden from Alt+Tab. No-activate: clicking never steals focus.
        NativeMethods.AddExtendedStyle(hwnd, NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);

        PositionWindow();

        _fullscreen = new FullscreenWatcher(hwnd);
        _fullscreen.Changed += OnFullscreenChanged;
        _fullscreen.Start();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _clockTimer.Start();
        _audio.Start();
        _brightness.Start();
        _power.Start();
        _network.Start();
        _keyboard.Start();
        _water.Start();
        _tasks.Start();
        try
        {
            await _media.InitializeAsync();
        }
        catch (Exception)
        {
            // Media sessions unavailable (e.g. stripped-down Windows); the notch still works as a clock.
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _fullscreen?.Dispose();
        _clockTimer.Stop();
        _hoverTimer.Stop();
        _lyricsTimer.Stop();
        _lyricsCts?.Cancel();
        _hudTimer.Stop();
        _audio.Dispose();
        _brightness.Dispose();
        _power.Dispose();
        _network.Dispose();
        _keyboard.Dispose();
        _water.Stop();
        _tasks.Stop();
        _store.SaveNow();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_MOUSEACTIVATE && !_keyboardMode)
        {
            handled = true;
            return new IntPtr(NativeMethods.MA_NOACTIVATE);
        }
        return IntPtr.Zero;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(PositionWindow);

    private void PositionWindow()
    {
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 0;
    }

    private void OnFullscreenChanged(bool fullscreen)
    {
        if (fullscreen)
        {
            _hoverTimer.Stop();
            SetExpanded(false);
            Hide();
            SetEqualizer(false);
        }
        else
        {
            Show();
            SetEqualizer(_state?.IsPlaying == true);
            ShowPendingHud();
        }
        UpdateLyricsTimer();
    }

    // ---- Keyboard (task input) ------------------------------------------------------

    private void EnterKeyboardMode()
    {
        if (_keyboardMode)
            return;

        _keyboardMode = true;
        _previousForeground = NativeMethods.GetForegroundWindow();
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.RemoveExtendedStyle(hwnd, NativeMethods.WS_EX_NOACTIVATE);
        Activate();
    }

    private void ExitKeyboardMode()
    {
        if (!_keyboardMode)
            return;

        _keyboardMode = false;
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.AddExtendedStyle(hwnd, NativeMethods.WS_EX_NOACTIVATE);

        // Done typing but still the active window: hand the keyboard back to where the user was.
        if (IsActive && _previousForeground != IntPtr.Zero)
            NativeMethods.SetForegroundWindow(_previousForeground);
        _previousForeground = IntPtr.Zero;
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // Clicked elsewhere while typing: give focus back and close like a normal mouse-out.
        TasksPage.EndTyping();
        ExitKeyboardMode();
        if (!NotchHost.IsMouseOver)
            ScheduleExpanded(false, 350);
    }

    // ---- Hover / expand -------------------------------------------------------------

    private void OnNotchMouseEnter(object sender, MouseEventArgs e) => ScheduleExpanded(true, 140);

    private void OnNotchMouseLeave(object sender, MouseEventArgs e)
    {
        if (!_seeking && !TasksPage.HasDraft)
            ScheduleExpanded(false, 350);
    }

    private void OnTabChecked(object sender, RoutedEventArgs e)
    {
        if (TasksPage == null)
            return; // Fired by the default tab while the XAML is still loading.

        _page = sender == TabFocus ? NotchPage.Focus
              : sender == TabWater ? NotchPage.Water
              : sender == TabTasks ? NotchPage.Tasks
              : NotchPage.Media;
        TasksPage.EndTyping();
        UpdateTargets();
    }

    private void OnNotchClick(object sender, MouseButtonEventArgs e)
    {
        if (!_expanded)
        {
            _hoverTimer.Stop();
            SetExpanded(true);
        }
    }

    private void ScheduleExpanded(bool expanded, int delayMs)
    {
        _pendingExpand = expanded;
        _hoverTimer.Stop();
        _hoverTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
        _hoverTimer.Start();
    }

    private void OnHoverTimer(object? sender, EventArgs e)
    {
        _hoverTimer.Stop();
        SetExpanded(_pendingExpand);
    }

    private void SetExpanded(bool expanded)
    {
        if (_expanded == expanded)
            return;

        _expanded = expanded;
        if (expanded)
        {
            _hudActive = false;
            _hudTimer.Stop();
        }
        else
        {
            TasksPage.EndTyping();
        }
        _clockTimer.Interval = TimeSpan.FromMilliseconds(expanded ? 250 : 1000);
        UpdateTargets();
        RefreshProgress();
        if (!expanded)
            ShowPendingHud();
    }

    // ---- Animation ------------------------------------------------------------------

    private void UpdateTargets()
    {
        bool hasMedia = _state != null;
        bool hasLyrics = hasMedia && _lyrics != null;
        Size compact = hasLyrics ? CompactLyrics
                     : hasMedia ? CompactMedia
                     : _focus.IsActive ? CompactIdleTimer
                     : CompactIdle;
        Size expanded = _page switch
        {
            NotchPage.Focus => ExpandedFocus,
            NotchPage.Water => ExpandedWater,
            NotchPage.Tasks => ExpandedTasks,
            _ => hasLyrics ? ExpandedLyrics : hasMedia ? ExpandedMedia : ExpandedIdle,
        };
        bool showHud = _hudActive && !_expanded;
        Size target = _expanded ? expanded
                    : showHud ? new Size(Math.Max(compact.Width, HudMinWidth), HudHeight)
                    : compact;

        _width.Target = target.Width;
        _height.Target = target.Height;
        _radius.Target = _expanded ? ExpandedRadius : CompactRadius;
        _expand.Target = _expanded ? 1 : 0;
        _hud.Target = showHud ? 1 : 0;

        ExpandedView.Width = expanded.Width;
        ExpandedView.Height = expanded.Height;
        MediaPanel.Visibility = hasMedia ? Visibility.Visible : Visibility.Collapsed;
        IdlePanel.Visibility = hasMedia ? Visibility.Collapsed : Visibility.Visible;
        LyricsPanel.Visibility = hasLyrics ? Visibility.Visible : Visibility.Collapsed;
        LyricsDivider.Visibility = LyricsPanel.Visibility;

        MediaPage.Visibility = _page == NotchPage.Media ? Visibility.Visible : Visibility.Collapsed;
        FocusPage.Visibility = _page == NotchPage.Focus ? Visibility.Visible : Visibility.Collapsed;
        WaterPage.Visibility = _page == NotchPage.Water ? Visibility.Visible : Visibility.Collapsed;
        TasksPage.Visibility = _page == NotchPage.Tasks ? Visibility.Visible : Visibility.Collapsed;

        StartAnimation();
    }

    private void StartAnimation()
    {
        if (_animating)
            return;

        _animating = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (_lastFrame == TimeSpan.Zero)
        {
            _lastFrame = now;
            return;
        }

        double dt = Math.Min((now - _lastFrame).TotalSeconds, 0.05);
        if (dt <= 0)
            return; // Rendering can fire more than once per frame.
        _lastFrame = now;

        bool moving = _width.Step(dt) | _height.Step(dt) | _radius.Step(dt) | _expand.Step(dt) | _hud.Step(dt);
        ApplyFrame();

        if (!moving)
        {
            CompositionTarget.Rendering -= OnRendering;
            _animating = false;
        }
    }

    private void ApplyFrame()
    {
        double width = Math.Max(_width.Value, 40);
        double height = Math.Max(_height.Value, 16);
        double radius = Math.Clamp(_radius.Value, 0, Math.Min(width, height) / 2);

        NotchShape.Data = BuildNotchGeometry(width, height, radius);
        ContentHost.Width = width;
        ContentHost.Height = height;

        double progress = _expand.Value;
        double compactOpacity = Math.Clamp(1 - progress * 3, 0, 1);
        double expandedOpacity = Math.Clamp((progress - 0.35) / 0.65, 0, 1);
        double hud = Math.Clamp(_hud.Value, 0, 1);

        CompactView.Opacity = compactOpacity * (1 - hud);
        CompactView.Visibility = CompactView.Opacity > 0.001 ? Visibility.Visible : Visibility.Collapsed;
        HudView.Opacity = compactOpacity * hud;
        HudView.Visibility = HudView.Opacity > 0.001 ? Visibility.Visible : Visibility.Collapsed;
        ExpandedView.Opacity = expandedOpacity;
        ExpandedView.Visibility = expandedOpacity > 0.001 ? Visibility.Visible : Visibility.Collapsed;
        ExpandedView.IsHitTestVisible = progress > 0.7;
    }

    /// <summary>Flat top flush with the screen edge, concave ears, rounded bottom corners.</summary>
    private static Geometry BuildNotchGeometry(double width, double height, double radius)
    {
        const double e = Ear;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(0, 0), true, true);
            ctx.LineTo(new Point(width + 2 * e, 0), false, false);
            ctx.ArcTo(new Point(width + e, e), new Size(e, e), 0, false, SweepDirection.Counterclockwise, false, false);
            ctx.LineTo(new Point(width + e, height - radius), false, false);
            ctx.ArcTo(new Point(width + e - radius, height), new Size(radius, radius), 0, false, SweepDirection.Clockwise, false, false);
            ctx.LineTo(new Point(e + radius, height), false, false);
            ctx.ArcTo(new Point(e, height - radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise, false, false);
            ctx.LineTo(new Point(e, e), false, false);
            ctx.ArcTo(new Point(0, 0), new Size(e, e), 0, false, SweepDirection.Counterclockwise, false, false);
        }
        geometry.Freeze();
        return geometry;
    }

    // ---- Media ----------------------------------------------------------------------

    private void OnMediaChanged()
    {
        var previous = _state;
        _state = _media.Current;

        if (_state != null)
        {
            TitleText.Text = _state.Title;
            ArtistText.Text = _state.Artist;
            ArtistText.Visibility = string.IsNullOrEmpty(_state.Artist) ? Visibility.Collapsed : Visibility.Visible;
            PlayPauseButton.Content = _state.IsPlaying ? "" : "";

            if (previous == null || !ReferenceEquals(previous.Thumbnail, _state.Thumbnail))
                SetArtwork(_state.Thumbnail);
        }
        else
        {
            SetArtwork(null);
        }

        UpdateCompactTimer();
        SetEqualizer(_state?.IsPlaying == true && IsVisible);

        string? track = _state == null ? null : $"{_state.SourceApp}\n{_state.Artist}\n{_state.Title}";
        if (track != _lyricsTrack)
        {
            _lyricsTrack = track;
            LoadLyrics(_state);
        }

        UpdateClock();
        UpdateTargets();
        RefreshProgress();
        UpdateLyricsTimer();
        UpdateLyricLine();
        UpdateCompactText();
    }

    private void UpdateCompactText()
    {
        if (_state == null)
            return; // The clock owns the compact text.

        string? line = _lyrics != null && _lyricIndex >= 0 ? _lyrics.Lines[_lyricIndex].Text : null;
        CompactText.Text = string.IsNullOrWhiteSpace(line) ? _state.Title : line;
    }

    /// <summary>While a focus session is active its countdown takes the equalizer's slot.</summary>
    private void UpdateCompactTimer()
    {
        bool active = _focus.IsActive;
        CompactTimer.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        Equalizer.Visibility = _state != null && !active ? Visibility.Visible : Visibility.Collapsed;

        var remaining = _focus.Remaining;
        CompactTimerText.Text = $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";

        if (active != _focusWasActive)
        {
            _focusWasActive = active;
            UpdateTargets(); // The idle notch widens to fit the countdown.
        }
    }

    private void SetArtwork(BitmapImage? image)
    {
        CompactArtBrush.ImageSource = image;
        ExpandedArtBrush.ImageSource = image;
        CompactArt.Visibility = image != null ? Visibility.Visible : Visibility.Collapsed;

        var accent = image != null ? ColorExtractor.FromImage(image) : ColorExtractor.Default;
        _accent.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(accent, TimeSpan.FromMilliseconds(500)));
    }

    private void SetEqualizer(bool running)
    {
        if (running == _equalizerRunning)
            return;
        _equalizerRunning = running;

        for (int i = 0; i < Equalizer.Children.Count; i++)
        {
            var scale = (ScaleTransform)((Rectangle)Equalizer.Children[i]).RenderTransform;
            if (running)
            {
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(EqualizerPeriods[i]))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                });
            }
            else
            {
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleY = 0.3;
            }
        }
    }

    private void OnPlayPause(object sender, RoutedEventArgs e) => _ = _media.TogglePlayPauseAsync();

    private void OnNext(object sender, RoutedEventArgs e) => _ = _media.NextAsync();

    private void OnPrevious(object sender, RoutedEventArgs e) => _ = _media.PreviousAsync();

    // ---- HUD ------------------------------------------------------------------------

    private void ShowHud(HudMessage message)
    {
        if (!IsVisible || _expanded)
        {
            // Reminders wait until the notch closes or a full-screen app exits; status blips are dropped.
            if (message.Important)
                _pendingHud = message;
            return;
        }

        _hudTimer.Interval = TimeSpan.FromMilliseconds(message.DurationMs);

        HudIcon.Text = message.Glyph;
        HudIcon.Foreground = message.Warning ? WarningBrush : Brushes.White;
        HudText.Text = message.Text;
        HudValue.Text = message.Value ?? "";
        HudValue.Visibility = message.Value == null ? Visibility.Collapsed : Visibility.Visible;
        HudBar.Visibility = message.Level.HasValue ? Visibility.Visible : Visibility.Collapsed;
        if (message.Level is double level)
        {
            HudBarFill.BeginAnimation(WidthProperty,
                new DoubleAnimation(Math.Clamp(level, 0, 1) * HudBar.Width, TimeSpan.FromMilliseconds(120)));
        }

        _hudActive = true;
        _hudTimer.Stop();
        _hudTimer.Start();
        UpdateTargets();
    }

    private void ShowPendingHud()
    {
        if (_pendingHud is not HudMessage message)
            return;
        _pendingHud = null;
        ShowHud(message);
    }

    private void HideHud()
    {
        _hudTimer.Stop();
        _hudActive = false;
        UpdateTargets();
    }

    // ---- Lyrics ---------------------------------------------------------------------

    private async void LoadLyrics(MediaState? media)
    {
        _lyricsCts?.Cancel();
        SetLyrics(null);
        if (media == null)
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
                    Margin = new Thickness(0, 4, 0, 4),
                    Foreground = LyricDim,
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
            ((TextBlock)LyricsLines.Children[_lyricIndex]).Foreground = LyricDim;
        if (index >= 0)
            ((TextBlock)LyricsLines.Children[index]).Foreground = Brushes.White;

        _lyricIndex = index;
        ScrollLyrics(animate: true);
        UpdateCompactText();
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

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    // ---- Progress / seeking ---------------------------------------------------------

    private void RefreshProgress()
    {
        if (_state == null || _seeking)
            return;

        if (_state.Duration <= TimeSpan.Zero)
        {
            ProgressRow.Visibility = Visibility.Hidden;
            return;
        }

        ProgressRow.Visibility = Visibility.Visible;
        var position = _state.CurrentPosition;
        SetProgress(position.TotalSeconds / _state.Duration.TotalSeconds, position, _state.Duration);
    }

    private void SetProgress(double fraction, TimeSpan position, TimeSpan duration)
    {
        ProgressFill.Width = Math.Clamp(fraction, 0, 1) * ProgressTrack.ActualWidth;
        ElapsedText.Text = FormatTime(position);
        DurationText.Text = FormatTime(duration);
    }

    private void OnTrackMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_state == null || _state.Duration <= TimeSpan.Zero)
            return;

        _seeking = true;
        ProgressTrack.CaptureMouse();
        PreviewSeek(e.GetPosition(ProgressTrack).X);
        e.Handled = true;
    }

    private void OnTrackMouseMove(object sender, MouseEventArgs e)
    {
        if (_seeking)
            PreviewSeek(e.GetPosition(ProgressTrack).X);
    }

    private void OnTrackMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_seeking || _state == null)
            return;

        var target = TimeSpan.FromTicks((long)(_state.Duration.Ticks * _seekFraction));
        _seeking = false;
        ProgressTrack.ReleaseMouseCapture();
        _ = _media.SeekAsync(target);
        e.Handled = true;

        if (!NotchHost.IsMouseOver)
            ScheduleExpanded(false, 350);
    }

    private void OnTrackLostCapture(object sender, MouseEventArgs e)
    {
        if (_seeking)
        {
            _seeking = false;
            RefreshProgress();
        }
    }

    private void OnTrackSizeChanged(object sender, SizeChangedEventArgs e) => RefreshProgress();

    private void PreviewSeek(double x)
    {
        if (_state == null)
            return;

        _seekFraction = Math.Clamp(x / Math.Max(ProgressTrack.ActualWidth, 1), 0, 1);
        SetProgress(_seekFraction, TimeSpan.FromTicks((long)(_state.Duration.Ticks * _seekFraction)), _state.Duration);
    }

    private static string FormatTime(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");

    // ---- Clock ----------------------------------------------------------------------

    private void UpdateClock()
    {
        var now = DateTime.Now;
        string time = now.ToString("HH:mm", PtBr);

        if (_state == null)
            CompactText.Text = time;

        IdleClock.Text = time;
        string date = now.ToString("dddd, d 'de' MMMM", PtBr);
        HeaderDate.Text = char.ToUpper(date[0], PtBr) + date[1..];
    }
}
