using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
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
using MediaState = Yata.Services.MediaState;

namespace Yata;

public partial class MainWindow : Window
{
    // Width of the concave "ears" that blend the notch into the top edge of the screen.
    private const double Ear = 10;

    private enum NotchPage { Media, Agenda, Focus, Water, Tasks, Shelf }

    private static readonly Size CompactIdle = new(170, 32);
    private static readonly Size CompactIdleTimer = new(250, 32);
    private static readonly Size CompactMedia = new(260, 32);
    private static readonly Size CompactLyrics = new(380, 32);
    // The next calendar event takes the clock's place in the minutes before it.
    private static readonly Size CompactEvent = new(300, 32);
    private static readonly Size CompactEventTimer = new(380, 32);
    // Expanded heights include the 42px header with the page tabs.
    private static readonly Size ExpandedIdle = new(420, 170);
    private static readonly Size ExpandedMedia = new(520, 196);
    private static readonly Size ExpandedLyrics = new(880, 196);
    private static readonly Size ExpandedFocus = new(440, 196);
    private static readonly Size ExpandedWater = new(440, 196);
    private static readonly Size ExpandedTasks = new(440, 270);
    private static readonly Size ExpandedShelf = new(520, 250);
    private static readonly Size ExpandedAgenda = new(460, 250);
    // How early the closed notch starts showing the next event.
    private static readonly TimeSpan UpcomingWindow = TimeSpan.FromMinutes(15);
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
    private readonly FocusTimer _focus;
    private readonly WaterTracker _water;
    private readonly TaskList _tasks;
    private readonly Shelf _shelf;
    private readonly GoogleCalendar _calendar;
    private readonly PresenceTracker _presence;
    private readonly DispatcherTimer _newsTimer = new() { Interval = TimeSpan.FromSeconds(9) };
    private readonly RavenAnimator _ambientRaven;
    private readonly RavenAnimator _musicRaven;
    private readonly RavenAnimator _newsRaven;
    private readonly RavenAnimator _idleRaven;
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
    // The compact line waiting for the outgoing one to slide away; null when no swap is running.
    private string? _incomingCompactText;
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
    // "Daily em 8 min" while an event is close and nothing is playing; null otherwise.
    private string? _upcomingText;
    // True while the task box is being typed in; the notch accepts focus only then.
    private bool _keyboardMode;
    private IntPtr _previousForeground;
    // Files are being dragged over the notch.
    private bool _dropHinting;

    private bool _fullscreenApp;
    private DateTime _pinnedUntil;
    private SettingsWindow? _settingsWindow;
    private BitmapImage? _artwork;
    private static readonly Color MonochromeAccent = Color.FromRgb(0xE0, 0xE0, 0xE0);

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

        _audio = new AudioWatcher(Dispatcher);
        _brightness = new BrightnessWatcher(Dispatcher);
        _power = new PowerWatcher(Dispatcher);
        _network = new NetworkWatcher(Dispatcher);
        _audio.Message += ShowHud;
        _brightness.Message += ShowHud;
        _power.Message += ShowHud;
        _network.Message += ShowHud;
        _keyboard.Message += ShowHud;

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
        _calendar = new GoogleCalendar(_store);
        _calendar.Message += ShowHud;
        _calendar.Changed += UpdateClock;
        AgendaPage.Attach(_calendar);
        TasksPage.KeyboardRequested += EnterKeyboardMode;
        TasksPage.KeyboardReleased += ExitKeyboardMode;
        Deactivated += OnDeactivated;

        _ambientRaven = new RavenAnimator(AmbientRaven);
        _musicRaven = new RavenAnimator(MusicRaven);
        _newsRaven = new RavenAnimator(NewsRaven);
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
        _calendar.Start();
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
        _audio.Dispose();
        _brightness.Dispose();
        _power.Dispose();
        _network.Dispose();
        _keyboard.Dispose();
        _water.Stop();
        _tasks.Stop();
        _calendar.Stop();
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
            _settingsWindow = new SettingsWindow(_store, _calendar);
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
        UpdateClock();
        OnFullscreenChanged(_fullscreenApp);
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
            "agenda" or "calendar" => TabAgenda,
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

