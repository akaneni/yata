using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Yata.Interop;
using Yata.Helpers;

namespace Yata.Services;

/// <summary>
/// Apps capturing from a microphone right now, from the Core Audio sessions on every active
/// capture device. Event driven: Windows tells us when a session appears or changes state; only while
/// an app is shown is the list also checked again every few seconds, in case a stop went unannounced.
/// Callbacks arrive on worker threads; the list is rebuilt on the UI dispatcher.
/// </summary>
[ComVisible(true)]
internal sealed class MicrophoneMonitor : IAudioSessionNotification, IMMNotificationClient, IDisposable
{
    private const int CLSCTX_ALL = 0x17;
    private const int DEVICE_STATE_ACTIVE = 1;

    private readonly Dispatcher _dispatcher;
    // Sessions start and stop in bursts (and per device); rebuild once things settle.
    private readonly DispatcherTimer _settle = new() { Interval = TimeSpan.FromMilliseconds(250) };
    // Windows sometimes never says a session stopped, which left the dot on for good. While an app
    // is shown, look again now and then; with nothing shown, nothing runs.
    private readonly DispatcherTimer _verify = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly List<IAudioSessionManager2> _managers = new();
    private readonly Dictionary<string, (IAudioSessionControl2 Control, SessionEvents Events)> _sessions = new();
    private IMMDeviceEnumerator? _enumerator;
    private bool _rebind;

    public IReadOnlyList<string> Apps { get; private set; } = Array.Empty<string>();

    public event Action? Changed;

    public MicrophoneMonitor(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _settle.Tick += (_, _) => { _settle.Stop(); Refresh(); };
        _verify.Tick += (_, _) => Refresh();
    }

    public void Start()
    {
        try
        {
            _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            _enumerator.RegisterEndpointNotificationCallback(this);
            BindDevices();
            Refresh();
        }
        catch (COMException ex)
        {
            // No audio subsystem: nothing can use a microphone either.
            Log.Warn("microphone", "Core Audio indisponível; uso do microfone não é detectado", ex);
        }
    }

    public void Dispose()
    {
        _settle.Stop();
        _verify.Stop();
        Unbind();
        if (_enumerator != null)
        {
            try { _enumerator.UnregisterEndpointNotificationCallback(this); } catch (COMException) { }
            Marshal.ReleaseComObject(_enumerator);
            _enumerator = null;
        }
    }

