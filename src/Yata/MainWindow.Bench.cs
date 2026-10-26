using System;
using System.Text;
using System.Threading;
using System.Windows.Threading;

namespace Yata;

// Fixed states for tools/measure.ps1 (--bench <state>): the media sessions of the PC are ignored and
// nothing goes to the network, so every run shows the same thing.
public partial class MainWindow
{
    private const string BenchArtist = "Yata";
    private const string BenchTitle = "Medição";
    private static readonly TimeSpan BenchTrackLength = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan BenchLineEvery = TimeSpan.FromSeconds(4);

    /// <summary>
    /// "parado" (just the clock), "musica" (a track with synced lyrics, notch closed),
    /// "aberto" (the same track, notch open on the media page), "foco" (Pomodoro running) or
    /// "dormindo" (Yata asleep in the closed notch);
    /// "erro" and "queda" throw on purpose, to check the error handling. Null outside measurements.
    /// </summary>
    internal string? Bench { get; init; }

    private void StartBench(string state)
    {
        switch (state)
        {
            case "musica":
                SimulateTrack();
                break;
            case "aberto":
                SimulateTrack();
                Dispatcher.InvokeAsync(() => ShowPage("media", 300), DispatcherPriority.ApplicationIdle);
                break;
            case "foco":
                _focus.Start();
                break;
            case "dormindo":
                // Nothing playing and the user long gone: Yata dozes in the closed notch.
                Settings.CompanionEnabled = true;
                _presence.Simulate(TimeSpan.FromMinutes(30));
                break;
            // The safety net (App.CatchUnhandledErrors): a UI error must be survived, a background one
            // must leave a crash file.
            case "erro":
                After(TimeSpan.FromSeconds(2), () => throw new InvalidOperationException("Erro de teste na interface (--bench erro)"));
                break;
            case "queda":
                After(TimeSpan.FromSeconds(2), () => new Thread(() =>
                    throw new InvalidOperationException("Erro de teste fora da interface (--bench queda)")).Start());
                break;
        }
    }

    private void After(TimeSpan delay, Action action)
    {
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
    }

    private void SimulateTrack()
    {
        var lrc = new StringBuilder();
        int line = 1;
        for (var at = TimeSpan.Zero; at < BenchTrackLength; at += BenchLineEvery)
            lrc.AppendLine($"[{(int)at.TotalMinutes:00}:{at.Seconds:00}.00] Linha {line++} da letra de medição");
        _lyricsService.Seed(BenchArtist, BenchTitle, lrc.ToString());
        _media.Simulate(BenchTitle, BenchArtist, BenchTrackLength);
    }
}
