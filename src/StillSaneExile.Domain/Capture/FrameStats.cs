using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace StillSaneExile.Domain.Capture;

/// <summary>The capture spike's stand-in for analysis: a checksum and the average colour. Both pixel
/// formats are reduced to 8-bit sRGB first (an HDR frame by dividing by its SDR white and clamping),
/// so the same window gives the same average whichever format it was captured in.</summary>
/// <param name="Checksum">First 16 hex digits of the SHA-256 of the pixel rows.</param>
/// <param name="BlackFraction">Share of pixels whose sRGB channels are all 2 or less.</param>
/// <param name="PeakScRgb">Largest colour channel in linear scRGB (1.0 = 80 nits). Above
/// <see cref="Frame.SdrWhite"/> means HDR highlights that an 8-bit frame would have clipped.</param>
public sealed record FrameStats(int Width, int Height, FramePixelFormat Format, string Checksum, byte AverageR, byte AverageG, byte AverageB, double BlackFraction, double PeakScRgb)
{
    private const int LutSize = 16384;
    private static readonly byte[] EncodeLut = BuildEncodeLut();
    private static readonly double[] DecodeLut = BuildDecodeLut();

    /// <summary>True when the frame is (almost) entirely black, which is what a backend that cannot see
    /// a window's content usually returns instead of failing.</summary>
    public bool IsBlank => BlackFraction >= 0.999;

    public string AverageHex => $"#{AverageR:X2}{AverageG:X2}{AverageB:X2}";

    public static FrameStats Of(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var pixels = frame.Pixels;
        long r = 0, g = 0, b = 0, black = 0;
        double peak = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var rowBytes = frame.Width * frame.BytesPerPixel;
        for (var y = 0; y < frame.Height; y++)
        {
            var row = pixels.Slice(y * frame.Stride, rowBytes);
            hash.AppendData(row);
            for (var x = 0; x < frame.Width; x++)
            {
                var (pr, pg, pb, lin) = Read(frame, row, x);
                r += pr;
                g += pg;
                b += pb;
                peak = Math.Max(peak, lin);
                if (pr <= 2 && pg <= 2 && pb <= 2)
                {
                    black++;
                }
            }
        }

        var n = (long)frame.Width * frame.Height;
        var digest = Convert.ToHexString(hash.GetHashAndReset())[..16];
        return new FrameStats(frame.Width, frame.Height, frame.Format, digest, (byte)(r / n), (byte)(g / n), (byte)(b / n), (double)black / n, peak);
    }

    /// <summary>The pixel at (<paramref name="x"/>, <paramref name="y"/>) as 8-bit sRGB.</summary>
    public static (byte R, byte G, byte B) PixelAt(Frame frame, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)x, (uint)frame.Width, nameof(x));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)y, (uint)frame.Height, nameof(y));
        var row = frame.Pixels.Slice(y * frame.Stride, frame.Width * frame.BytesPerPixel);
        var (r, g, b, _) = Read(frame, row, x);
        return (r, g, b);
    }

    /// <summary>Linear light (0..1 of SDR white) to an 8-bit sRGB value.</summary>
    public static byte EncodeSrgb(double linear) =>
        linear <= 0 ? (byte)0 : linear >= 1 ? (byte)255 : EncodeLut[(int)(linear * (LutSize - 1) + 0.5)];

    /// <summary>An 8-bit sRGB value to linear light (0..1).</summary>
    public static double DecodeSrgb(byte value) => DecodeLut[value];

    private static (byte R, byte G, byte B, double Linear) Read(Frame frame, ReadOnlySpan<byte> row, int x)
    {
        if (frame.Format == FramePixelFormat.Bgra8)
        {
            var p = row.Slice(x * 4, 4);
            return (p[2], p[1], p[0], DecodeLut[Math.Max(p[0], Math.Max(p[1], p[2]))]);
        }

        var h = MemoryMarshal.Cast<byte, Half>(row.Slice(x * 8, 8));
        double lr = (double)h[0], lg = (double)h[1], lb = (double)h[2];
        var white = frame.SdrWhite > 0 ? frame.SdrWhite : 1.0;
        return (EncodeSrgb(lr / white), EncodeSrgb(lg / white), EncodeSrgb(lb / white), Math.Max(lr, Math.Max(lg, lb)));
    }

    private static byte[] BuildEncodeLut()
    {
        var lut = new byte[LutSize];
        for (var i = 0; i < LutSize; i++)
        {
            var l = (double)i / (LutSize - 1);
            var s = l <= 0.0031308 ? 12.92 * l : 1.055 * Math.Pow(l, 1 / 2.4) - 0.055;
            lut[i] = (byte)Math.Clamp((int)Math.Round(s * 255), 0, 255);
        }

        return lut;
    }

    private static double[] BuildDecodeLut()
    {
        var lut = new double[256];
        for (var i = 0; i < 256; i++)
        {
            var s = i / 255.0;
            lut[i] = s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return lut;
    }
}
