using StillSaneExile.Domain.Capture;

namespace StillSaneExile.CaptureSpike.TestWindows;

/// <summary>A 64×64 block in the top-left of every test window whose colour encodes the number of the
/// frame it was drawn in (12 bits, one nibble per channel). Reading it back from a capture says which
/// frame the backend saw, and finding it at exactly (8, 8) proves the crop is the client area.</summary>
public static class Marker
{
    public const int X = 8;
    public const int Y = 8;
    public const int Size = 64;

    public static (byte R, byte G, byte B) Encode(long frame) =>
        ((byte)((frame & 15) * 16 + 8), (byte)(((frame >> 4) & 15) * 16 + 8), (byte)(((frame >> 8) & 15) * 16 + 8));

    public static int? Decode(byte r, byte g, byte b)
    {
        static int? Nibble(byte v)
        {
            var n = v / 16;
            return Math.Abs(v - (n * 16 + 8)) <= 5 ? n : null;
        }

        return Nibble(r) is { } a && Nibble(g) is { } c && Nibble(b) is { } d ? a | (c << 4) | (d << 8) : null;
    }

    /// <summary>The frame number the capture shows, or null when the marker is not where it should be:
    /// inside the block it decodes, one pixel outside its top-left corner it does not.</summary>
    public static int? Read(Frame frame, double scale = 1.0)
    {
        int x = (int)Math.Round(X * scale), y = (int)Math.Round(Y * scale), s = (int)Math.Round(Size * scale);
        if (frame.Width < x + s || frame.Height < y + s)
        {
            return null;
        }

        var inside = FrameStats.PixelAt(frame, x + 2, y + 2);
        var centre = FrameStats.PixelAt(frame, x + s / 2, y + s / 2);
        var outside = FrameStats.PixelAt(frame, x - 3, y - 3);
        var a = Decode(inside.R, inside.G, inside.B);
        var b = Decode(centre.R, centre.G, centre.B);
        var background = Decode(outside.R, outside.G, outside.B) is null || outside != inside;
        return a is not null && a == b && background ? a : null;
    }

    /// <summary>How many frames behind <paramref name="latest"/> the captured <paramref name="seen"/> is.</summary>
    public static int Behind(long latest, int seen) => (int)(((latest - seen) % 4096 + 4096) % 4096);

    /// <summary>A calm background that drifts slowly, so the window visibly runs without flashing.</summary>
    public static (float R, float G, float B) Background(double seconds) =>
        ((float)(0.10 + 0.04 * Math.Sin(seconds * 0.7)), 0.12f, (float)(0.16 + 0.04 * Math.Cos(seconds * 0.5)));
}
