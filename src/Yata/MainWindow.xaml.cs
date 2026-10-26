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

public partial class MainWindow : Window
{
    // Width of the concave "ears" that blend the notch into the top edge of the screen.
    private const double Ear = 10;

    private enum NotchPage { Media, Focus, Water, Tasks, Shelf }

    private static readonly Size CompactIdle = new(170, 32);
    private static readonly Size CompactIdleTimer = new(250, 32);
    private static readonly Size CompactMedia = new(260, 32);
    private static readonly Size CompactLyrics = new(380, 32);
    // Expanded heights include the 42px header with the page tabs.
    private static readonly Size ExpandedIdle = new(420, 170);
    private static readonly Size ExpandedMedia = new(520, 196);
    private static readonly Size ExpandedLyrics = new(880, 196);
    private static readonly Size ExpandedFocus = new(440, 196);
    private static readonly Size ExpandedWater = new(440, 196);
    private static readonly Size ExpandedTasks = new(440, 270);
    private static readonly Size ExpandedShelf = new(520, 250);
    private static readonly Size CompactAmbient = new(130, 32);
    private const double NewsWidth = 460;
    private const int NewsMaxItems = 4;
    private const double HudMinWidth = 340;
    private const double HudHeight = 36;
    private const double CompactRadius = 12;
    private const double ExpandedRadius = 30;
    // How much the closed notch swells under the mouse, before the hover delay opens it.
    private const double PeekWidth = 8;
    private const double PeekHeight = 3;
    private const double PrivacyGap = 8;
    private static readonly CultureInfo PtBr = new("pt-BR");

    private readonly MediaService _media;
    private readonly DispatcherTimer _lyricsTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _hudTimer = new() { Interval = TimeSpan.FromMilliseconds(1800) };
    private readonly AudioWatcher _audio;
    private readonly BrightnessWatcher _brightness;
    private readonly PowerWatcher _power;
    private readonly NetworkWatcher _network;
    private readonly KeyboardWatcher _keyboard = new();
    private readonly PrivacyWatcher _privacy;
    private readonly UpdateChecker _updates;
    private readonly AppStore _store = AppStore.Load();
    private readonly FocusTimer _focus;
    private readonly WaterTracker _water;
    private readonly TaskList _tasks;
    private readonly Shelf _shelf;
    private readonly PresenceTracker _presence;
    private readonly DispatcherTimer _newsTimer = new() { Interval = TimeSpan.FromSeconds(9) };
    private readonly RavenAnimator _ambientRaven;
    private readonly RavenAnimator _musicRaven;
    private readonly RavenAnimator _newsRaven;
    private readonly RavenAnimator _idleRaven;
    private readonly RavenAnimator _hudRaven;
    private readonly DispatcherTimer _titleReveal = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly SolidColorBrush _accent = new(ColorExtractor.Default);
    private readonly DispatcherTimer _hoverTimer = new();
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private readonly Spring _width = new(CompactIdle.Width, 0.1);
    private readonly Spring _height = new(CompactIdle.Height, 0.1);
    private readonly Spring _radius = new(CompactRadius, 0.1);
    private readonly Spring _expand = new(0, 0.002);
    // Blends the overlay (HUD or news) over the compact content.
    private readonly Spring _hud = new(0, 0.002);
    private readonly Spring _ambient = new(0, 0.002);

    private FullscreenWatcher? _fullscreen;
    private MediaState? _state;
    private bool _expanded;
    private bool _pendingExpand;
    private bool _peeking;
    private bool _animating;
    private TimeSpan _lastFrame;
    private bool _seeking;
    private Lyrics? _lyrics;
    private bool _hudActive;

    private NotchPage _page = NotchPage.Media;
    private bool _focusWasActive;
    // True while the task box is being typed in; the notch accepts focus only then.
    private bool _keyboardMode;
    private IntPtr _previousForeground;
    // Files are being dragged over the notch.
    private bool _dropHinting;

    private bool _fullscreenApp;
    private DateTime _pinnedUntil;
    private SettingsWindow? _settingsWindow;

    private bool _ambientShown;
    private bool _ambientLeaving;
    private bool _newsActive;
    private int _newsCount;
    // Which overlay the _hud spring is fading; sticky so a fade-out keeps its content.
    private bool _overlayIsNews;

