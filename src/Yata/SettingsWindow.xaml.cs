using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
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
    private readonly GoogleCalendar _calendar;

    public SettingsWindow(AppStore store, GoogleCalendar calendar)
    {
        _store = store;
        _calendar = calendar;
        InitializeComponent();
        DataContext = store.Data;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"versão {version?.ToString(3)} · código aberto (MIT)";
        StartupSwitch.IsChecked = Autostart.IsEnabled;
        ShowMonitor();
        ShowUpdate();
        Activated += (_, _) => ShowUpdate(); // A check may have finished meanwhile.

        // Switches and stepper buttons are all ButtonBase; by the time Click bubbles up here
        // the bindings have already written the new value.
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, _) => _store.NotifySettingsChanged()));
        SourceInitialized += (_, _) => UseDarkTitleBar();

        _calendar.Changed += ShowCalendar;
        Closed += (_, _) => _calendar.Changed -= ShowCalendar;
        ShowCalendar();
        ShowWeatherPlace();
    }

    // ---- Weather ----------------------------------------------------------------------

    private static readonly System.Globalization.CultureInfo PtBr = new("pt-BR");
    private CancellationTokenSource? _weatherSearch;

    private void ShowWeatherPlace() =>
        WeatherPlaceText.Text = _store.Settings.WeatherPlace ?? "nenhuma escolhida";

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
        SetWeatherStatus("Buscando…");
        try
        {
            var places = await WeatherService.SearchAsync(name, cts.Token);
            if (cts.IsCancellationRequested)
                return;
            WeatherResults.ItemsSource = places;
            SetWeatherStatus(places.Count == 0 ? "Nenhuma cidade encontrada." : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            if (!cts.IsCancellationRequested)
                SetWeatherStatus("Sem conexão com o Open-Meteo.");
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
        SetWeatherStatus("Pedindo a localização ao Windows…");
        var (result, latitude, longitude) = await WeatherService.LocateOnceAsync();
        WeatherLocateButton.IsEnabled = true;

        switch (result)
        {
            case LocateResult.Found:
                UseWeatherPlace($"Minha localização ({latitude.ToString("0.00", PtBr)}, {longitude.ToString("0.00", PtBr)})",
                    latitude, longitude);
                // Arrived after the click was handled, so save and apply here.
                _store.NotifySettingsChanged();
                break;
            case LocateResult.Denied:
                SetWeatherStatus("A localização está desligada para apps da área de trabalho.");
                LocationSettingsButton.Visibility = Visibility.Visible;
                break;
            default:
                SetWeatherStatus("Não foi possível obter a localização agora. Tente de novo ou busque a cidade.");
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
    }

    private sealed record CalendarChoice(string Id, string Name, string Hint, bool Included);

    private void ShowCalendar()
    {
        var (title, hint, button) = _calendar.Status switch
        {
            CalendarStatus.NotConfigured => ("Google Agenda",
                "Esta versão foi compilada sem credenciais do Google (veja docs/google-calendar.md).", null),
            CalendarStatus.Connecting => ("Google Agenda", "Continue no navegador que abriu…", "Cancelar"),
            CalendarStatus.Connected => ($"Conectado como {_calendar.Email}",
                _calendar.Error ?? "Só leitura: suas agendas e os eventos de hoje e amanhã ficam só na memória.", "Desconectar"),
            CalendarStatus.NeedsReconnect => (_calendar.Email ?? "Google Agenda", _calendar.Error ?? "Conecte de novo.", "Reconectar"),
            _ => ("Google Agenda", _calendar.Error ??
                "Mostra seus próximos eventos e avisa antes de cada um. Só leitura; o acesso fica protegido pelo Windows neste PC.",
                (string?)"Conectar com Google"),
        };
        CalendarTitle.Text = title;
        CalendarHint.Text = hint;
        CalendarButton.Content = button;
        CalendarButton.Visibility = button == null ? Visibility.Collapsed : Visibility.Visible;

        bool connected = _calendar.Status == CalendarStatus.Connected;
        CalendarOptions.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        CalendarList.ItemsSource = connected
            ? _calendar.Calendars
                .Select(c => new CalendarChoice(c.Id, c.Name, c.Primary ? "Agenda principal" : "", _calendar.IsIncluded(c)))
                .ToList()
            : null;
    }

    private void OnCalendarButton(object sender, RoutedEventArgs e)
    {
        switch (_calendar.Status)
        {
            case CalendarStatus.Connecting:
                _calendar.CancelConnect();
                break;
            case CalendarStatus.Connected:
                _ = _calendar.DisconnectAsync();
                break;
            default:
                _ = _calendar.ConnectAsync();
                break;
        }
    }

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
        }
        StartupSwitch.IsChecked = Autostart.IsEnabled;
    }

    /// <summary>Next display, wrapping around; the window-level Click handler then applies it.</summary>
    private void OnMonitorClick(object sender, RoutedEventArgs e)
    {
        var all = Monitors.All();
        var current = Monitors.Resolve(_store.Settings.Monitor);
        int index = all.ToList().FindIndex(m => m.DeviceName == current.DeviceName);
        var next = all[(index + 1) % all.Count];
        // The primary display is stored as "no choice", so it keeps following the primary if that changes.
        _store.Settings.Monitor = next.IsPrimary ? null : next.DeviceName;
        ShowMonitor();
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
        UpdateText.Text = $"Versão {latest} disponível";
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

    private void UseDarkTitleBar()
    {
        int dark = 1;
        DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
