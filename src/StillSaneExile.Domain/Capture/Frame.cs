namespace StillSaneExile.Domain.Capture;

public enum FramePixelFormat
{
    /// <summary>8 bits per channel, B G R A, sRGB-encoded. What an SDR display shows.</summary>
    Bgra8,

    /// <summary>16-bit float per channel, R G B A, linear scRGB: 1.0 is 80 nits, and SDR white sits at
    /// <see cref="Frame.SdrWhite"/>. What an HDR display needs so highlights are not clipped.</summary>
    RgbaHalf,
}

/// <summary>One image of a window's client area, held in memory only. <see cref="Dispose"/> clears the
/// pixels and drops them, so a frame cannot outlive the analysis it was taken for.</summary>
public sealed class Frame : IDisposable
{
    private byte[]? _pixels;

    /// <param name="pixels">Rows top to bottom, <paramref name="stride"/> bytes apart. The frame owns the array.</param>
    /// <param name="timestamp">When the frame was acquired, in <see cref="System.Diagnostics.Stopwatch"/> ticks.</param>
    /// <param name="sdrWhite">For <see cref="FramePixelFormat.RgbaHalf"/>: the scRGB value of SDR white on
    /// the display it came from (1.0 with HDR off; 2.5 for a 200-nit SDR white).</param>
    public Frame(int width, int height, int stride, FramePixelFormat format, byte[] pixels, long timestamp, double sdrWhite = 1.0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, width * BytesPer(format));
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length < (long)stride * height)
        {
            throw new ArgumentException("The pixel buffer is smaller than stride × height.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Stride = stride;
        Format = format;
        Timestamp = timestamp;
        SdrWhite = sdrWhite;
        _pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public int Stride { get; }

    public FramePixelFormat Format { get; }

    public long Timestamp { get; }

    public double SdrWhite { get; }

    public int BytesPerPixel => BytesPer(Format);

    public bool IsDisposed => _pixels is null;

    public ReadOnlySpan<byte> Pixels => _pixels ?? throw new ObjectDisposedException(nameof(Frame), "The frame was discarded after analysis.");

    public void Dispose()
    {
        if (_pixels is { } p)
        {
            Array.Clear(p);
            _pixels = null;
        }
    }

    public static int BytesPer(FramePixelFormat format) => format == FramePixelFormat.RgbaHalf ? 8 : 4;
}

/// <summary>A backend that can take one frame of one window.</summary>
public interface IFrameSource
{
    string Name { get; }

    /// <summary>Takes exactly one frame of <paramref name="window"/>'s client area, or returns null when
    /// it cannot (minimized, closed, black, timed out). Nothing keeps capturing after it returns: no
    /// session, frame pool, duplication or timer is left running. It blocks, so call it off the input
    /// hook's thread.</summary>
    Frame? CaptureOnce(nint window);
}
