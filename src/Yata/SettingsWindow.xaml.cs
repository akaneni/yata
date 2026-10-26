using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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

        // Switches and stepper buttons are all ButtonBase; by the time Click bubbles up here
        // the bindings have already written the new value.
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, _) => _store.NotifySettingsChanged()));
        SourceInitialized += (_, _) => UseDarkTitleBar();

        _calendar.Changed += ShowCalendar;
        Closed += (_, _) => _calendar.Changed -= ShowCalendar;
        ShowCalendar();
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
