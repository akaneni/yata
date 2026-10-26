using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using Microsoft.Win32;
using Yata.Interop;
using Yata.Helpers;

namespace Yata.Services;

/// <summary>Which apps are using the camera and the microphone right now.</summary>
public sealed record PrivacyState(IReadOnlyList<string> Camera, IReadOnlyList<string> Microphone)
{
    public static readonly PrivacyState None = new(Array.Empty<string>(), Array.Empty<string>());

    public bool Any => Camera.Count > 0 || Microphone.Count > 0;
}

/// <summary>
/// Camera / microphone in use. Nothing is polled, hooked or sent anywhere:
/// <list type="bullet">
/// <item>Microphone: Core Audio capture sessions (<see cref="MicrophoneMonitor"/>), on every Windows version.</item>
/// <item>Camera: where Windows records it for the taskbar privacy icon (CapabilityAccessManager\ConsentStore:
/// an app is using it while its LastUsedTimeStop is 0), waiting on a registry change notification. Windows 11
/// 26H2 moved that record to an admin-only database, so there the camera isn't detected.</item>
/// </list>
/// </summary>
internal sealed class PrivacyWatcher : IDisposable
{
    private const string StorePath = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

    private readonly Dispatcher _dispatcher;
    private readonly MicrophoneMonitor _microphone;
    private readonly ManualResetEvent _stop = new(false);
    private Thread? _thread;
    private IReadOnlyList<string> _camera = Array.Empty<string>();

    public PrivacyState Current { get; private set; } = PrivacyState.None;

    /// <summary>Raised on the dispatcher with the previous and the new state.</summary>
    public event Action<PrivacyState, PrivacyState>? Changed;

    public PrivacyWatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _microphone = new MicrophoneMonitor(dispatcher);
        _microphone.Changed += () => Apply(Compose());
    }

    public void Start()
    {
        _camera = ReadCamera();
        _microphone.Start();
        Apply(Compose());

        _thread = new Thread(Watch) { IsBackground = true, Name = "Yata camera watcher" };
        _thread.Start();
    }

    public void Dispose()
    {
        _stop.Set();
        _thread?.Join(500);
        _microphone.Dispose();
    }

    private PrivacyState Compose() => new(_camera, _microphone.Apps);

    private void Watch()
    {
        using var store = Registry.CurrentUser.OpenSubKey(StorePath, writable: false);
        if (store == null)
            return;

        using var changed = new AutoResetEvent(false);
        var handles = new WaitHandle[] { changed, _stop };
        while (true)
        {
            // Re-armed every time: a notification fires once.
            if (NativeMethods.RegNotifyChangeKeyValue(store.Handle, true,
                    NativeMethods.REG_NOTIFY_CHANGE_NAME | NativeMethods.REG_NOTIFY_CHANGE_LAST_SET,
                    changed.SafeWaitHandle, true) != 0)
                return;
            if (WaitHandle.WaitAny(handles) == 1)
                return;
            // Apps write start and stop times in quick bursts; read once things settle.
            if (_stop.WaitOne(200))
                return;

            var camera = ReadCamera();
            _dispatcher.InvokeAsync(() =>
            {
                _camera = camera;
                Apply(Compose());
            });
        }
    }

    private void Apply(PrivacyState state)
    {
        var previous = Current;
        if (previous.Camera.SequenceEqual(state.Camera) && previous.Microphone.SequenceEqual(state.Microphone))
            return;
        Current = state;
        Changed?.Invoke(previous, state);
    }

    private static IReadOnlyList<string> ReadCamera()
    {
        try
        {
            using var store = Registry.CurrentUser.OpenSubKey(StorePath, writable: false);
            return store == null ? Array.Empty<string>() : AppsUsing(store, "webcam");
        }
        catch (Exception ex)
        {
            Log.Warn("privacy", "Falha ao ler o uso da câmera", ex);
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> AppsUsing(RegistryKey store, string capability)
    {
        using var root = store.OpenSubKey(capability);
        if (root == null)
            return Array.Empty<string>();

        var apps = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var name in root.GetSubKeyNames())
        {
            if (name == "NonPackaged")
            {
                // Desktop apps: one key per executable path, with '#' in place of '\'.
                using var desktop = root.OpenSubKey(name);
                foreach (var exe in desktop?.GetSubKeyNames() ?? Array.Empty<string>())
                {
                    using var app = desktop!.OpenSubKey(exe);
                    string path = exe.Replace('#', '\\');
                    // A crashed app never writes its stop time; only trust it while it runs.
                    if (InUse(app) && IsRunning(path))
                        apps.Add(DisplayName(path));
                }
            }
            else
            {
                // Store apps: package family name, e.g. MSTeams_8wekyb3d8bbwe.
                using var app = root.OpenSubKey(name);
                if (InUse(app))
                    apps.Add(name.Split('_')[0]);
            }
        }
        return apps.ToList();
    }

    private static bool InUse(RegistryKey? app) =>
        app?.GetValue("LastUsedTimeStart") is long start && start > 0 &&
        app.GetValue("LastUsedTimeStop") is long stop && stop == 0;

    private static bool IsRunning(string path)
    {
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path));
        foreach (var process in processes)
            process.Dispose();
        return processes.Length > 0;
    }

    private static string DisplayName(string path)
    {
        try
        {
            string? description = FileVersionInfo.GetVersionInfo(path).FileDescription;
            if (!string.IsNullOrWhiteSpace(description))
                return description.Trim();
        }
        catch (Exception)
        {
            // Missing or unreadable file: fall back to its name.
        }
        return Path.GetFileNameWithoutExtension(path);
    }
}
