using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StillSaneExile.Domain.Capture;

namespace StillSaneExile.CaptureSpike;

/// <summary>The only code in the spike that writes pixels to disk, and only when --dump names a folder.
/// A 16-bit frame is reduced to 8-bit sRGB (divided by its SDR white, clamped) for the PNG.</summary>
internal static class Dump
{
    public static string Png(Frame frame, string folder, string name)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{name}.png");
        byte[] bgra;
        if (frame.Format == FramePixelFormat.Bgra8)
        {
            bgra = frame.Pixels.ToArray();
        }
        else
        {
            bgra = new byte[frame.Width * frame.Height * 4];
            for (var y = 0; y < frame.Height; y++)
            {
                for (var x = 0; x < frame.Width; x++)
                {
                    var (r, g, b) = FrameStats.PixelAt(frame, x, y);
                    var i = (y * frame.Width + x) * 4;
                    (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]) = (b, g, r, 255);
                }
            }
        }

        var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgr32, null, bgra, frame.Width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
        Array.Clear(bgra);
        return path;
    }
}
