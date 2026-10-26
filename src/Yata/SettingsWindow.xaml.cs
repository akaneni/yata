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
        VersionText.Text = $"versão {version?.ToString(3)} · código aberto (MIT)";
        StartupSwitch.IsChecked = Autostart.IsEnabled;
        ShowMonitor();
        ShowUpdate();
        Activated += (_, _) => ShowUpdate(); // A check may have finished meanwhile.

        // Switches and stepper buttons are all ButtonBase; by the time Click bubbles up here
        // the bindings have already written the new value.
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, _) => _store.NotifySettingsChanged()));
        SourceInitialized += (_, _) => UseDarkTitleBar();

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

    private void ShowGoogle()
    {
        var (title, hint, button) = _google.Status switch
        {
            GoogleAccountStatus.NotConfigured => ("Conta Google",
                "Esta versão foi compilada sem credenciais do Google (veja docs/google-calendar.md).", null),
            GoogleAccountStatus.Connecting => ("Conta Google", "Continue no navegador que abriu…", "Cancelar"),
            GoogleAccountStatus.Connected => ($"Conectado como {_google.Email}", _google.Error ??
                "O acesso fica protegido pelo Windows neste PC. Desconectar revoga o acesso no Google.", "Desconectar"),
            GoogleAccountStatus.NeedsReconnect => (_google.Email ?? "Conta Google", _google.Error ?? "Conecte de novo.", "Reconectar"),
            _ => ("Conta Google", _google.Error ??
                "Agenda e tarefas do Google no notch. Na tela do Google você escolhe o que liberar; o acesso fica protegido pelo Windows neste PC.",
                (string?)"Conectar com Google"),
        };
        GoogleTitle.Text = title;
        GoogleHint.Text = hint;
        GoogleButton.Content = button;
        GoogleButton.Visibility = button == null ? Visibility.Collapsed : Visibility.Visible;
        GoogleFeatures.Visibility = _google.Status == GoogleAccountStatus.Connected ? Visibility.Visible : Visibility.Collapsed;

        // Calendar
        bool calendar = _calendar.Status == CalendarStatus.Connected;
        CalendarHint.Text = !calendar ? "Mostra seus próximos eventos e avisa antes de cada um. Só leitura."
            : _calendar.Error ?? "Só leitura: suas agendas e os eventos de hoje e amanhã ficam só na memória.";
        CalendarButton.Visibility = calendar ? Visibility.Collapsed : Visibility.Visible;
        CalendarOptions.Visibility = calendar ? Visibility.Visible : Visibility.Collapsed;
        CalendarList.ItemsSource = calendar
            ? _calendar.Calendars
                .Select(c => new CalendarChoice(c.Id, c.Name, c.Primary ? "Agenda principal" : "", _calendar.IsIncluded(c)))
                .ToList()
            : null;

        // Tasks
        bool access = _googleTasks.HasAccess, on = _googleTasks.IsOn;
        TasksHint.Text = !on
            ? "Sincroniza a aba Tarefas com o Google Tasks, nos dois sentidos; as abertas daqui vão para a lista. O horário dos lembretes fica só no Yata."
            : _googleTasks.Error ?? "Concluir, criar e remover aqui vale também no Google. Desligar tira da aba as tarefas do Google.";
        TasksButton.Visibility = access ? Visibility.Collapsed : Visibility.Visible;
        TasksSwitch.Visibility = access ? Visibility.Visible : Visibility.Collapsed;
        TasksSwitch.IsChecked = on;
        TasksOptions.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        TasksListButton.Content = _store.Settings.TasksListName ?? "Carregando…";
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
            "Algumas permissões ficaram desmarcadas; dá para liberar depois aqui.");
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

    /// <summary>Next list, wrapping around, like the display picker.</summary>
    private void OnTasksList(object sender, RoutedEventArgs e)
    {
        var lists = _googleTasks.Lists;
        if (lists.Count < 2)
            return;
        int index = lists.ToList().FindIndex(l => l.Id == _store.Settings.TasksListId);
        _googleTasks.SetList(lists[(index + 1) % lists.Count]);
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
            Log.Warn("settings", "Não foi possível mudar o início com o Windows", ex);
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
        DiagnosticsButton.Content = "Copiado";
        await Task.Delay(2000);
        DiagnosticsButton.Content = "Copiar diagnóstico";
    }

    private void UseDarkTitleBar()
    {
        int dark = 1;
        DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