    private void BindDevices()
    {
        Unbind();
        if (_enumerator == null)
            return;

        IntPtr collectionPtr;
        try
        {
            _enumerator.EnumAudioEndpoints(EDataFlow.Capture, DEVICE_STATE_ACTIVE, out collectionPtr);
        }
        catch (COMException ex)
        {
            // A device coming or going mid-listing: try again at the next change.
            Log.Warn("microphone", "Falha ao listar os microfones; nova tentativa na próxima mudança", ex);
            _rebind = true;
            return;
        }
        var devices = (IMMDeviceCollection)Marshal.GetObjectForIUnknown(collectionPtr);
        Marshal.Release(collectionPtr);
        try
        {
            devices.GetCount(out uint count);
            for (uint i = 0; i < count; i++)
            {
                try
                {
                    devices.Item(i, out var device);
                    var iid = typeof(IAudioSessionManager2).GUID;
                    device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var instance);
                    var manager = (IAudioSessionManager2)instance;
                    // Session notifications only start after the sessions were enumerated once.
                    manager.GetSessionEnumerator(out var sessions);
                    Marshal.ReleaseComObject(sessions);
                    manager.RegisterSessionNotification(this);
                    _managers.Add(manager);
                }
                catch (COMException)
                {
                    // Device went away mid-enumeration; the device notification rebinds.
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(devices);
        }
    }

    private void Unbind()
    {
        foreach (var (control, events) in _sessions.Values)
            Release(control, events);
        _sessions.Clear();
        foreach (var manager in _managers)
        {
            try { manager.UnregisterSessionNotification(this); } catch (COMException) { }
            Marshal.ReleaseComObject(manager);
        }
        _managers.Clear();
    }

    private void Refresh()
    {
        if (_rebind)
        {
            _rebind = false;
            BindDevices();
        }

        var seen = new HashSet<string>();
        var apps = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        int self = Environment.ProcessId;

        foreach (var manager in _managers)
        {
            try
            {
                manager.GetSessionEnumerator(out var sessions);
                sessions.GetCount(out int count);
                for (int i = 0; i < count; i++)
                {
                    sessions.GetSession(i, out var session);
                    var control = (IAudioSessionControl2)session;
                    control.GetSessionInstanceIdentifier(out var id);
                    seen.Add(id);
                    if (!_sessions.ContainsKey(id))
                    {
                        // Follow this session's start/stop from now on.
                        var events = new SessionEvents(ScheduleRefresh);
                        control.RegisterAudioSessionNotification(events);
                        _sessions[id] = (control, events);
                    }
                    else
                    {
                        // Not released: .NET may hand back the very same wrapper we track.
                        control = _sessions[id].Control;
                    }

                    control.GetState(out var state);
                    control.GetProcessId(out uint pid);
                    if (state == AudioSessionState.Active && pid != 0 && pid != self)
                        apps.Add(AppName((int)pid));
                }
                Marshal.ReleaseComObject(sessions);
            }
            catch (COMException)
            {
                // Device removed; its notification triggers a rebind.
            }
        }

        foreach (var id in _sessions.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            Release(_sessions[id].Control, _sessions[id].Events);
            _sessions.Remove(id);
        }

        _verify.IsEnabled = apps.Count > 0;
        if (apps.SequenceEqual(Apps))
            return;
        Apps = apps.ToList();
        Changed?.Invoke();
    }

    private static void Release(IAudioSessionControl2 control, SessionEvents events)
    {
        try { control.UnregisterAudioSessionNotification(events); } catch (COMException) { }
        Marshal.ReleaseComObject(control);
    }

    private static string AppName(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            try
            {
                string? description = process.MainModule?.FileVersionInfo.FileDescription;
                if (!string.IsNullOrWhiteSpace(description))
                    return description.Trim();
            }
            catch (Exception)
            {
                // Elevated or protected process: its name is all we can read.
            }
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return "App";
        }
    }

    private void ScheduleRefresh() => _dispatcher.Post("microphone", () =>
    {
        _settle.Stop();
        _settle.Start();
    });

    private void ScheduleRebind()
    {
        _rebind = true;
        ScheduleRefresh();
    }

    // ---- IAudioSessionNotification / IMMNotificationClient (worker threads) ------------

    int IAudioSessionNotification.OnSessionCreated(IAudioSessionControl session)
    {
        ScheduleRefresh();
        return 0;
    }

    int IMMNotificationClient.OnDeviceStateChanged(string deviceId, int newState) { ScheduleRebind(); return 0; }
    int IMMNotificationClient.OnDeviceAdded(string deviceId) { ScheduleRebind(); return 0; }
    int IMMNotificationClient.OnDeviceRemoved(string deviceId) { ScheduleRebind(); return 0; }
    int IMMNotificationClient.OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId) => 0;
    int IMMNotificationClient.OnPropertyValueChanged(string deviceId, PropertyKey key) => 0;

    /// <summary>Per-session callback: only start/stop and disconnects matter.</summary>
    [ComVisible(true)]
    private sealed class SessionEvents : IAudioSessionEvents
    {
        private readonly Action _changed;

        public SessionEvents(Action changed) => _changed = changed;

        public int OnStateChanged(AudioSessionState state) { _changed(); return 0; }
        public int OnSessionDisconnected(int reason) { _changed(); return 0; }
        public int OnDisplayNameChanged(string name, IntPtr eventContext) => 0;
        public int OnIconPathChanged(string path, IntPtr eventContext) => 0;
        public int OnSimpleVolumeChanged(float volume, int muted, IntPtr eventContext) => 0;
        public int OnChannelVolumeChanged(uint channels, IntPtr volumes, uint changedChannel, IntPtr eventContext) => 0;
        public int OnGroupingParamChanged(IntPtr grouping, IntPtr eventContext) => 0;
    }
}
