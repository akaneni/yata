using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Yata.Interop;
using Yata.Helpers;

namespace Yata.Services;

/// <summary>
/// Mutes the microphone in Windows itself, so every app hears silence (Teams, Meet, Discord, a game's
/// voice chat), not just one. Follows the default communications microphone and reports its mute
/// however it changed (Yata's shortcut, a keyboard's mic key, Windows' settings). Core Audio calls
/// back on worker threads; <see cref="Changed"/> is raised on the UI dispatcher.
/// </summary>
[ComVisible(true)]
internal sealed class MicrophoneMute : IAudioEndpointVolumeCallback, IMMNotificationClient, IDisposable
{
    private const int CLSCTX_ALL = 0x17;

    private readonly Dispatcher _dispatcher;
    private IMMDeviceEnumerator? _enumerator;
    private IAudioEndpointVolume? _volume;

    /// <summary>Whether the default microphone is muted; false when there is none.</summary>
    public bool IsMuted { get; private set; }

    /// <summary>The mute changed: true when muted. Not raised when the microphone itself is swapped.</summary>
    public event Action<bool>? Changed;

    public MicrophoneMute(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public void Start()
    {
        try
        {
            _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            _enumerator.RegisterEndpointNotificationCallback(this);
            Bind();
        }
        catch (COMException ex)
        {
            Log.Warn("microphone", "Core Audio indisponível; mute do microfone desligado", ex);
        }
    }

    public void Dispose()
    {
        Unbind();
        if (_enumerator != null)
        {
            try { _enumerator.UnregisterEndpointNotificationCallback(this); } catch (COMException) { }
            Marshal.ReleaseComObject(_enumerator);
            _enumerator = null;
        }
    }

    /// <summary>
    /// Mutes or unmutes the default microphones: the communications one (what call apps use) and the
    /// default one, when they differ. Returns false with no microphone to mute.
    /// </summary>
    public bool Toggle()
    {
        if (_enumerator == null || _volume == null)
            return false;
        bool mute = !IsMuted;
        bool any = false;
        foreach (var role in new[] { ERole.Communications, ERole.Console })
        {
            try
            {
                _enumerator.GetDefaultAudioEndpoint(EDataFlow.Capture, role, out var device);
                try
                {
                    var iid = typeof(IAudioEndpointVolume).GUID;
                    device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var instance);
                    var volume = (IAudioEndpointVolume)instance;
                    var context = Guid.Empty;
                    volume.SetMute(mute, ref context);
                    Marshal.ReleaseComObject(volume);
                    any = true;
                }
                finally
                {
                    Marshal.ReleaseComObject(device);
                }
            }
            catch (COMException ex)
            {
                Log.Warn("microphone", $"Falha ao {(mute ? "mutar" : "desmutar")} o microfone ({role})", ex);
            }
        }
        // The new state (and the notice) comes from Windows' callback, like any other way of muting.
        return any;
    }

    private void Bind()
    {
        Unbind();
        if (_enumerator == null)
            return;

        IMMDevice device;
        try
        {
            _enumerator.GetDefaultAudioEndpoint(EDataFlow.Capture, ERole.Communications, out device);
        }
        catch (COMException)
        {
            IsMuted = false; // No microphone connected.
            return;
        }

        try
        {
            var iid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var instance);
            _volume = (IAudioEndpointVolume)instance;
            _volume.GetMute(out bool muted);
            IsMuted = muted;
            _volume.RegisterControlChangeNotify(this);
        }
        catch (COMException)
        {
            Unbind();
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    }

    private void Unbind()
    {
        if (_volume == null)
            return;
        try { _volume.UnregisterControlChangeNotify(this); } catch (COMException) { }
        Marshal.ReleaseComObject(_volume);
        _volume = null;
    }

    // ---- Core Audio callbacks (worker threads) ----------------------------------------

    int IAudioEndpointVolumeCallback.OnNotify(IntPtr notifyData)
    {
        bool muted = Marshal.PtrToStructure<AudioVolumeNotificationData>(notifyData).Muted;
        _dispatcher.Post("microphone", () =>
        {
            // Volume changes come here too: only the mute matters.
            if (muted == IsMuted)
                return;
            IsMuted = muted;
            Changed?.Invoke(muted);
        });
        return 0;
    }

    int IMMNotificationClient.OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId)
    {
        // Another microphone became the default: follow it, quietly (its mute is its own).
        if (flow == EDataFlow.Capture && role == ERole.Communications)
            _dispatcher.Post("microphone", Bind);
        return 0;
    }

    int IMMNotificationClient.OnDeviceStateChanged(string deviceId, int newState) => 0;

    int IMMNotificationClient.OnDeviceAdded(string deviceId) => 0;

    int IMMNotificationClient.OnDeviceRemoved(string deviceId) => 0;

    int IMMNotificationClient.OnPropertyValueChanged(string deviceId, PropertyKey key) => 0;
}
