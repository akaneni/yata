using System;
using System.IO;
using System.Media;

namespace Yata.Helpers;

/// <summary>A soft two-note chime, synthesized once in memory (no audio files shipped).</summary>
internal static class Chime
{
    // Kept alive for the app's lifetime: async playback reads from the player's buffer.
    private static readonly Lazy<SoundPlayer> Player = new(() =>
    {
        var player = new SoundPlayer(new MemoryStream(Build()));
        player.Load();
        return player;
    });

    public static void Play()
    {
        try
        {
            Player.Value.Play();
        }
        catch (Exception)
        {
            // No audio device; the visual notice is enough.
        }
    }

    private static byte[] Build()
    {
        const int rate = 44100;
        var notes = new (double Frequency, double Start)[] { (1046.5, 0), (1568.0, 0.16) }; // C6, G6
        int samples = (int)(rate * 1.1);
        var pcm = new short[samples];

        foreach (var (frequency, start) in notes)
        {
            for (int i = (int)(start * rate); i < samples; i++)
            {
                double t = i / (double)rate - start;
                double envelope = Math.Min(1, t / 0.01) * Math.Exp(-t * 5);
                double tone = Math.Sin(2 * Math.PI * frequency * t) + 0.25 * Math.Sin(4 * Math.PI * frequency * t);
                pcm[i] = (short)Math.Clamp(pcm[i] + tone * envelope * 5500, short.MinValue, short.MaxValue);
            }
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + samples * 2);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)1); // mono
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples * 2);
        foreach (short sample in pcm)
            writer.Write(sample);
        writer.Flush();
        return stream.ToArray();
    }
}