    // ---- Shelf drag & drop ----------------------------------------------------------

    private void OnNotchDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (ShelfPage.IsDraggingOut || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        // The shelf only keeps a reference, so "link" is the honest cursor when the source allows it.
        e.Effects = e.AllowedEffects.HasFlag(DragDropEffects.Link) ? DragDropEffects.Link : DragDropEffects.Copy;
        _hoverTimer.Stop();
        if (_dropHinting)
            return;

        _dropHinting = true;
        TabShelf.IsChecked = true;
        ShelfPage.ShowDropHint(true);
        SetExpanded(true);
    }

    private void OnNotchDragLeave(object sender, DragEventArgs e)
    {
        // Also fires when moving between child elements; the next DragOver cancels this.
        if (_dropHinting)
            ScheduleExpanded(false, 400);
    }

    private void OnNotchDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!ShelfPage.IsDraggingOut && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            _shelf.Add(paths);
            // Tell the source nothing was moved: the shelf only references the files.
            e.Effects = e.AllowedEffects.HasFlag(DragDropEffects.Link) ? DragDropEffects.Link : DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        EndDropHint();
    }

    private void EndDropHint()
    {
        _dropHinting = false;
        ShelfPage.ShowDropHint(false);
    }

    private void OnTabChecked(object sender, RoutedEventArgs e)
    {
        if (TasksPage == null)
            return; // Fired by the default tab while the XAML is still loading.

        _page = sender == TabAgenda ? NotchPage.Agenda
              : sender == TabFocus ? NotchPage.Focus
              : sender == TabWater ? NotchPage.Water
              : sender == TabTasks ? NotchPage.Tasks
              : sender == TabShelf ? NotchPage.Shelf
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
        _peeking = false;
        if (expanded)
        {
            _hudActive = false;
            _hudTimer.Stop();
            HideNews();
        }
        else
        {
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
                     : _upcomingText != null ? (_focus.IsActive ? CompactEventTimer : CompactEvent)
                     : _focus.IsActive ? CompactIdleTimer
                     : CompactIdle;
        Size expanded = _page switch
        {
            NotchPage.Agenda => ExpandedAgenda,
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
        AgendaPage.Visibility = _page == NotchPage.Agenda ? Visibility.Visible : Visibility.Collapsed;
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
            PlayPauseButton.Content = _state.IsPlaying ? "\uF8AE" : "\uF5B0";
            AutomationProperties.SetName(PlayPauseButton, _state.IsPlaying ? "Pausar" : "Tocar");

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
        UpdateCompanion();
    }

    private void UpdateCompactText()
    {
        if (_state == null)
            return; // The clock owns the compact text.

        string? line = _lyrics != null && _lyricIndex >= 0 ? _lyrics.Lines[_lyricIndex].Text : null;
        SetCompactText(string.IsNullOrWhiteSpace(line) ? _state.Title : line);
    }

    /// <summary>
    /// Swaps the closed notch's line: the old text slides up and out, the new one rises in.
    /// Changes that arrive mid-swap just replace the incoming text.
    /// </summary>
    private void SetCompactText(string text)
    {
        if (_incomingCompactText != null)
        {
            _incomingCompactText = text;
            return;
        }
        if (text == CompactText.Text)
            return;
        if (CompactView.Visibility != Visibility.Visible || string.IsNullOrEmpty(CompactText.Text))
        {
            CompactText.Text = text;
            return;
        }

        _incomingCompactText = text;
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var outgoing = new DoubleAnimation(0, -10, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease };
        outgoing.Completed += (_, _) =>
        {
            CompactText.Text = _incomingCompactText;
            _incomingCompactText = null;
            var rise = new CubicEase { EasingMode = EasingMode.EaseOut };
            CompactTextShift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = rise });
            CompactText.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = rise });
        };
        CompactTextShift.BeginAnimation(TranslateTransform.YProperty, outgoing);
        CompactText.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease });
    }

    /// <summary>While a focus session is active its countdown takes the equalizer's slot.</summary>
    private void UpdateCompactTimer()
    {
        bool active = _focus.IsActive;
        CompactTimer.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        bool nodding = MusicRaven.Visibility == Visibility.Visible;
        Equalizer.Visibility = _state != null && !active && !nodding ? Visibility.Visible : Visibility.Collapsed;

        var remaining = _focus.Remaining;
        CompactTimerText.Text = $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
        // Paused reads differently from running at a glance: pause icon, dimmed time.
        CompactTimerGlyph.Text = _focus.IsPaused ? "" : Glyphs.Timer;
        CompactTimer.Opacity = _focus.IsPaused ? 0.55 : 1;

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
        _artwork = image;
        ApplyAccent();
    }

    /// <summary>
    /// The cover's color, or plain light gray when the user prefers black and white.
    /// Only media elements (equalizer, progress bar) wear it; everything else stays white.
    /// </summary>
    private void ApplyAccent()
    {
        var accent = !Settings.UseArtworkAccent ? MonochromeAccent
                   : _artwork != null ? ColorExtractor.FromImage(_artwork)
                   : ColorExtractor.Default;
        _accent.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(accent, TimeSpan.FromMilliseconds(500)));
    }

    /// <summary>
    /// Endless decorative animations run at 15 fps instead of 60. Each frame repaints the
    /// whole transparent window; at 60 fps the equalizer alone cost ~25% of a CPU core.
    /// </summary>
    internal static T Ambient<T>(T animation) where T : Timeline
    {
        Timeline.SetDesiredFrameRate(animation, 15);
        return animation;
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
                var bar = new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(EqualizerPeriods[i]))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, Ambient(bar));
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

    // ---- Yata (companion) -----------------------------------------------------------

    private void UpdateCompanion()
    {
        bool idle = Settings.CompanionEnabled && _presence.State != Presence.Active;

        // Music still playing while you're away: Yata takes the equalizer's slot and nods along.
        bool nodding = idle && _state?.IsPlaying == true && !_focus.IsActive;
        MusicRaven.Visibility = nodding ? Visibility.Visible : Visibility.Collapsed;
        if (nodding)
            _musicRaven.Nod();
        else
            _musicRaven.Stop();

        // Nothing playing (a paused player counts): the clock gives way to Yata, who eventually dozes off.
        bool ambient = idle && _state?.IsPlaying != true && IsVisible;
        if (ambient)
        {
            if (_ambientLeaving)
                CancelLeave();
            _ambientShown = true;
            bool asleep = _presence.State == Presence.Asleep;
            if (asleep)
                _ambientRaven.Sleep();
            else
                _ambientRaven.Perch();
            SetZzz(asleep);
        }
        else if (_ambientShown && !_ambientLeaving)
        {
            LeaveAmbient();
        }

        UpdateCompactTimer();
        UpdateTargets();
    }

    /// <summary>You're back: Yata flaps off to the right and the clock returns.</summary>
    private void LeaveAmbient()
    {
        _ambientLeaving = true;
        SetZzz(false);
        _ambientRaven.Fly();
        var flight = new DoubleAnimation(0, 150, TimeSpan.FromMilliseconds(700))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        flight.Completed += (_, _) =>
        {
            if (!_ambientLeaving)
                return;
            CancelLeave();
            _ambientShown = false;
            _ambientRaven.Stop();
            UpdateTargets();
        };
        AmbientRavenShift.BeginAnimation(TranslateTransform.XProperty, flight);
    }

    private void CancelLeave()
    {
        _ambientLeaving = false;
        AmbientRavenShift.BeginAnimation(TranslateTransform.XProperty, null);
        AmbientRavenShift.X = 0;
    }

    private void SetZzz(bool on)
    {
        Zzz.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!on)
        {
            ZzzShift.BeginAnimation(TranslateTransform.YProperty, null);
            Zzz.BeginAnimation(OpacityProperty, null);
            return;
        }

        var period = TimeSpan.FromSeconds(2.2);
        ZzzShift.BeginAnimation(TranslateTransform.YProperty,
            Ambient(new DoubleAnimation(3, -6, period) { RepeatBehavior = RepeatBehavior.Forever }));
        Zzz.BeginAnimation(OpacityProperty,
            Ambient(new DoubleAnimation(1, 0, period) { RepeatBehavior = RepeatBehavior.Forever }));
    }

    private void OnWentAway(TimeSpan unattended)
    {
        // Pause focus so time away isn't counted — unless something is playing (likely a video).
        if (Settings.PauseFocusWhenAway && _state?.IsPlaying != true && _focus.PauseAfterIdle(unattended))
            _presence.Add(Glyphs.Timer, "Foco pausado quando você saiu");
    }

    private void OnReturned(AwayReport report)
    {
        if (Settings.AwaySummaryEnabled && report.Items.Count > 0 && IsVisible && !_expanded)
            ShowNews(report);
    }

    private void ShowNews(AwayReport report)
    {
        var items = report.Items.Skip(Math.Max(0, report.Items.Count - NewsMaxItems)).ToList();
        _newsCount = items.Count;
        NewsList.ItemsSource = items;
        NewsTitle.Text = report.Items.Count > items.Count
            ? $"Enquanto você esteve fora · {FormatAway(report.Duration)} · {report.Items.Count} avisos"
            : $"Enquanto você esteve fora · {FormatAway(report.Duration)}";

        _hudActive = false;
        _hudTimer.Stop();
        _newsActive = true;
        _newsTimer.Stop();
        _newsTimer.Start();
        UpdateTargets();

        // Yata flies in with the news, then the text appears.
        _newsRaven.Fly();
        var flight = new DoubleAnimation(NewsWidth, 0, TimeSpan.FromMilliseconds(750))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        flight.Completed += (_, _) =>
        {
            if (_newsActive)
                _newsRaven.Perch();
        };
        NewsRavenShift.BeginAnimation(TranslateTransform.XProperty, flight);
        NewsBody.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400))
        {
            BeginTime = TimeSpan.FromMilliseconds(450),
        });
    }

    private void HideNews()
    {
        if (!_newsActive)
            return;
        _newsActive = false;
        _newsTimer.Stop();
        _newsRaven.Stop();
        UpdateTargets();
    }

    /// <summary>Walks through Yata's states in ~20 s instead of waiting real minutes.</summary>
    public async void RunCompanionDemo()
    {
        SetExpanded(false);
        _presence.Simulate(_presence.IdleAfter);
        await Task.Delay(7000);
        _presence.Simulate(_presence.SleepAfter + TimeSpan.FromMinutes(37));
        _presence.Add(Glyphs.Bell, "Reunião com o time · 15:30");
        _presence.Add(Glyphs.Water, "Hora de beber água · 750/2000 ml");
        await Task.Delay(7000);
        _presence.Simulate(TimeSpan.Zero); // "You're back."
        await Task.Delay(1500);
        _presence.Simulate(null);
    }

    private static string FormatAway(TimeSpan duration) =>
        duration.TotalHours >= 1 ? $"{(int)duration.TotalHours}h{duration.Minutes:00}" : $"{Math.Max(1, (int)duration.TotalMinutes)} min";

    // ---- HUD ------------------------------------------------------------------------

    private void ShowHud(HudMessage message)
    {
        if (!Settings.Shows(message.Kind))
            return;
        _presence.NoteIfUnseen(message);

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

        string? upcoming = null;
        if (_state == null && Settings.CalendarInCompact &&
            CalendarReminders.NextWithin(_calendar.Events, now, UpcomingWindow) is CalendarEvent next)
            upcoming = $"{next.Title} {CalendarReminders.Until(next.Start, now)}";
        if ((upcoming == null) != (_upcomingText == null))
        {
            _upcomingText = upcoming;
            UpdateTargets(); // The notch widens for the event's name.
        }
        _upcomingText = upcoming;

        if (_state == null)
            SetCompactText(upcoming ?? time);

        IdleClock.Text = time;
        string date = now.ToString("dddd, d 'de' MMMM", PtBr);
        HeaderDate.Text = char.ToUpper(date[0], PtBr) + date[1..];
    }
}
