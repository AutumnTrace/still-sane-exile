using StillSaneExile.Domain.Capture;

namespace StillSaneExile.Domain.Tests;

public class FrameReaderTests
{
    private const string Stash = "stash-labels";
    private const nint Game = 0x1234;

    [Fact]
    public async Task Ten_second_hold_with_autorepeat_takes_exactly_one_frame()
    {
        var source = new FakeFrameSource { HoldUntilReleased = true };
        var reader = new FrameReader(new CaptureGate(), Stash, source);
        var reads = new List<Task<FrameRead<FrameStats>>>();

        // Windows' fastest auto-repeat is about 30 Hz: 300 repeated key-downs over 10 seconds. The
        // frame is still being taken while the first repeats arrive, as it would be in the game.
        for (var i = 0; i <= 300; i++)
        {
            var d = reader.KeyDown(injected: false, allowedForeground: true);
            if (d.Action == GateAction.Read)
            {
                reads.Add(reader.ReadAsync(d, Game, FrameStats.Of));
            }

            if (i == 5)
            {
                source.Release();
            }
        }

        var read = Assert.Single(reads);
        var result = await read;
        Assert.Equal(GateAction.Hide, reader.KeyUp(injected: false).Action);

        Assert.Equal(FrameReadOutcome.Accepted, result.Outcome);
        Assert.Equal(1, source.Calls);
        Assert.Equal(Game, Assert.Single(source.Windows));
        Assert.Equal(1, reader.Counters.CaptureCalls);
        Assert.Equal(1, reader.Counters.Frames);
        Assert.Equal(1, reader.Counters.FramesDisposed);
        Assert.Equal(0, reader.Counters.FramesInMemory);
        Assert.True(Assert.Single(source.Handed).IsDisposed);
        Assert.Equal(1, reader.GateCounters.Reads);
        Assert.Equal(300, reader.GateCounters.AutoRepeatIgnored);
        Assert.Equal(0, reader.GateCounters.GameActionsSent);
    }

    [Fact]
    public async Task Release_before_the_frame_arrives_drops_the_result_and_still_discards_the_frame()
    {
        var source = new FakeFrameSource { HoldUntilReleased = true };
        var reader = new FrameReader(new CaptureGate(), Stash, source);

        var read = reader.ReadAsync(reader.KeyDown(false, true), Game, FrameStats.Of);
        await source.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        reader.KeyUp(false);
        source.Release();

        var result = await read;
        Assert.Equal(FrameReadOutcome.Late, result.Outcome);
        Assert.Null(result.Value);
        Assert.Equal(1, reader.Counters.LateDropped);
        Assert.Equal(1, reader.GateCounters.StaleResultsDropped);
        Assert.True(Assert.Single(source.Handed).IsDisposed);
        Assert.Equal(0, reader.Counters.FramesInMemory);
    }

    [Fact]
    public async Task A_newer_press_supersedes_a_frame_still_being_taken()
    {
        var source = new FakeFrameSource { HoldUntilReleased = true };
        var reader = new FrameReader(new CaptureGate(), Stash, source);

        var first = reader.ReadAsync(reader.KeyDown(false, true), Game, FrameStats.Of);
        await source.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        reader.KeyUp(false);
        var second = reader.ReadAsync(reader.KeyDown(false, true), Game, FrameStats.Of);
        source.Release();

        Assert.Equal(FrameReadOutcome.Late, (await first).Outcome);
        Assert.Equal(FrameReadOutcome.Accepted, (await second).Outcome);
        Assert.Equal(2, source.Calls);
        Assert.All(source.Handed, f => Assert.True(f.IsDisposed));
    }

    [Fact]
    public async Task Focus_loss_while_held_drops_the_frame_and_autorepeat_does_not_take_another()
    {
        var source = new FakeFrameSource { HoldUntilReleased = true };
        var reader = new FrameReader(new CaptureGate(), Stash, source);

        var read = reader.ReadAsync(reader.KeyDown(false, true), Game, FrameStats.Of);
        await source.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        reader.Invalidate("focus lost");
        source.Release();
        Assert.Equal(FrameReadOutcome.Late, (await read).Outcome);

        for (var i = 0; i < 30; i++)
        {
            Assert.Equal(GateAction.Ignore, reader.KeyDown(false, true).Action);
        }

        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public void Synthetic_input_or_another_window_in_front_never_captures()
    {
        var source = new FakeFrameSource();
        var reader = new FrameReader(new CaptureGate(), Stash, source);

        Assert.Equal(GateAction.Ignore, reader.KeyDown(injected: true, allowedForeground: true).Action);
        Assert.Equal(GateAction.Ignore, reader.KeyDown(injected: false, allowedForeground: false).Action);

        Assert.Equal(0, source.Calls);
        Assert.Equal(0, reader.Counters.CaptureCalls);
    }

    [Fact]
    public async Task A_decision_allows_one_capture_only()
    {
        var source = new FakeFrameSource();
        var reader = new FrameReader(new CaptureGate(), Stash, source);
        var d = reader.KeyDown(false, true);

        await reader.ReadAsync(d, Game, FrameStats.Of);
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(d, Game, FrameStats.Of));
        await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadAsync(GateDecision.Ignore("auto-repeat"), Game, FrameStats.Of));
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task No_frame_is_reported_and_counted()
    {
        var source = new FakeFrameSource { ReturnNull = true };
        var reader = new FrameReader(new CaptureGate(), Stash, source);

        var result = await reader.ReadAsync(reader.KeyDown(false, true), Game, FrameStats.Of);

        Assert.Equal(FrameReadOutcome.NoFrame, result.Outcome);
        Assert.Equal(1, reader.Counters.CaptureCalls);
        Assert.Equal(1, reader.Counters.NoFrame);
        Assert.Equal(0, reader.Counters.Frames);
    }

