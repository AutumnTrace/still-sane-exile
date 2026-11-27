namespace StillSaneExile.Domain.Capture;

public enum FrameReadOutcome
{
    /// <summary>Analysed, and its generation is still the one showing: paint it.</summary>
    Accepted,
    /// <summary>Released, focus lost or a newer press while the frame was taken: dropped unpainted.</summary>
    Late,
    /// <summary>The source returned no frame (or threw).</summary>
    NoFrame,
    /// <summary>The analysis threw. The frame was discarded all the same.</summary>
    AnalysisFailed,
}

public sealed record FrameRead<T>(FrameReadOutcome Outcome, long Generation, T? Value, string? Error = null);

/// <summary>Counters for the acceptance check "one physical press → one frame". Safe to read from any
/// thread; the capture side updates them from a worker thread.</summary>
public sealed class FrameReaderCounters
{
    private int _captureCalls;
    private int _frames;
    private int _framesDisposed;
    private int _noFrame;
    private int _analysisFailures;
    private int _lateDropped;
    private int _accepted;

    /// <summary><see cref="IFrameSource.CaptureOnce"/> calls. Always equal to the gate's reads for this command.</summary>
    public int CaptureCalls => Volatile.Read(ref _captureCalls);

    /// <summary>Frames the source handed over.</summary>
    public int Frames => Volatile.Read(ref _frames);

    public int FramesDisposed => Volatile.Read(ref _framesDisposed);

    /// <summary>Frames handed over and not yet discarded. Zero whenever no read is in progress.</summary>
    public int FramesInMemory => Frames - FramesDisposed;

    public int NoFrame => Volatile.Read(ref _noFrame);

    public int AnalysisFailures => Volatile.Read(ref _analysisFailures);

    public int LateDropped => Volatile.Read(ref _lateDropped);

    public int Accepted => Volatile.Read(ref _accepted);

    internal void CaptureCalled() => Interlocked.Increment(ref _captureCalls);

    internal void FrameReceived() => Interlocked.Increment(ref _frames);

    internal void FrameDisposed() => Interlocked.Increment(ref _framesDisposed);

    internal void NoFrameReturned() => Interlocked.Increment(ref _noFrame);

    internal void AnalysisFailed() => Interlocked.Increment(ref _analysisFailures);

    internal void Late() => Interlocked.Increment(ref _lateDropped);

    internal void Accept() => Interlocked.Increment(ref _accepted);

    public override string ToString() =>
        $"captures {CaptureCalls} · frames {Frames} · discarded {FramesDisposed} · in memory {FramesInMemory} · no frame {NoFrame} · analysis failed {AnalysisFailures} · late dropped {LateDropped} · accepted {Accepted}";
}

/// <summary>One physical press, one frame. Puts a <see cref="CaptureGate"/> in front of an
/// <see cref="IFrameSource"/>: a <see cref="GateAction.Read"/> decision allows exactly one
/// <see cref="IFrameSource.CaptureOnce"/>, the frame lives only for the analysis and is discarded
/// right after, and the result is handed back only if its generation is still current.
/// Every gate call goes through this class under one lock, so the input hook's thread and the
/// capture worker can both use it.</summary>
public sealed class FrameReader
{
    private readonly object _sync = new();
    private readonly CaptureGate _gate;
    private readonly string _command;
    private readonly IFrameSource _source;
    private long _lastReadGeneration;

    public FrameReader(CaptureGate gate, string command, IFrameSource source)
    {
        _gate = gate;
        _command = command;
        _source = source;
    }

    public FrameReaderCounters Counters { get; } = new();

    public CaptureCounters GateCounters => _gate.Counters;

    public string SourceName => _source.Name;

    public GateDecision KeyDown(bool injected, bool allowedForeground)
    {
        lock (_sync)
        {
            return _gate.KeyDown(_command, injected, allowedForeground);
        }
    }

    public GateDecision KeyUp(bool injected)
    {
        lock (_sync)
        {
            return _gate.KeyUp(_command, injected);
        }
    }

    public GateDecision Invalidate(string reason)
    {
        lock (_sync)
        {
            return _gate.Invalidate(reason);
        }
    }

    /// <summary>Takes the one frame <paramref name="decision"/> allows, runs <paramref name="analyse"/>
    /// on it off the calling thread, discards it, then checks the generation. A decision can be read
    /// once; a second call throws.</summary>
    public async Task<FrameRead<T>> ReadAsync<T>(GateDecision decision, nint window, Func<Frame, T> analyse)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(analyse);
        if (decision.Action != GateAction.Read)
        {
            throw new ArgumentException("Only a Read decision allows a capture.", nameof(decision));
        }

        lock (_sync)
        {
            if (decision.Generation <= _lastReadGeneration)
            {
                throw new InvalidOperationException("A Read decision allows exactly one capture, and this one was already used.");
            }

            _lastReadGeneration = decision.Generation;
        }

        var (outcome, value, error) = await Task.Run(() => CaptureAndAnalyse(window, analyse)).ConfigureAwait(false);

        lock (_sync)
        {
            if (!_gate.TryAccept(_command, decision.Generation))
            {
                // Whatever the analysis made of the frame goes too, cleared if it can be.
                (value as IDisposable)?.Dispose();
                Counters.Late();
                return new FrameRead<T>(FrameReadOutcome.Late, decision.Generation, default, error);
            }
        }

        if (outcome == FrameReadOutcome.Accepted)
        {
            Counters.Accept();
        }

        return new FrameRead<T>(outcome, decision.Generation, value, error);
    }

    private (FrameReadOutcome, T?, string?) CaptureAndAnalyse<T>(nint window, Func<Frame, T> analyse)
    {
        Counters.CaptureCalled();
        Frame? frame;
        try
        {
            frame = _source.CaptureOnce(window);
        }
        catch (Exception ex)
        {
            Counters.NoFrameReturned();
            return (FrameReadOutcome.NoFrame, default, $"{_source.Name}: {ex.Message}");
        }

        if (frame is null)
        {
            Counters.NoFrameReturned();
            return (FrameReadOutcome.NoFrame, default, $"{_source.Name} returned no frame.");
        }

        Counters.FrameReceived();
        try
        {
            return (FrameReadOutcome.Accepted, analyse(frame), null);
        }
        catch (Exception ex)
        {
            Counters.AnalysisFailed();
            return (FrameReadOutcome.AnalysisFailed, default, ex.Message);
        }
        finally
        {
            frame.Dispose();
            Counters.FrameDisposed();
        }
    }
}
