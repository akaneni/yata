using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Yata.Helpers;
using Yata.Interop;
using Yata.Services;
using Yata.Views;
using Glyphs = Yata.Services.Glyphs;
using MediaState = Yata.Services.MediaState;

namespace Yata;

// Transient status notices in the closed notch.
public partial class MainWindow
{
    private readonly DispatcherTimer _hudRavenLand = new() { Interval = TimeSpan.FromMilliseconds(1100) };
    // A reminder that arrived while the notch was open or hidden; shown once it can be seen.
    private HudMessage? _pendingHud;

    private void ShowHud(HudMessage message)
    {
        if (!Settings.Shows(message.Kind))
            return;
        _presence.NoteIfUnseen(message);

        if (!IsVisible || _expanded)
        {
            // Reminders wait until the notch closes or a full-screen app exits; status blips are dropped.
            if (message.Important)
                _pendingHud = message;
            return;
        }

        _hudTimer.Interval = TimeSpan.FromMilliseconds(message.DurationMs);

        HudIcon.Text = message.Glyph;
        HudIcon.SetResourceReference(TextBlock.ForegroundProperty,
            message.Warning ? "WarningBrush"
            : message.Kind == HudKind.Privacy ? (message.Glyph == Glyphs.Camera ? "CameraBrush" : "MicrophoneBrush")
            : "TextPrimary");
        // Yata delivers reminders himself (he is a messenger, after all) and celebrates small wins;
        // routine status blips keep their plain icon.
        bool caw = message.Important && !message.Celebrate && message.Kind == HudKind.Reminder;
        bool raven = message.Celebrate || caw;
        HudIcon.Visibility = raven ? Visibility.Collapsed : Visibility.Visible;
        HudRaven.Visibility = raven ? Visibility.Visible : Visibility.Collapsed;
        _hudRavenLand.Stop();
        if (message.Celebrate && message.Glyph == Glyphs.Water)
        {
            _hudRaven.Shake();
        }
        else if (message.Celebrate)
        {
            // A quick flap, then Yata lands and settles for the rest of the message.
            _hudRaven.Fly();
            _hudRavenLand.Start();
        }
        else if (caw)
        {
            _hudRaven.Caw();
        }
        else
        {
            StopHudRaven();
        }
        HudText.Text = message.Text;
        HudValue.Text = message.Value ?? "";
        HudValue.Visibility = message.Value == null ? Visibility.Collapsed : Visibility.Visible;
        HudBar.Visibility = message.Level.HasValue ? Visibility.Visible : Visibility.Collapsed;
        if (message.Level is double level)
        {
            HudBarFill.BeginAnimation(WidthProperty,
                new DoubleAnimation(Math.Clamp(level, 0, 1) * HudBar.Width, TimeSpan.FromMilliseconds(120)));
        }

        _hudActive = true;
        _hudTimer.Stop();
        _hudTimer.Start();
        UpdateTargets();
    }

    private void ShowPendingHud()
    {
        if (_pendingHud is not HudMessage message)
            return;
        _pendingHud = null;
        ShowHud(message);
    }

    private void HideHud()
    {
        _hudTimer.Stop();
        _hudActive = false;
        StopHudRaven();
        UpdateTargets();
    }

    private void StopHudRaven()
    {
        _hudRavenLand.Stop();
        _hudRaven.Stop();
    }
}
