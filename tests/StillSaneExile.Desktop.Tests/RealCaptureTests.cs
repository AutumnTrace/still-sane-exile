using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Domain.Capture;

namespace StillSaneExile.Desktop.Tests;

/// <summary>A test that needs a real capture: skipped, not failed, where there is no GPU, no desktop or no
/// Windows.Graphics.Capture (GitHub's windows-latest runners among them), or with SSE_CAPTURE_TESTS=0.</summary>
public sealed class RealCaptureFactAttribute : FactAttribute
{
    private static readonly Lazy<string?> WhyNot = new(() =>
    {
        try
        {
            return Environment.GetEnvironmentVariable("SSE_CAPTURE_TESTS") == "0" ? "SSE_CAPTURE_TESTS=0"
                : !Environment.UserInteractive ? "no interactive desktop"
                : new WgcFrameSource().Unsupported ?? WgcFrameSource.GraphicsProblem();
        }
        catch (Exception e)
        {
            return "the check failed: " + e.Message;
        }
    });

    public RealCaptureFactAttribute()
    {
        if (WhyNot.Value is { } reason)
        {
            Skip = "Needs a real capture: " + reason;
        }
    }
}

/// <summary>The app's own Windows.Graphics.Capture source against a window this test owns: a solid colour
/// with a 1-pixel red edge under a title bar, so the crop to the client area shows to the pixel.</summary>
public class RealCaptureTests
{
    private static readonly Color Fill = Color.FromRgb(0x30, 0x80, 0xC0);

    private static (Window Window, nint Handle) OpenWindow()
    {
        var window = new Window
        {
            Title = "SSE capture test",
            Width = 480,
            Height = 300,
            Left = 80,
            Top = 80,
            WindowStartupLocation = WindowStartupLocation.Manual,
            ShowActivated = false,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(Fill),
            Content = new System.Windows.Controls.Border { BorderBrush = Brushes.Red, BorderThickness = new Thickness(1), SnapsToDevicePixels = true },
        };
        window.Show();
        var until = DateTime.UtcNow.AddMilliseconds(400); // let it render and the desktop compose it
        while (DateTime.UtcNow < until)
        {
            Sta.Drain();
            Thread.Sleep(10);
        }

        return (window, new WindowInteropHelper(window).Handle);
    }

    private static (byte B, byte G, byte R) At(PictureCopy copy, int x, int y)
    {
        var i = y * copy.Stride + x * 4;
        return (copy.Pixels[i], copy.Pixels[i + 1], copy.Pixels[i + 2]);
    }

    [RealCaptureFact]
    public void Wgc_takes_one_frame_of_the_client_area_and_closes_its_session() => Sta.Run(() =>
    {
        var (window, handle) = OpenWindow();
        using var source = new WgcFrameSource();
        try
        {
            var capture = Task.Run<(PictureCopy? Copy, CaptureFacts? Facts)>(() =>
            {
                using var frame = source.CaptureOnce(handle);
                return frame is null ? (null, null) : (PictureCopy.Of(frame), source.FactsFor(frame));
            });
            Sta.Until(() => capture.IsCompleted, "the capture");
            var (copy, facts) = capture.Result;

            Assert.True(copy is not null, source.LastProblem);
            using (copy)
            {
                Assert.NotNull(facts);
                Assert.Equal((facts.Window.ClientWidth, facts.Window.ClientHeight), (copy.Width, copy.Height));
                Assert.Equal("windowed", facts.Window.WindowMode);
                Assert.False(copy.IsBlank);

                // Every corner is the fill: the title bar and borders were cut off.
                // The client area's 1-pixel red edge is exactly the picture's edge, and the fill is inside
                // it: the title bar and borders were cut off, and nothing of the client area was. (Not the
                // bottom corners: Windows 11 rounds the window's corners there.)
                var (w, h) = (copy.Width - 1, copy.Height - 1);
                foreach (var (x, y) in new[] { (0, 0), (w, 0), (w / 2, 0), (w / 2, h), (0, h / 2), (w, h / 2) })
                {
                    Assert.Equal(((byte)0, (byte)0, (byte)255), At(copy, x, y));
                }

                foreach (var (x, y) in new[] { (1, 1), (w - 1, 1), (w / 2, h - 1), (1, h / 2), (w - 1, h / 2), (w / 2, h / 2) })
                {
                    var (b, g, r) = At(copy, x, y);
                    Assert.InRange(r, Fill.R - 2, Fill.R + 2);
                    Assert.InRange(g, Fill.G - 2, Fill.G + 2);
                    Assert.InRange(b, Fill.B - 2, Fill.B + 2);
                }
            }

            Assert.Equal(1, source.Captures);
            Assert.Equal(1, source.Acquisitions);
            Assert.Equal(1, source.SessionsOpened);
            Assert.Equal(1, source.SessionsClosed);
            Assert.Equal(0, source.Timeouts);
        }
        finally
        {
            window.Close();
        }
    });

    [RealCaptureFact]
    public void A_held_stash_key_saves_one_real_picture() => Sta.Run(() =>
    {
        var (window, handle) = OpenWindow();
        var source = new WgcFrameSource();
        try
        {
            using var rig = new StashRig(s => s.CollectStashPictures = true, source);
            rig.Foreground.Switch(ForegroundKind.Game, window: handle); // stands in for the game

            rig.Press();
            for (var i = 0; i < 300; i++)
            {
                rig.Press(); // ten seconds of auto-repeat
            }

            rig.Release();
            rig.Settle();

            Assert.Equal("Saved stash picture 1", Assert.Single(rig.Toast.Shown));
            Assert.Equal(1, source.Captures);
            Assert.Equal(1, source.Acquisitions);
            Assert.Equal(1, source.SessionsOpened);
            Assert.Equal(1, source.SessionsClosed);
            Assert.Equal(300, rig.Collector.Reader.GateCounters.AutoRepeatIgnored);
            Assert.Equal(0, rig.Collector.Reader.Counters.FramesInMemory);
            var notes = Assert.Single(rig.Pictures.Pictures).Picture.Notes;
            Assert.Equal("Windows.Graphics.Capture", notes.Capture!.Backend);
            Assert.Equal((notes.GameWindow!.ClientWidth, notes.GameWindow.ClientHeight), (notes.Picture.Width, notes.Picture.Height));
            Assert.Equal(2, rig.Files().Count);
            Assert.True(File.Exists(Assert.Single(rig.Pictures.Pictures).Picture.PngPath));
        }
        finally
        {
            source.Dispose();
            window.Close();
        }
    });
}