    [Fact]
    public async Task A_failing_analysis_still_discards_the_frame()
    {
        var source = new FakeFrameSource();
        var reader = new FrameReader(new CaptureGate(), Stash, source);

        var result = await reader.ReadAsync<int>(reader.KeyDown(false, true), Game, _ => throw new InvalidDataException("unreadable"));

        Assert.Equal(FrameReadOutcome.AnalysisFailed, result.Outcome);
        Assert.Equal("unreadable", result.Error);
        Assert.True(Assert.Single(source.Handed).IsDisposed);
        Assert.Equal(0, reader.Counters.FramesInMemory);
    }

    [Fact]
    public void A_disposed_frame_has_no_pixels_left()
    {
        var pixels = new byte[4 * 4 * 4];
        Array.Fill(pixels, (byte)0x7F);
        var frame = new Frame(4, 4, 16, FramePixelFormat.Bgra8, pixels, 0);

        frame.Dispose();

        Assert.True(frame.IsDisposed);
        Assert.All(pixels, b => Assert.Equal(0, b));
        Assert.Throws<ObjectDisposedException>(() => frame.Pixels.Length);
    }

    [Fact]
    public void Stats_give_the_same_colour_for_8_bit_and_hdr_frames()
    {
        using var sdr = FakeFrameSource.Solid(8, 4, 200, 100, 30, FramePixelFormat.Bgra8);
        using var hdr = FakeFrameSource.Solid(8, 4, 200, 100, 30, FramePixelFormat.RgbaHalf, sdrWhite: 2.5);

        var a = FrameStats.Of(sdr);
        var b = FrameStats.Of(hdr);

        Assert.Equal("#C8641E", a.AverageHex);
        Assert.Equal(a.AverageHex, b.AverageHex);
        Assert.InRange(b.PeakScRgb, 2.5 * FrameStats.DecodeSrgb(200) - 0.01, 2.5 * FrameStats.DecodeSrgb(200) + 0.01);
        Assert.False(a.IsBlank);
        Assert.NotEqual(a.Checksum, b.Checksum);
    }

    [Fact]
    public void Srgb_round_trips_every_8_bit_value()
    {
        for (var v = 0; v < 256; v++)
        {
            Assert.Equal(v, FrameStats.EncodeSrgb(FrameStats.DecodeSrgb((byte)v)));
        }
    }

    [Fact]
    public void A_black_frame_is_blank()
    {
        using var black = FakeFrameSource.Solid(16, 16, 0, 0, 0, FramePixelFormat.Bgra8);
        var stats = FrameStats.Of(black);
        Assert.True(stats.IsBlank);
        Assert.Equal((byte)0, FrameStats.PixelAt(black, 15, 15).R);
    }

    private sealed class FakeFrameSource : IFrameSource
    {
        private readonly SemaphoreSlim _go = new(0);
        private readonly SemaphoreSlim _entered = new(0);
        private int _calls;

        public bool HoldUntilReleased { get; init; }

        public bool ReturnNull { get; init; }

        public int Calls => Volatile.Read(ref _calls);

        public List<nint> Windows { get; } = [];

        public List<Frame> Handed { get; } = [];

        public SemaphoreSlim Entered => _entered;

        public string Name => "fake";

        public void Release() => _go.Release(100);

        public Frame? CaptureOnce(nint window)
        {
            Interlocked.Increment(ref _calls);
            lock (Windows)
            {
                Windows.Add(window);
            }

            _entered.Release();
            if (HoldUntilReleased && !_go.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("The test never released the fake capture.");
            }

            if (ReturnNull)
            {
                return null;
            }

            var frame = Solid(32, 18, 40, 80, 120, FramePixelFormat.Bgra8);
            lock (Handed)
            {
                Handed.Add(frame);
            }

            return frame;
        }

        public static Frame Solid(int w, int h, byte r, byte g, byte b, FramePixelFormat format, double sdrWhite = 1.0)
        {
            var bpp = Frame.BytesPer(format);
            var pixels = new byte[w * h * bpp];
            for (var i = 0; i < w * h; i++)
            {
                if (format == FramePixelFormat.Bgra8)
                {
                    pixels[i * 4] = b;
                    pixels[i * 4 + 1] = g;
                    pixels[i * 4 + 2] = r;
                    pixels[i * 4 + 3] = 255;
                }
                else
                {
                    var span = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Half>(pixels.AsSpan(i * 8, 8));
                    span[0] = (Half)(FrameStats.DecodeSrgb(r) * sdrWhite);
                    span[1] = (Half)(FrameStats.DecodeSrgb(g) * sdrWhite);
                    span[2] = (Half)(FrameStats.DecodeSrgb(b) * sdrWhite);
                    span[3] = (Half)1.0;
                }
            }

            return new Frame(w, h, w * bpp, format, pixels, 0, sdrWhite);
        }
    }
}
