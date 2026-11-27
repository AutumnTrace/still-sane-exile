namespace StillSaneExile.CaptureSpike.TestWindows;

/// <summary>A window this process owns and draws a numbered <see cref="Marker"/> into. The automated
/// spike modes capture only these.</summary>
public interface ITestWindow : IDisposable
{
    nint Handle { get; }

    /// <summary>The number of the newest frame drawn.</summary>
    long Presented { get; }

    /// <summary>When the frame whose marker reads <paramref name="marker"/> was drawn (Stopwatch ticks).
    /// Markers wrap every 4096 frames, about 17 s at 240 Hz.</summary>
    long RenderedAt(int marker);

    /// <summary>WM_PRINT and WM_PRINTCLIENT messages the window's thread received.</summary>
    int PrintMessages { get; }

    /// <summary>How many of each window message the window's thread has handled so far.</summary>
    IReadOnlyDictionary<uint, int> MessageCounts();

    int ClientWidth { get; }

    int ClientHeight { get; }

    string Description { get; }

    /// <summary>Block the window's thread for a while, like a busy game.</summary>
    void Stall(TimeSpan duration);
}
