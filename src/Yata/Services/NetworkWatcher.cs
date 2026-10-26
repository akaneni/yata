using System;
using System.Windows.Threading;
using Windows.Networking.Connectivity;
using Yata.Helpers;

namespace Yata.Services;

/// <summary>Internet connected / lost, with the network name when Windows provides it.</summary>
internal sealed class NetworkWatcher : IDisposable
{
    private readonly Dispatcher _dispatcher;
    // Connectivity flaps while a network comes up; only report the state it settles on.
    private readonly DispatcherTimer _settle = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private bool _connected;
    private string? _name;

    public event Action<HudMessage>? Message;

    public NetworkWatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _settle.Tick += (_, _) => { _settle.Stop(); Check(); };
    }

    public void Start()
    {
        (_connected, _name, _) = Read();
        NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
    }

    public void Dispose()
    {
        NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;
        _settle.Stop();
    }

    private void OnNetworkStatusChanged(object sender) => _dispatcher.InvokeAsync(() =>
    {
        _settle.Stop();
        _settle.Start();
    });

    private void Check()
    {
        var (connected, name, isWifi) = Read();
        if (connected == _connected && name == _name)
            return;

        _connected = connected;
        _name = name;
        Message?.Invoke(connected
            ? new HudMessage(isWifi ? Glyphs.Wifi : Glyphs.Ethernet, string.IsNullOrEmpty(name) ? "Conectado" : name, Kind: HudKind.Network)
            : new HudMessage(Glyphs.Offline, "Sem internet", Warning: true, Kind: HudKind.Network));
    }

    private static (bool Connected, string? Name, bool IsWifi) Read()
    {
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile == null)
                return (false, null, false);

            bool connected = profile.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;
            return (connected, connected ? profile.ProfileName : null, profile.IsWlanConnectionProfile);
        }
        catch (Exception ex)
        {
            Log.Warn("network", "Falha ao ler o estado da rede", ex);
            return (false, null, false);
        }
    }
}