    public MainWindow()
    {
        Resources["AccentBrush"] = _accent;
        InitializeComponent();

        _media = new MediaService(Dispatcher);
        _media.Changed += OnMediaChanged;

        _hoverTimer.Tick += OnHoverTimer;
        _clockTimer.Tick += (_, _) => { UpdateClock(); RefreshProgress(); CollapseIfCursorLeft(); };
        _lyricsTimer.Tick += (_, _) => UpdateLyricLine();
        _hudTimer.Tick += (_, _) => HideHud();
        _titleReveal.Tick += (_, _) => { _titleReveal.Stop(); TitleMarquee.Run(); };

        _audio = new AudioWatcher(Dispatcher);
        _brightness = new BrightnessWatcher(Dispatcher);
        _power = new PowerWatcher(Dispatcher);
        _network = new NetworkWatcher(Dispatcher);
        _audio.Message += ShowHud;
        _brightness.Message += ShowHud;
        _power.Message += ShowHud;
        _network.Message += ShowHud;
        _keyboard.Message += ShowHud;
        _privacy = new PrivacyWatcher(Dispatcher);
        _privacy.Changed += OnPrivacyChanged;
        _updates = new UpdateChecker(_store);
        _updates.Message += ShowHud;

        _focus = new FocusTimer(_store.Settings);
        _presence = new PresenceTracker(_store.Settings);
        _store.SettingsChanged += OnSettingsChanged;
        _water = new WaterTracker(_store);
        _tasks = new TaskList(_store);
        _shelf = new Shelf(_store);
        ShelfPage.Attach(_shelf);
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

        _ambientRaven = new RavenAnimator(AmbientRaven);
        _musicRaven = new RavenAnimator(MusicRaven);
        _newsRaven = new RavenAnimator(NewsRaven);
        _hudRaven = new RavenAnimator(HudRaven);
        _hudRavenLand.Tick += (_, _) => { _hudRavenLand.Stop(); _hudRaven.Land(); };
        BuildEqualizer();
        _idleRaven = new RavenAnimator(IdleRaven);
        // Hidden means a full-screen app is up: someone is watching it, not away.
        _presence.IsSurelyPresent = () => !IsVisible;
        _presence.StateChanged += UpdateCompanion;
        _presence.WentAway += OnWentAway;
        _presence.Returned += OnReturned;
        _newsTimer.Tick += (_, _) => HideNews();

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += OnClosed;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        DpiChanged += OnDpiChanged;

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
        UpdateHotkey();

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
        _privacy.Start();
        _updates.Start();
        _water.Start();
        _tasks.Start();
        _presence.Start();
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
        _hudRavenLand.Stop();
        _titleReveal.Stop();
        _audio.Dispose();
        _brightness.Dispose();
        _power.Dispose();
        _network.Dispose();
        _keyboard.Dispose();
        _privacy.Dispose();
        _updates.Stop();
        _water.Stop();
        _tasks.Stop();
        _presence.Stop();
        _newsTimer.Stop();
        _store.SaveNow();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_MOUSEACTIVATE && !_keyboardMode)
        {
            handled = true;
            return new IntPtr(NativeMethods.MA_NOACTIVATE);
        }
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            OnHotkey();
        }
        return IntPtr.Zero;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(PositionWindow);

    /// <summary>
    /// Centers the window at the top of the chosen display. Positioned in physical pixels, since
    /// WPF's Left/Top are in the primary display's units and displays can have different scales.
    /// </summary>
    private void PositionWindow()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            // Not created yet: a first guess; OnSourceInitialized places it properly.
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = 0;
            return;
        }

        var display = Monitors.Resolve(Settings.Monitor);
        double scale = NativeMethods.ScaleAt(display.X + display.Width / 2, display.Y);
        int width = (int)Math.Round(Width * scale);
        NativeMethods.MoveWindow(hwnd, display.X + (display.Width - width) / 2, display.Y);
    }

    // Moving to a display with another scale makes WPF resize the window; center it again.
    private void OnDpiChanged(object sender, DpiChangedEventArgs e) => Dispatcher.InvokeAsync(PositionWindow);

    private void OnFullscreenChanged(bool fullscreen)
    {
        _fullscreenApp = fullscreen;
        if (fullscreen && Settings.HideInFullscreen)
        {
            _hoverTimer.Stop();
            SetExpanded(false);
            Hide();
            SetEqualizer(false);
        }
        else if (!IsVisible)
        {
            Show();
            SetEqualizer(_state?.IsPlaying == true);
            ShowPendingHud();
        }
        UpdateLyricsTimer();
    }

    // ---- Settings -------------------------------------------------------------------

    private SettingsData Settings => _store.Settings;

    public void OpenSettings()
    {
        SetExpanded(false);
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(_store);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        if (_settingsWindow.WindowState == WindowState.Minimized)
            _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => OpenSettings();

    /// <summary>Everything the settings window can change, re-applied live.</summary>
    private void OnSettingsChanged()
    {
        _focus.ApplySettings();
        _water.ApplySettings();

        if (!Settings.LyricsEnabled && _lyrics != null)
        {
            _lyricsCts?.Cancel();
            SetLyrics(null);
        }
        else if (Settings.LyricsEnabled && _lyrics == null && _state != null)
        {
            LoadLyrics(_state);
        }

        ApplyAccent();
        PositionWindow();
        UpdateHotkey();
        _ = _updates.CheckIfDueAsync(); // Just switched on: look now rather than within the hour.
        OnFullscreenChanged(_fullscreenApp);
        UpdatePrivacy();
        UpdateCompanion();
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

    private void OnNotchMouseEnter(object sender, MouseEventArgs e)
    {
        _pinnedUntil = DateTime.MinValue; // The real mouse takes over from a preview.
        SetPeeking(true);
        ScheduleExpanded(true, Settings.HoverDelayMs);
    }

    /// <summary>Instant feedback that the notch noticed the mouse, while the hover delay runs.</summary>
    private void SetPeeking(bool peeking)
    {
        if (_peeking == peeking)
            return;
        _peeking = peeking;
        UpdateTargets();
    }

    /// <summary>
    /// Opens the notch on a page and keeps it open for a while without the mouse
    /// (<c>Yata.exe --show foco [segundos]</c>): for previews, screenshots and UI tests.
    /// </summary>
    public void ShowPage(string page, int seconds = 20)
    {
        var tab = page.ToLowerInvariant() switch
        {
            "foco" or "focus" => TabFocus,
            "agua" or "água" or "water" => TabWater,
            "tarefas" or "tasks" => TabTasks,
            "prateleira" or "shelf" => TabShelf,
            _ => TabMedia,
        };
        tab.IsChecked = true;
        _pinnedUntil = DateTime.Now.AddSeconds(Math.Clamp(seconds, 1, 300));
        _hoverTimer.Stop();
        SetExpanded(true);
    }

    private void OnNotchMouseLeave(object sender, MouseEventArgs e)
    {
        SetPeeking(false);
        if (!_seeking && !TasksPage.HasDraft)
            ScheduleExpanded(false, 350);
    }

    /// <summary>
    /// Safety net for missed MouseLeave events: Windows doesn't send mouse events during
    /// drag-and-drop, so after dropping on (or dragging out of) the notch it could stay open.
    /// </summary>
    private void CollapseIfCursorLeft()
    {
        if (!_expanded || _dropHinting || _seeking || _hoverTimer.IsEnabled || DateTime.Now < _pinnedUntil ||
            TasksPage.HasDraft || ShelfPage.IsDraggingOut || IsCursorOverNotch())
            return;
        ScheduleExpanded(false, 350);
    }

    private bool IsCursorOverNotch()
    {
        if (!IsVisible || !NativeMethods.GetCursorPos(out var cursor))
            return false;
        var local = NotchHost.PointFromScreen(new Point(cursor.X, cursor.Y));
        return local.X >= 0 && local.Y >= 0 && local.X <= NotchHost.ActualWidth && local.Y <= NotchHost.ActualHeight;
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
        _peeking = false;
        if (expanded)
        {
            _hudActive = false;
            _hudTimer.Stop();
            StopHudRaven();
            HideNews();
            // Wait for the page to fade in before scrolling a long title.
            _titleReveal.Stop();
            _titleReveal.Start();
        }
        else
        {
            _titleReveal.Stop();
            TitleMarquee.Stop();
            TasksPage.EndTyping();
            EndDropHint();
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
        bool ambient = _ambientShown && _state?.IsPlaying != true;
        Size compact = hasLyrics ? CompactLyrics
                     : hasMedia ? CompactMedia
                     : ambient ? CompactAmbient
                     : _focus.IsActive ? CompactIdleTimer
                     : CompactIdle;
        // The privacy dots get their own space at the left edge of the closed notch.
        int dots = !PrivacyShown ? 0 : (_privacy.Current.Camera.Count > 0 ? 1 : 0) + (_privacy.Current.Microphone.Count > 0 ? 1 : 0);
        double privacyRoom = dots == 0 ? 0 : dots * 6 + (dots - 1) * 3 + PrivacyGap;
        compact = new Size(compact.Width + privacyRoom, compact.Height);
        CompactView.Margin = new Thickness(12 + privacyRoom, 0, 12, 0);
        Size expanded = _page switch
        {
            NotchPage.Focus => ExpandedFocus,
            NotchPage.Water => ExpandedWater,
            NotchPage.Tasks => ExpandedTasks,
            NotchPage.Shelf => ExpandedShelf,
            _ => hasLyrics ? ExpandedLyrics : hasMedia ? ExpandedMedia : ExpandedIdle,
        };
        bool showNews = _newsActive && !_expanded;
        bool showHud = !showNews && _hudActive && !_expanded;
        if (showNews || showHud)
            _overlayIsNews = showNews;

        Size target = _expanded ? expanded
                    : showNews ? new Size(NewsWidth, 50 + 22 * _newsCount)
                    : showHud ? new Size(Math.Max(compact.Width, HudMinWidth), HudHeight)
                    : _peeking ? new Size(compact.Width + PeekWidth, compact.Height + PeekHeight)
                    : compact;

        _width.Target = target.Width;
        _height.Target = target.Height;
        _radius.Target = _expanded ? ExpandedRadius : showNews ? 22 : CompactRadius;
        _expand.Target = _expanded ? 1 : 0;
        _hud.Target = showHud || showNews ? 1 : 0;
        _ambient.Target = ambient ? 1 : 0;

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
        ShelfPage.Visibility = _page == NotchPage.Shelf ? Visibility.Visible : Visibility.Collapsed;

        // Yata only fidgets beside the idle clock while it can be seen.
        if (_expanded && _page == NotchPage.Media && !hasMedia)
            _idleRaven.Perch();
        else
            _idleRaven.Stop();

        FitWindow(allowShrink: false);
        StartAnimation();
    }

    /// <summary>
    /// Keeps the transparent window just big enough for the notch, its shadow and the spring's
    /// overshoot. Every animation frame repaints the whole window, so a window sized for the
    /// largest state (1040×340) cost several times more than the ~300×80 a closed notch needs.
    /// Grows before an animation starts (nothing gets clipped) and shrinks once it settles.
    /// </summary>
    private void FitWindow(bool allowShrink)
    {
        const double sideRoom = 40;   // shadow blur sideways + overshoot
        const double bottomRoom = 48; // shadow below
        const double overshoot = 1.06;

        double width = Math.Max(_width.Value, _width.Target) * overshoot + 2 * Ear + 2 * sideRoom;
        double height = Math.Max(_height.Value, _height.Target) * overshoot + bottomRoom;
        width = Math.Ceiling(width / 2) * 2; // Even, so centering doesn't shift the notch by half a pixel.
        height = Math.Ceiling(height);

        if (!allowShrink)
        {
            width = Math.Max(width, Width);
            height = Math.Max(height, Height);
        }
        if (width == Width && height == Height)
            return;

        Width = width;
        Height = height;
        PositionWindow();
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

        bool moving = _width.Step(dt) | _height.Step(dt) | _radius.Step(dt) | _expand.Step(dt) | _hud.Step(dt) |
                      _ambient.Step(dt);
        ApplyFrame();

        if (!moving)
        {
            CompositionTarget.Rendering -= OnRendering;
            _animating = false;
            FitWindow(allowShrink: true);
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
        double overlay = Math.Clamp(_hud.Value, 0, 1);
        double ambient = Math.Clamp(_ambient.Value, 0, 1);
        double underOverlay = compactOpacity * (1 - overlay);

        CompactView.Opacity = underOverlay * (1 - ambient);
        AmbientView.Opacity = underOverlay * ambient;
        // Dots stay through HUDs; the open notch names the apps in its header instead.
        PrivacyDots.Opacity = compactOpacity * (_overlayIsNews ? 1 - overlay : 1);
        HudView.Opacity = _overlayIsNews ? 0 : compactOpacity * overlay;
        NewsView.Opacity = _overlayIsNews ? compactOpacity * overlay : 0;
        foreach (var view in new UIElement[] { CompactView, AmbientView, HudView, NewsView })
            view.Visibility = view.Opacity > 0.001 ? Visibility.Visible : Visibility.Collapsed;
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

    // ---- Clock ----------------------------------------------------------------------

    private void UpdateClock()
    {
        var now = DateTime.Now;
        string time = now.ToString("HH:mm", PtBr);

        if (_state == null)
            SetCompactText(time);

        IdleClock.Text = time;
        string date = now.ToString("dddd, d 'de' MMMM", PtBr);
        HeaderDate.Text = char.ToUpper(date[0], PtBr) + date[1..];
    }
}
