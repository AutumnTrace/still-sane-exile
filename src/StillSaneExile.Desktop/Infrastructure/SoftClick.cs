using System.IO;
using System.Media;

namespace StillSaneExile.Desktop.Infrastructure;

/// <summary>The optional soft click when the quick price key is heard (Settings → Price card, off by
/// default): a short, quiet tick that SSE makes itself, so there is no sound file to ship. Played through
/// Windows' sound player without waiting for it; it goes to the speakers and nowhere else.</summary>
internal static class SoftClick
{
    private const int Rate = 44100;

    private static SoundPlayer? _player;

    public static void Play()
    {
        try
        {
            (_player ??= Make()).Play();
        }
        catch (InvalidOperationException)
        {
            // Windows could not play it (no sound device): the ring still answers the press.
        }
    }

    /// <summary>18 ms of a 2.2 kHz tone with a little noise, dying away fast, at a fifth of full volume.</summary>
    private static SoundPlayer Make()
    {
        var count = Rate * 18 / 1000;
        var noise = new Random(7);
        using var pcm = new MemoryStream();
        using (var w = new BinaryWriter(pcm, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            for (var i = 0; i < count; i++)
            {
                var t = i / (double)Rate;
                var tone = (Math.Sin(2 * Math.PI * 2200 * t) * 0.65) + ((noise.NextDouble() - 0.5) * 0.7);
                w.Write((short)(tone * Math.Exp(-t / 0.0028) * 0.2 * short.MaxValue));
            }
        }

        var data = pcm.ToArray();
        var wav = new MemoryStream();
        using (var w = new BinaryWriter(wav, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            w.Write("RIFF"u8);
            w.Write(36 + data.Length);
            w.Write("WAVE"u8);
            w.Write("fmt "u8);
            w.Write(16);
            w.Write((short)1); // PCM
            w.Write((short)1); // mono
            w.Write(Rate);
            w.Write(Rate * 2);
            w.Write((short)2);
            w.Write((short)16);
            w.Write("data"u8);
            w.Write(data.Length);
            w.Write(data);
        }

        wav.Position = 0;
        var player = new SoundPlayer(wav);
        player.Load();
        return player;
    }
}
