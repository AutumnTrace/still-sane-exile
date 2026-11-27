using System.Diagnostics;
using StillSaneExile.CaptureSpike.Capture;
using StillSaneExile.CaptureSpike.TestWindows;
using StillSaneExile.Domain.Capture;

namespace StillSaneExile.CaptureSpike.Tests;

/// <summary>A test that needs a real capture: skipped, not failed, where there is no GPU, no desktop or
/// no Windows.Graphics.Capture (GitHub's windows-latest runners among them).</summary>
public sealed class CaptureFactAttribute : FactAttribute
{
    public CaptureFactAttribute()
    {
        if (CaptureSupport.WhyNot is { } reason)
        {
            Skip = "Needs a real capture: " + reason;
        }
    }
}

/// <summary>Real captures of windows this test process owns. One class, so they never run in parallel.</summary>
public class RealCaptureTests
{
    private const uint WM_PAINT = 0x000F;

    [CaptureFact]
    public void Wgc_takes_one_current_frame_of_the_client_area_and_closes_the_session()
    {
        using var window = D3DTestWindow.Open(D3DWindowMode.Windowed, 640, 360);
        using var source = new WgcFrameSource(new WgcOptions(CaptureFormat.Bgra8));

        var press = Stopwatch.GetTimestamp();
        using (var frame = source.CaptureOnce(window.Handle))
        {
            Assert.NotNull(frame);
            Assert.Equal(FramePixelFormat.Bgra8, frame.Format);
            Assert.Equal((window.ClientWidth, window.ClientHeight), (frame.Width, frame.Height));
            var seen = Marker.Read(frame);
            Assert.NotNull(seen);
            Assert.InRange(Stopwatch.GetElapsedTime(window.RenderedAt(seen.Value), press).TotalMilliseconds, -1000, 250);
        }

        Thread.Sleep(300); // a FrameArrived after close would be counted by now
        var c = source.Counters;
        Assert.Equal(1, c.Captures);
        Assert.Equal(1, c.Acquisitions);
        Assert.Equal(1, c.SessionsOpened);
        Assert.Equal(1, c.SessionsClosed);
        Assert.Equal(1, c.FrameArrivedEvents);
        Assert.Equal(0, c.ExtraEvents);
        Assert.Equal(0, c.EventsAfterClose);
    }

    [CaptureFact]
    public void Wgc_16_bit_frame_reads_the_same_as_the_8_bit_one()
    {
        using var window = D3DTestWindow.Open(D3DWindowMode.Windowed, 640, 360);
        using var source = new WgcFrameSource(new WgcOptions(CaptureFormat.RgbaHalf));

        using var frame = source.CaptureOnce(window.Handle);

        Assert.NotNull(frame);
        Assert.Equal(FramePixelFormat.RgbaHalf, frame.Format);
        Assert.NotNull(Marker.Read(frame));
        Assert.False(FrameStats.Of(frame).IsBlank);
    }

    [CaptureFact]
    public async Task Held_key_with_autorepeat_takes_one_wgc_frame_through_the_real_gate()
    {
        using var window = D3DTestWindow.Open(D3DWindowMode.Windowed, 640, 360);
        using var source = new WgcFrameSource(new WgcOptions(CaptureFormat.Bgra8));
        var reader = new FrameReader(new CaptureGate(), "stash-labels", source);
        var reads = new List<Task<FrameRead<FrameStats>>>();

        for (var i = 0; i <= 300; i++)
        {
            var d = reader.KeyDown(injected: false, allowedForeground: true);
            if (d.Action == GateAction.Read)
            {
                reads.Add(reader.ReadAsync(d, window.Handle, FrameStats.Of));
            }
        }

        var result = await Assert.Single(reads);
        reader.KeyUp(injected: false);
        Thread.Sleep(300);

        Assert.Equal(FrameReadOutcome.Accepted, result.Outcome);
        Assert.Equal(1, reader.GateCounters.Reads);
        Assert.Equal(300, reader.GateCounters.AutoRepeatIgnored);
        Assert.Equal(0, reader.Counters.FramesInMemory);
        Assert.Equal(1, source.Counters.Acquisitions);
        Assert.Equal(1, source.Counters.SessionsOpened);
        Assert.Equal(1, source.Counters.SessionsClosed);
        Assert.Equal(0, source.Counters.EventsAfterClose);
    }

    [CaptureFact]
    public void A_minimized_or_closed_window_gives_no_frame_and_starts_no_session()
    {
        using var source = new WgcFrameSource();
        nint closed;
        using (var window = D3DTestWindow.Open(D3DWindowMode.Windowed, 320, 180))
        {
            window.Minimize();
            Thread.Sleep(200);
            Assert.Null(source.CaptureOnce(window.Handle));
            closed = window.Handle;
        }

        Assert.Null(source.CaptureOnce(closed));
        Assert.Equal(0, source.Counters.SessionsOpened);
    }

    [CaptureFact]
    public void PrintWindow_makes_the_window_paint_itself()
    {
        // Why PrintWindow is not the fallback: the captured window's own thread handles the request
        // (WM_PAINT and friends), so it touches the game's message loop and waits while the game is busy.
        using var window = D3DTestWindow.Open(D3DWindowMode.Windowed, 320, 180);
        using var source = new PrintWindowFrameSource();
        var before = window.MessageCounts().GetValueOrDefault(WM_PAINT);

        using var frame = source.CaptureOnce(window.Handle);

        Assert.NotNull(frame);
        Assert.True(window.MessageCounts().GetValueOrDefault(WM_PAINT) > before, "PrintWindow made no WM_PAINT reach the window.");
    }
}
