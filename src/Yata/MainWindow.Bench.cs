using System;
using System.Text;
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
    /// "aberto" (the same track, notch open on the media page) or "foco" (Pomodoro running).
    /// Null outside measurements.
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
        }
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
