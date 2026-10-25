using System;
using System.Management;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Yata.Services;

/// <summary>
/// Built-in display brightness changes, via the WmiMonitorBrightnessEvent WMI event.
/// Only laptops/tablets expose it; on desktops the watcher silently does nothing.
/// </summary>
internal sealed class BrightnessWatcher : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private ManagementEventWatcher? _watcher;

    public event Action<HudMessage>? Message;

    public BrightnessWatcher(Dispatcher dispatcher) => _dispatcher = dispatcher;

    // Connecting to WMI can take a moment, so it happens off the UI thread.
    public void Start() => Task.Run(() =>
    {
        try
        {
            var watcher = new ManagementEventWatcher(
                new ManagementScope(@"root\wmi"),
                new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            watcher.EventArrived += OnEventArrived;
            watcher.Start();
            _watcher = watcher;
        }
        catch (Exception)
        {
            // No WMI brightness provider (external monitors only).
        }
    });

    public void Dispose()
    {
        try
        {
            _watcher?.Stop();
        }
        catch (Exception)
        {
        }
        _watcher?.Dispose();
        _watcher = null;
    }

    private void OnEventArrived(object sender, EventArrivedEventArgs e)
    {
        if (e.NewEvent["Brightness"] is not byte level)
            return;

        _dispatcher.InvokeAsync(() =>
            Message?.Invoke(new HudMessage(Glyphs.Brightness, "Brilho", $"{level}", level / 100.0)));
    }
}
