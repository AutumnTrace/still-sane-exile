using System.Runtime.InteropServices;

namespace StillSaneExile.Domain.Capture;

/// <summary>An 8-bit sRGB copy of a frame, made only for a picture the player asked to keep (the stash
/// picture collector), so the frame itself can still be discarded right after. Rows are B G R x, 4 bytes
/// a pixel, no padding. A 16-bit HDR frame is reduced the way <see cref="FrameStats"/> reads it: divided by
/// its SDR white, clamped and encoded as sRGB. <see cref="Dispose"/> clears the pixels.</summary>
public sealed class PictureCopy : IDisposable
{
    private byte[]? _bgrx;

    private PictureCopy(int width, int height, byte[] bgrx, FramePixelFormat source, double blackFraction, double peakScRgb, long timestamp)
    {
        Width = width;
        Height = height;
        _bgrx = bgrx;
        SourceFormat = source;
        BlackFraction = blackFraction;
        PeakScRgb = peakScRgb;
        Timestamp = timestamp;
    }

    public int Width { get; }

    public int Height { get; }

    public int Stride => Width * 4;

    /// <summary>The frame's own format, before the copy reduced it to 8 bits.</summary>
    public FramePixelFormat SourceFormat { get; }

    /// <summary>Share of pixels whose sRGB channels are all 2 or less.</summary>
    public double BlackFraction { get; }

    /// <summary>True when (almost) every pixel is black: what a window that can't be captured gives instead of an error.</summary>
    public bool IsBlank => BlackFraction >= 0.999;

    /// <summary>Largest colour channel in linear scRGB (1.0 = 80 nits), for an HDR frame; 0 for an 8-bit one.</summary>
    public double PeakScRgb { get; }

    /// <summary>When the frame was acquired, in <see cref="System.Diagnostics.Stopwatch"/> ticks.</summary>
    public long Timestamp { get; }

    public bool IsDisposed => _bgrx is null;

    public byte[] Pixels => _bgrx ?? throw new ObjectDisposedException(nameof(PictureCopy), "The picture was saved and cleared.");

    public static PictureCopy Of(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var src = frame.Pixels;
        var w = frame.Width;
        var h = frame.Height;
        var dst = GC.AllocateUninitializedArray<byte>(w * h * 4);
        long black = 0;
        double peak = 0;
        var white = frame.SdrWhite > 0 ? frame.SdrWhite : 1.0;
        for (var y = 0; y < h; y++)
        {
            var row = src.Slice(y * frame.Stride, w * frame.BytesPerPixel);
            var outRow = dst.AsSpan(y * w * 4, w * 4);
            if (frame.Format == FramePixelFormat.Bgra8)
            {
                row.CopyTo(outRow);
                for (var x = 0; x < w; x++)
                {
                    var i = x * 4;
                    outRow[i + 3] = 255;
                    if (outRow[i] <= 2 && outRow[i + 1] <= 2 && outRow[i + 2] <= 2)
                    {
                        black++;
                    }
                }
            }
            else
            {
                var halves = MemoryMarshal.Cast<byte, Half>(row);
                for (var x = 0; x < w; x++)
                {
                    double r = (double)halves[x * 4], g = (double)halves[x * 4 + 1], b = (double)halves[x * 4 + 2];
                    peak = Math.Max(peak, Math.Max(r, Math.Max(g, b)));
                    var i = x * 4;
                    outRow[i] = FrameStats.EncodeSrgb(b / white);
                    outRow[i + 1] = FrameStats.EncodeSrgb(g / white);
                    outRow[i + 2] = FrameStats.EncodeSrgb(r / white);
                    outRow[i + 3] = 255;
                    if (outRow[i] <= 2 && outRow[i + 1] <= 2 && outRow[i + 2] <= 2)
                    {
                        black++;
                    }
                }
            }
        }

        return new PictureCopy(w, h, dst, frame.Format, (double)black / ((long)w * h), peak, frame.Timestamp);
    }

    public void Dispose()
    {
        if (_bgrx is { } p)
        {
            Array.Clear(p);
            _bgrx = null;
        }
    }
}
