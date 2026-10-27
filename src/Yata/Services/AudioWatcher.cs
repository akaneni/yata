using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Yata.Interop;
using Yata.Helpers;

namespace Yata.Services;

/// <summary>
/// Reports master volume / mute changes and switches of the default output device
/// (e.g. headphones connecting). Core Audio calls back on worker threads; messages
/// are raised on the UI dispatcher.
/// </summary>
[ComVisible(true)]
internal sealed class AudioWatcher : IAudioEndpointVolumeCallback, IMMNotificationClient, IDisposable
{
    private const int CLSCTX_ALL = 0x17;
    private const int STGM_READ = 0;
    private const uint FormFactorHeadphones = 3;
    private const uint FormFactorHeadset = 5;

    private readonly Dispatcher _dispatcher;
    private IMMDeviceEnumerator? _enumerator;
    private IAudioEndpointVolume? _volume;
    private string? _deviceId;

    public event Action<HudMessage>? Message;

    public AudioWatcher(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public void Start()
    {
        try
        {
            _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            _enumerator.RegisterEndpointNotificationCallback(this);
            BindDefaultDevice(announce: false);
        }
        catch (COMException ex)
        {
            // No audio subsystem; volume HUD stays off.
            Log.Warn("audio", "Core Audio indisponível; avisos de volume desligados", ex);
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

    private void BindDefaultDevice(bool announce)
    {
        if (_enumerator == null)
            return;

        Unbind();
        IMMDevice device;
        try
        {
            _enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device);
        }
        catch (COMException)
        {
            return; // No output device connected.
        }

        try
        {
            device.GetId(out var id);
            var iid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var instance);
            _volume = (IAudioEndpointVolume)instance;
            _volume.RegisterControlChangeNotify(this);

            bool changed = _deviceId != null && _deviceId != id;
            _deviceId = id;
            if (announce && changed)
            {
                var (name, isHeadphones) = Describe(device);
                Message?.Invoke(new HudMessage(isHeadphones ? Glyphs.Headphones : Glyphs.Speakers, name, Kind: HudKind.AudioDevice));
            }
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

    private static (string Name, bool IsHeadphones) Describe(IMMDevice device)
    {
        string name = Strings.Hud_AudioOutput;
        bool isHeadphones = false;
        try
        {
            device.OpenPropertyStore(STGM_READ, out var store);
            try
            {
                var key = PropertyKey.DeviceFriendlyName;
                store.GetValue(ref key, out var value);
                name = value.AsString() ?? name;
                PropVariant.PropVariantClear(ref value);

                key = PropertyKey.AudioEndpointFormFactor;
                store.GetValue(ref key, out value);
                isHeadphones = value.AsUInt() is FormFactorHeadphones or FormFactorHeadset;
                PropVariant.PropVariantClear(ref value);
            }
            finally
            {
                Marshal.ReleaseComObject(store);
            }
        }
        catch (COMException)
        {
        }
        return (name, isHeadphones);
    }

    // ---- Core Audio callbacks (worker threads) ----------------------------------------

    int IAudioEndpointVolumeCallback.OnNotify(IntPtr notifyData)
    {
        var data = Marshal.PtrToStructure<AudioVolumeNotificationData>(notifyData);
        _dispatcher.InvokeAsync(() => Message?.Invoke(VolumeMessage(data.MasterVolume, data.Muted)));
        return 0;
    }

    int IMMNotificationClient.OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId)
    {
        // Fires once per role; react to one of them only.
        if (flow == EDataFlow.Render && role == ERole.Multimedia)
            _dispatcher.InvokeAsync(() => BindDefaultDevice(announce: true));
        return 0;
    }

    int IMMNotificationClient.OnDeviceStateChanged(string deviceId, int newState) => 0;

    int IMMNotificationClient.OnDeviceAdded(string deviceId) => 0;

    int IMMNotificationClient.OnDeviceRemoved(string deviceId) => 0;

    int IMMNotificationClient.OnPropertyValueChanged(string deviceId, PropertyKey key) => 0;

    private static HudMessage VolumeMessage(float level, bool muted)
    {
        int percent = (int)Math.Round(level * 100);
        string glyph = muted || percent == 0 ? Glyphs.Mute
                     : percent < 34 ? Glyphs.Volume1
                     : percent < 67 ? Glyphs.Volume2
                     : Glyphs.Volume3;
        return muted
            ? new HudMessage(glyph, Strings.Hud_Muted, null, 0, Kind: HudKind.Volume)
            : new HudMessage(glyph, Strings.Hud_Volume, $"{percent}", level, Kind: HudKind.Volume);
    }
}
