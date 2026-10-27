using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using Yata.Helpers;
using Yata.Services;

namespace Yata;

/// <summary>
/// Preferences. Controls bind straight to the stored data; every click applies and saves
/// immediately, so there's no "Save" button to forget.
/// </summary>
public partial class SettingsWindow : Window
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private readonly AppStore _store;
    private readonly GoogleAccount _google;
    private readonly GoogleCalendar _calendar;
    private readonly GoogleTasks _googleTasks;

    public SettingsWindow(AppStore store, GoogleAccount google, GoogleCalendar calendar, GoogleTasks googleTasks)
    {
        _store = store;
        _google = google;
        _calendar = calendar;
        _googleTasks = googleTasks;
        InitializeComponent();
        DataContext = store.Data;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = string.Format(Strings.Settings_Version, version?.ToString(3));
        StartupSwitch.IsChecked = Autostart.IsEnabled;
        ShowLanguage();
        ShowMonitor();
        ShowUpdate();
        Activated += (_, _) => ShowUpdate(); // A check may have finished meanwhile.

        // Switches and stepper buttons are all ButtonBase; by the time Click bubbles up here
        // the bindings have already written the new value.
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, _) => _store.NotifySettingsChanged()));
        SourceInitialized += (_, _) => UseDarkTitleBar();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };

        _calendar.Changed += ShowGoogle;
        _googleTasks.Changed += ShowGoogle;
        Closed += (_, _) =>
        {
            _calendar.Changed -= ShowGoogle;
            _googleTasks.Changed -= ShowGoogle;
        };
        ShowGoogle();
        ShowWeatherPlace();
    }

    // ---- Screen readers ---------------------------------------------------------------

    /// <summary>
    /// Every row is a label (maybe with a hint) and a control on the right. Left alone, a screen
    /// reader says "on" without saying what is on: switches and steppers take the label as their
    /// name and the hint as help; buttons keep their own text and get the label as help.
    /// </summary>
    private void OnRowLoaded(object sender, RoutedEventArgs e)
    {
        var row = (Views.SettingRow)sender;
        var texts = row.Children.OfType<TextBlock>()
            .Concat(row.Children.OfType<StackPanel>().SelectMany(panel => panel.Children.OfType<TextBlock>()))
            .ToList();
        var labelStyle = (Style)FindResource("Label");
        var hintStyle = (Style)FindResource("Hint");
        if (texts.FirstOrDefault(t => t.Style == labelStyle) is not { } label)
            return;
        var hint = texts.FirstOrDefault(t => t.Style == hintStyle);

        foreach (var control in row.Children.OfType<FrameworkElement>())
        {
            switch (control)
            {
                case Views.Stepper stepper:
                    stepper.Label = label.Text;
                    break;
                case CheckBox:
                    BindText(control, AutomationProperties.NameProperty, label);
                    if (hint != null)
                        BindText(control, AutomationProperties.HelpTextProperty, hint);
                    break;
                case ButtonBase:
                    BindText(control, AutomationProperties.HelpTextProperty, label);
                    break;
            }
        }
    }

    private static void BindText(DependencyObject target, DependencyProperty property, TextBlock source) =>
        BindingOperations.SetBinding(target, property, new Binding(nameof(TextBlock.Text)) { Source = source });

    // ---- Weather ----------------------------------------------------------------------

    private CancellationTokenSource? _weatherSearch;

    private void ShowWeatherPlace() =>
        WeatherPlaceText.Text = _store.Settings.WeatherPlace ?? Strings.Settings_NoCity;

    private void OnWeatherSearchChanged(object sender, TextChangedEventArgs e) =>
        WeatherSearchPlaceholder.Visibility = WeatherSearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnWeatherSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        e.Handled = true;
        _ = SearchWeatherAsync();
    }

    private void OnWeatherSearch(object sender, RoutedEventArgs e) => _ = SearchWeatherAsync();

    private async Task SearchWeatherAsync()
    {
        string name = WeatherSearchBox.Text.Trim();
        if (name.Length < 2)
            return;

        _weatherSearch?.Cancel();
        var cts = _weatherSearch = new CancellationTokenSource();
        SetWeatherStatus(Strings.Settings_Searching);
        try
        {
            var places = await WeatherService.SearchAsync(name, cts.Token);
            if (cts.IsCancellationRequested)
                return;
            WeatherResults.ItemsSource = places;
            SetWeatherStatus(places.Count == 0 ? Strings.Settings_NoCityFound : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            if (!cts.IsCancellationRequested)
                SetWeatherStatus(Strings.Settings_NoOpenMeteo);
        }
    }

    private void OnWeatherPlacePicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WeatherPlace place })
            UseWeatherPlace(place.Detail.Length > 0 ? $"{place.Name}, {place.Detail}" : place.Name, place.Latitude, place.Longitude);
        // The window-level Click handler saves and applies right after this.
    }

    private async void OnWeatherLocate(object sender, RoutedEventArgs e)
    {
        WeatherLocateButton.IsEnabled = false;
        LocationSettingsButton.Visibility = Visibility.Collapsed;
        SetWeatherStatus(Strings.Settings_Locating);
        var (result, latitude, longitude) = await WeatherService.LocateOnceAsync();
        WeatherLocateButton.IsEnabled = true;

        switch (result)
        {
            case LocateResult.Found:
                UseWeatherPlace(string.Format(Strings.Settings_MyLocation, latitude.ToString("0.00", Lang.Culture), longitude.ToString("0.00", Lang.Culture)),
                    latitude, longitude);
                // Arrived after the click was handled, so save and apply here.
                _store.NotifySettingsChanged();
                break;
            case LocateResult.Denied:
                SetWeatherStatus(Strings.Settings_LocationOff);
                LocationSettingsButton.Visibility = Visibility.Visible;
                break;
            default:
                SetWeatherStatus(Strings.Settings_LocationFailed);
                break;
        }
    }

    private void OnOpenLocationSettings(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:privacy-location") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Settings app unavailable; the message already says where to look.
        }
    }

    private void UseWeatherPlace(string name, double latitude, double longitude)
    {
        var settings = _store.Settings;
        settings.WeatherPlace = name;
        settings.WeatherLatitude = Weather.Coarse(latitude);
        settings.WeatherLongitude = Weather.Coarse(longitude);
        settings.WeatherEnabled = true;
        // The switch is bound one way from the data it was created with; refresh it.
        DataContext = null;
        DataContext = _store.Data;

        WeatherResults.ItemsSource = null;
        WeatherSearchBox.Clear();
        SetWeatherStatus(null);
        LocationSettingsButton.Visibility = Visibility.Collapsed;
        ShowWeatherPlace();
    }

    private void SetWeatherStatus(string? text)
    {
        WeatherSearchStatus.Text = text ?? "";
        WeatherSearchStatus.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
        // Read out "Buscando…", "Nenhuma cidade encontrada." and the like as they appear.
        if (text != null)
            UIElementAutomationPeer.CreatePeerForElement(WeatherSearchStatus)
                .RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private sealed record CalendarChoice(string Id, string Name, string Hint, bool Included);

    private void ShowGoogle()
    {
        var (title, hint, button) = _google.Status switch
        {
            GoogleAccountStatus.NotConfigured => (Strings.Google_Account, Strings.Google_NotConfigured, null),
            GoogleAccountStatus.Connecting => (Strings.Google_Account, Strings.Google_ContinueInBrowser, Strings.Common_Cancel),
            GoogleAccountStatus.Connected => (string.Format(Strings.Google_ConnectedAs, _google.Email),
                _google.Error ?? Strings.Google_ConnectedHint, Strings.Google_Disconnect),
            GoogleAccountStatus.NeedsReconnect => (_google.Email ?? Strings.Google_Account,
                _google.Error ?? Strings.Google_ConnectAgain, Strings.Google_Reconnect),
            _ => (Strings.Google_Account, _google.Error ?? Strings.Google_Intro, (string?)Strings.Google_Connect),
        };
        GoogleTitle.Text = title;
        GoogleHint.Text = hint;
        GoogleButton.Content = button;
        GoogleButton.Visibility = button == null ? Visibility.Collapsed : Visibility.Visible;
        GoogleFeatures.Visibility = _google.Status == GoogleAccountStatus.Connected ? Visibility.Visible : Visibility.Collapsed;

        // Calendar
        bool calendar = _calendar.Status == CalendarStatus.Connected;
        CalendarHint.Text = !calendar ? Strings.Calendar_Intro
            : _calendar.Error ?? Strings.Calendar_Connected;
        CalendarButton.Visibility = calendar ? Visibility.Collapsed : Visibility.Visible;
        CalendarOptions.Visibility = calendar ? Visibility.Visible : Visibility.Collapsed;
        CalendarList.ItemsSource = calendar
            ? _calendar.Calendars
                .Select(c => new CalendarChoice(c.Id, c.Name, c.Primary ? Strings.Calendar_Primary : "", _calendar.IsIncluded(c)))
                .ToList()
            : null;

        // Tasks
        bool access = _googleTasks.HasAccess, on = _googleTasks.IsOn;
        TasksHint.Text = !on
            ? Strings.Tasks_Intro
            : _googleTasks.Error ?? Strings.Tasks_Connected;
        TasksButton.Visibility = access ? Visibility.Collapsed : Visibility.Visible;
        TasksSwitch.Visibility = access ? Visibility.Visible : Visibility.Collapsed;
        TasksSwitch.IsChecked = on;
        TasksOptions.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        TasksListButton.Content = _store.Settings.TasksListName ?? Strings.Common_Loading;
        TasksListButton.IsEnabled = _googleTasks.Lists.Count > 1;
    }

    private void OnGoogleButton(object sender, RoutedEventArgs e)
    {
        switch (_google.Status)
        {
            case GoogleAccountStatus.Connecting:
                _google.CancelConnect();
                break;
            case GoogleAccountStatus.Connected:
                _ = _google.DisconnectAsync();
                break;
            default:
                _ = ConnectGoogleAsync();
                break;
        }
    }

    /// <summary>Asks for both; whatever the user ticks on Google's screen is what gets turned on.</summary>
    private async Task ConnectGoogleAsync()
    {
        await _google.ConnectAsync(GoogleAuth.CalendarScopes.Concat(GoogleAuth.TasksScopes).ToArray(),
            Strings.Google_SomeUnchecked);
        if (_googleTasks.HasAccess && !_googleTasks.IsOn)
            await _googleTasks.EnableAsync();
        await _calendar.RefreshAsync();
    }

    private void OnCalendarButton(object sender, RoutedEventArgs e) => _ = _calendar.ConnectAsync();

    private void OnTasksButton(object sender, RoutedEventArgs e) => _ = _googleTasks.EnableAsync();

    private void OnTasksSwitch(object sender, RoutedEventArgs e)
    {
        if (TasksSwitch.IsChecked == true)
            _ = _googleTasks.EnableAsync();
        else
            _googleTasks.Disable();
    }

    private void OnTasksList(object sender, RoutedEventArgs e) =>
        ShowChoices(TasksListButton, _googleTasks.Lists, l => l.Name, l => l.Id == _store.Settings.TasksListId,
            _googleTasks.SetList);

    private void OnCalendarToggle(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: CalendarChoice choice } box)
            _calendar.SetIncluded(choice.Id, box.IsChecked == true);
    }

    private void OnStartupClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Autostart.Set(StartupSwitch.IsChecked == true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Registry locked down by policy: show the real state.
            Log.Warn("settings", "Não foi possível mudar o início com o Windows", ex);
        }
        StartupSwitch.IsChecked = Autostart.IsEnabled;
    }

    /// <summary>Following Windows or a language of its own; it takes effect at the next start.</summary>
    private void OnLanguageClick(object sender, RoutedEventArgs e) =>
        ShowChoices(LanguageButton, new string?[] { null }.Concat(Lang.Supported), LanguageLabel,
            language => language == _store.Settings.Language, chosen =>
            {
                _store.Settings.Language = chosen;
                ShowLanguage();
            });

    private void ShowLanguage() => LanguageButton.Content = LanguageLabel(_store.Settings.Language);

    private static string LanguageLabel(string? language) =>
        language != null ? Lang.Name(language) : string.Format(Strings.Settings_LanguageAuto, Lang.Name(Lang.FromWindows));

    private void OnMonitorClick(object sender, RoutedEventArgs e)
    {
        var current = Monitors.Resolve(_store.Settings.Monitor);
        ShowChoices(MonitorButton, Monitors.All(), m => m.Label, m => m.DeviceName == current.DeviceName, chosen =>
        {
            // The primary display is stored as "no choice", so it keeps following the primary if that changes.
            _store.Settings.Monitor = chosen.IsPrimary ? null : chosen.DeviceName;
            ShowMonitor();
        });
    }

    /// <summary>
    /// The options under a pill button, the current one checked; arrows and Enter pick, Esc closes.
    /// Picking one applies and saves it like any other setting.
    /// </summary>
    private void ShowChoices<T>(Button anchor, IEnumerable<T> options, Func<T, string> label, Func<T, bool> isCurrent,
        Action<T> choose)
    {
        var menu = new ContextMenu
        {
            Style = (Style)FindResource("ChoiceMenu"),
            PlacementTarget = anchor,
            MinWidth = anchor.ActualWidth,
        };
        foreach (var option in options)
        {
            var item = new MenuItem { Header = label(option), IsChecked = isCurrent(option) };
            item.Click += (_, _) =>
            {
                choose(option);
                _store.NotifySettingsChanged();
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void ShowMonitor()
    {
        var all = Monitors.All();
        MonitorButton.Content = Monitors.Resolve(_store.Settings.Monitor).Label;
        MonitorButton.IsEnabled = all.Count > 1;
    }

    private void ShowUpdate()
    {
        string? latest = _store.Data.Updates.Latest;
        bool newer = _store.Settings.UpdateCheckEnabled && UpdateChecker.IsNewer(latest, UpdateChecker.Current);
        UpdateRow.Visibility = newer ? Visibility.Visible : Visibility.Collapsed;
        UpdateText.Text = string.Format(Strings.Settings_UpdateAvailable, latest);
    }

    private void OnOpenReleases(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(UpdateChecker.ReleasesPage) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No default browser.
        }
    }

    private void OnOpenDataFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppPaths.DataDir) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Folder not created yet (nothing saved so far).
        }
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Log.Directory);
            Process.Start(new ProcessStartInfo(Log.Directory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("settings", "Não foi possível abrir a pasta do registro", ex);
        }
    }

    private async void OnCopyDiagnostics(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(Diagnostics.Describe(_store.Settings, _google.Status));
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            // Another app is holding the clipboard.
            Log.Warn("settings", "Área de transferência ocupada", ex);
            return;
        }
        DiagnosticsButton.Content = Strings.Settings_Copied;
        await Task.Delay(2000);
        DiagnosticsButton.Content = Strings.Settings_CopyDiagnostics;
    }

    private void UseDarkTitleBar()
    {
        int dark = 1;
        DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
