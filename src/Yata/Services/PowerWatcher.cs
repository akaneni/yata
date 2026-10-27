using System;
using System.Windows.Forms;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Yata.Services;

/// <summary>Charger plugged/unplugged and low-battery warnings (20% and 10%).</summary>
internal sealed class PowerWatcher : IDisposable
{
    private static readonly int[] LowThresholds = { 20, 10 };

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };
    private PowerLineStatus _line;
    private int _lastWarning = int.MaxValue;
    private bool _started;

    public event Action<HudMessage>? Message;

    public PowerWatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _timer.Tick += (_, _) => Check();
    }

    public void Start()
    {
        var status = SystemInformation.PowerStatus;
        if (status.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery))
            return; // Desktop PC.

        _line = status.PowerLineStatus;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _timer.Start();
        _started = true;
    }

    public void Dispose()
    {
        if (!_started)
            return;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _timer.Stop();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.StatusChange)
            _dispatcher.InvokeAsync(Check);
    }

    private void Check()
    {
        var status = SystemInformation.PowerStatus;
        float fraction = status.BatteryLifePercent;
        if (fraction is < 0 or > 1)
            return; // Unknown charge.

        int percent = (int)Math.Round(fraction * 100);

        if (status.PowerLineStatus != _line && status.PowerLineStatus != PowerLineStatus.Unknown)
        {
            _line = status.PowerLineStatus;
            if (_line == PowerLineStatus.Online)
            {
                _lastWarning = int.MaxValue;
                Message?.Invoke(new HudMessage(Glyphs.Charging, Strings.Hud_Charging, $"{percent}%", fraction, Kind: HudKind.Power));
            }
            else
            {
                Message?.Invoke(new HudMessage(Glyphs.Battery, Strings.Hud_OnBattery, $"{percent}%", fraction, Kind: HudKind.Power));
            }
            return;
        }

        if (_line != PowerLineStatus.Offline)
            return;

        foreach (int threshold in LowThresholds)
        {
            if (percent <= threshold && _lastWarning > threshold)
            {
                _lastWarning = threshold;
                Message?.Invoke(new HudMessage(Glyphs.BatteryLow,
                    threshold <= 10 ? Strings.Hud_BatteryVeryLow : Strings.Hud_BatteryLow, $"{percent}%", fraction, Warning: true, Kind: HudKind.Power));
            }
        }
    }
}
