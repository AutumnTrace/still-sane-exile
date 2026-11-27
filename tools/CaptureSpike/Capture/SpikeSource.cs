using System.Diagnostics;
using StillSaneExile.Domain.Capture;

namespace StillSaneExile.CaptureSpike.Capture;

public enum CaptureFormat
{
    /// <summary>16-bit float when the window's monitor has HDR on, 8-bit otherwise.</summary>
    Auto,
    Bgra8,
    RgbaHalf,
}

/// <summary>Where the time of one capture went, in milliseconds.</summary>
/// <param name="SetupMs">Creating the D3D device. Only the first capture of a source pays it.</param>
/// <param name="AcquireMs">Create → frame: from asking the OS for a capture to holding the frame.</param>
/// <param name="LiveMs">How long a capture session or duplication existed at all.</param>
/// <param name="ReadbackMs">Copying the frame from the GPU into memory.</param>
/// <param name="TotalMs">The whole <see cref="IFrameSource.CaptureOnce"/> call.</param>
/// <param name="FrameAgeMs">How long before the capture started the frame was composed (WGC only;
/// negative means composed after the start).</param>
public sealed record CaptureTiming(double SetupMs, double AcquireMs, double LiveMs, double ReadbackMs, double TotalMs, double? FrameAgeMs = null, string? Note = null);

/// <summary>What a backend did at the OS level. "Acquisitions" counts frames the OS handed over
/// (TryGetNextFrame, AcquireNextFrame or PrintWindow), the number that must equal one per press.</summary>
public sealed class SourceCounters
{
    private int _captures, _frames, _acquisitions, _opened, _closed, _arrived, _extra, _afterClose, _timeouts;

    public int Captures => Volatile.Read(ref _captures);

    public int Frames => Volatile.Read(ref _frames);

    public int Acquisitions => Volatile.Read(ref _acquisitions);

    public int SessionsOpened => Volatile.Read(ref _opened);

    public int SessionsClosed => Volatile.Read(ref _closed);

    public int FrameArrivedEvents => Volatile.Read(ref _arrived);

    /// <summary>FrameArrived raised again while the session was still open. The handler ignores it.</summary>
    public int ExtraEvents => Volatile.Read(ref _extra);

    /// <summary>FrameArrived raised after the session was closed. Must stay zero.</summary>
    public int EventsAfterClose => Volatile.Read(ref _afterClose);

    public int Timeouts => Volatile.Read(ref _timeouts);

    internal void Capture() => Interlocked.Increment(ref _captures);

    internal void Frame() => Interlocked.Increment(ref _frames);

    internal void Acquired() => Interlocked.Increment(ref _acquisitions);

    internal void Opened() => Interlocked.Increment(ref _opened);

    internal void Closed() => Interlocked.Increment(ref _closed);

    internal void Arrived() => Interlocked.Increment(ref _arrived);

    internal void Extra() => Interlocked.Increment(ref _extra);

    internal void AfterClose() => Interlocked.Increment(ref _afterClose);

    internal void Timeout() => Interlocked.Increment(ref _timeouts);

    public override string ToString() =>
        $"captures {Captures} · frames {Frames} · OS acquisitions {Acquisitions} · sessions opened {SessionsOpened} / closed {SessionsClosed} · FrameArrived {FrameArrivedEvents} (extra {ExtraEvents}, after close {EventsAfterClose}) · timeouts {Timeouts}";
}

/// <summary>A spike backend: an <see cref="IFrameSource"/> that also reports timings and OS-level counters.</summary>
public interface ISpikeSource : IFrameSource, IDisposable
{
    SourceCounters Counters { get; }

    CaptureTiming? LastTiming { get; }

    /// <summary>The format the last frame was asked for, as a short label.</summary>
    string LastFormat { get; }
}

internal static class Ms
{
    public static double Since(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;

    public static double Between(long start, long end) => Stopwatch.GetElapsedTime(start, end).TotalMilliseconds;
}
