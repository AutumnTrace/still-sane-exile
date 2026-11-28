using System.Runtime.InteropServices;
using System.Windows;

namespace StillSaneExile.Desktop.Infrastructure;

/// <param name="NothingCopied">The game copied nothing after the press, so nothing was read: there was no
/// item under the cursor.</param>
public sealed record ClipboardRead(string? Text, string? Error, bool NothingCopied = false);

/// <summary>The two ways the app reads the clipboard, so the HUD and the paste button can be tested
/// with a fake. Each call is at most one read.</summary>
public interface IClipboardSource
{
    Task<ClipboardRead> ReadAfterChangeAsync(uint sequenceAtPress, TimeSpan timeout);

    ClipboardRead ReadNow();
}

/// <summary>Reads clipboard text exactly once per call. No retries: a busy clipboard produces one
/// message and waits for the player's next press. Callers never store the text unless it parses as
/// an item and the player then saves something.</summary>
public sealed class ClipboardReader : IClipboardSource
{
    public int ReadCount { get; private set; }

    /// <summary>Waits (without reading) until the clipboard change counter moves past
    /// <paramref name="sequenceAtPress"/>, then reads once, so the read sees the item the game just
    /// copied. If the game copied nothing within <paramref name="timeout"/>, nothing is read at all:
    /// older clipboard content is never mistaken for the hovered item.</summary>
    public async Task<ClipboardRead> ReadAfterChangeAsync(uint sequenceAtPress, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (NativeMethods.GetClipboardSequenceNumber() == sequenceAtPress)
        {
            if (DateTime.UtcNow >= until)
            {
                return new ClipboardRead(null, "The game did not copy anything. Hover an item, then press again.", NothingCopied: true);
            }

            await Task.Delay(10).ConfigureAwait(true);
        }

        return ReadNow();
    }

    public ClipboardRead ReadNow()
    {
        ReadCount++;
        try
        {
            var text = Clipboard.GetText();
            return text.Length > 0 ? new ClipboardRead(text, null) : new ClipboardRead(null, "The clipboard holds no text.");
        }
        catch (ExternalException) // includes COMException: another program holds the clipboard open
        {
            return new ClipboardRead(null, "The clipboard is busy. Press again.");
        }
    }
}
