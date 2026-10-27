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

// Yata the raven: ambient perch, nodding, news while you were away.
public partial class MainWindow
{
    // From 3 px below its place to 6 px above, fading out, in 2.2 s.
    private const int ZzzStart = 3;
    private const int ZzzRise = 9;
    private readonly DispatcherTimer _zzzTimer = new() { Interval = TimeSpan.FromMilliseconds(2200.0 / (ZzzRise + 1)) };
    private int _zzzStep;

    private void UpdateCompanion()
    {
        bool idle = Settings.CompanionEnabled && _presence.State != Presence.Active;

        // Music still playing while you're away: Yata takes the equalizer's slot and nods along.
        bool nodding = idle && _state?.IsPlaying == true && !_focus.IsActive;
        MusicRaven.Visibility = nodding ? Visibility.Visible : Visibility.Collapsed;
        if (nodding)
            _musicRaven.Nod();
        else
            _musicRaven.Stop();

        // Nothing playing (a paused player counts): the clock gives way to Yata, who eventually dozes off.
        bool ambient = idle && _state?.IsPlaying != true && IsVisible;
        if (ambient)
        {
            if (_ambientLeaving)
                CancelLeave();
            _ambientShown = true;
            bool asleep = _presence.State == Presence.Asleep;
            if (asleep)
                _ambientRaven.Sleep();
            else
                _ambientRaven.Perch();
            SetZzz(asleep);
        }
        else if (_ambientShown && !_ambientLeaving)
        {
            LeaveAmbient();
        }

        UpdateCompactTimer();
        UpdateTargets();
    }

    /// <summary>You're back: Yata flaps off to the right and the clock returns.</summary>
    private void LeaveAmbient()
    {
        _ambientLeaving = true;
        SetZzz(false);
        _ambientRaven.Fly();
        var flight = new DoubleAnimation(0, 150, Motion.Ms(700))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        flight.Completed += (_, _) =>
        {
            if (!_ambientLeaving)
                return;
            CancelLeave();
            _ambientShown = false;
            _ambientRaven.Stop();
            UpdateTargets();
        };
        AmbientRavenShift.BeginAnimation(TranslateTransform.XProperty, flight);
    }

    private void CancelLeave()
    {
        _ambientLeaving = false;
        AmbientRavenShift.BeginAnimation(TranslateTransform.XProperty, null);
        AmbientRavenShift.X = 0;
    }

    /// <summary>
    /// The "z" drifts up a pixel at a time and fades, over and over while Yata sleeps. A timer
    /// steps it: an endless WPF animation would keep frames coming for as long as the user is
    /// away, which can be all night.
    /// </summary>
    private void SetZzz(bool on)
    {
        Zzz.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on == _zzzTimer.IsEnabled)
            return;
        if (!on)
        {
            _zzzTimer.Stop();
            return;
        }
        _zzzStep = 0;
        StepZzz();
        _zzzTimer.Start();
    }

    private void StepZzz()
    {
        ZzzShift.Y = ZzzStart - _zzzStep;
        Zzz.Opacity = 1 - (double)_zzzStep / ZzzRise;
        _zzzStep = (_zzzStep + 1) % (ZzzRise + 1);
    }

    private void OnWentAway(TimeSpan unattended)
    {
        // Pause focus so time away isn't counted — unless something is playing (likely a video).
        if (Settings.PauseFocusWhenAway && _state?.IsPlaying != true && _focus.PauseAfterIdle(unattended))
            _presence.Add(Glyphs.Timer, "Foco pausado quando você saiu");
    }

    private void OnReturned(AwayReport report)
    {
        if (Settings.AwaySummaryEnabled && report.Items.Count > 0 && IsVisible && !_expanded)
            ShowNews(report);
    }

    private void ShowNews(AwayReport report)
    {
        var items = report.Items.Skip(Math.Max(0, report.Items.Count - NewsMaxItems)).ToList();
        _newsCount = items.Count;
        NewsList.ItemsSource = items;
        NewsTitle.Text = report.Items.Count > items.Count
            ? $"Enquanto você esteve fora · {FormatAway(report.Duration)} · {report.Items.Count} avisos"
            : $"Enquanto você esteve fora · {FormatAway(report.Duration)}";

        _hudActive = false;
        _hudTimer.Stop();
        _newsActive = true;
        _newsTimer.Stop();
        _newsTimer.Start();
        UpdateTargets();

        // Yata flies in with the news in a letter, lands, turns to face it, then the text appears.
        NewsRaven.Letter = true;
        _newsRaven.Fly();
        NewsRaven.FacingLeft = true;
        var flight = new DoubleAnimation(NewsWidth, 0, Motion.Ms(750))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        flight.Completed += (_, _) =>
        {
            if (_newsActive)
                _newsRaven.Land();
        };
        NewsRavenShift.BeginAnimation(TranslateTransform.XProperty, flight);
        NewsBody.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.Ms(400))
        {
            BeginTime = Motion.Ms(450),
        });
    }

    private void HideNews()
    {
        if (!_newsActive)
            return;
        _newsActive = false;
        _newsTimer.Stop();
        _newsRaven.Stop();
        NewsRaven.Letter = false;
        UpdateTargets();
    }

    /// <summary>Walks through Yata's states in ~30 s instead of waiting real minutes.</summary>
    public async void RunCompanionDemo()
    {
        SetExpanded(false);
        _presence.Simulate(_presence.IdleAfter);
        await Task.Delay(7000);
        _presence.Simulate(_presence.SleepAfter + TimeSpan.FromMinutes(37));
        _presence.Add(Glyphs.Bell, "Reunião com o time · 15:30");
        _presence.Add(Glyphs.Water, "Hora de beber água · 750/2000 ml");
        await Task.Delay(7000);
        _presence.Simulate(TimeSpan.Zero); // "You're back."
        await Task.Delay(1500);
        _presence.Simulate(null);
        await Task.Delay(9000); // Let the news run out.
        ShowHud(new HudMessage(Glyphs.Timer, "Hora da pausa", "5 min", DurationMs: 4000, Celebrate: true));
        await Task.Delay(4500);
        ShowHud(new HudMessage(Glyphs.Water, "Meta de água batida", "2000/2000 ml", 1, DurationMs: 3500, Celebrate: true));
        await Task.Delay(4000);
        ShowHud(new HudMessage(Glyphs.Bell, "Reunião com o time", "15:30", DurationMs: 4000, Important: true));
    }

    private static string FormatAway(TimeSpan duration) =>
        duration.TotalHours >= 1 ? $"{(int)duration.TotalHours}h{duration.Minutes:00}" : $"{Math.Max(1, (int)duration.TotalMinutes)} min";
}
