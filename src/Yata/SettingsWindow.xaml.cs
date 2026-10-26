using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
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

    public SettingsWindow(AppStore store)
    {
        _store = store;
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
