namespace StillSaneExile.Domain.Capture;

public enum HoldMode
{
    /// <summary>Show while held, hide on release.</summary>
    Hold,
    /// <summary>Accessibility: first press shows a frozen snapshot, the next press dismisses it.</summary>
    Toggle,
}

public enum GateAction
{
    Ignore,
    /// <summary>Perform exactly one read (clipboard or frame) tagged with <see cref="GateDecision.Generation"/>.</summary>
    Read,
    /// <summary>Hide whatever this command is showing.</summary>
    Hide,
}

public sealed record GateDecision(GateAction Action, long Generation, string Reason)
{
    public static GateDecision Ignore(string reason) => new(GateAction.Ignore, 0, reason);
}

/// <summary>Counters that back the acceptance check "one input → at most one read, zero game actions,
/// zero market requests". Market requests live elsewhere; the HUD path has no network client.</summary>
public sealed class CaptureCounters
{
    public int Reads { get; internal set; }
    public int AutoRepeatIgnored { get; internal set; }
    public int InjectedIgnored { get; internal set; }
    public int NotForegroundIgnored { get; internal set; }
    public int StaleResultsDropped { get; internal set; }
    public int Hides { get; internal set; }

    /// <summary>The app never sends input to the game. Kept as a counter so the invariant is visible.</summary>
    public int GameActionsSent => 0;
}

/// <summary>Decides when a physical key event may trigger a read. Owns the generation id that late
/// results are checked against, so an OCR or clipboard result that finishes after release, focus
/// loss or a newer press can never paint.</summary>
public sealed class CaptureGate
{
    private readonly Dictionary<string, CommandState> _commands = new(StringComparer.Ordinal);
    private long _generation;

    public CaptureCounters Counters { get; } = new();

    public long CurrentGeneration => _generation;

    public void Configure(string command, HoldMode mode) => State(command).Mode = mode;

    /// <param name="injected">True when the OS flags the event as synthetic (another program sent it).</param>
    /// <param name="allowedForeground">True when the foreground window is the game (or the app's own practice view).</param>
    public GateDecision KeyDown(string command, bool injected, bool allowedForeground)
    {
        var s = State(command);
        if (injected)
        {
            Counters.InjectedIgnored++;
            return GateDecision.Ignore("synthetic input");
        }

        if (s.IsDown)
        {
            Counters.AutoRepeatIgnored++;
            return GateDecision.Ignore("auto-repeat");
        }

        s.IsDown = true;

        if (s.Mode == HoldMode.Toggle && s.ActiveGeneration is not null)
        {
            s.ActiveGeneration = null;
            _generation++;
            Counters.Hides++;
            return new GateDecision(GateAction.Hide, _generation, "toggle dismiss");
        }

        if (!allowedForeground)
        {
            Counters.NotForegroundIgnored++;
            return GateDecision.Ignore("game is not the foreground window");
        }

        _generation++;
        s.ActiveGeneration = _generation;
        Counters.Reads++;
        return new GateDecision(GateAction.Read, _generation, "fresh physical press");
    }

    public GateDecision KeyUp(string command, bool injected)
    {
        var s = State(command);
        if (injected)
        {
            return GateDecision.Ignore("synthetic input");
        }

        s.IsDown = false;
        if (s.Mode == HoldMode.Hold && s.ActiveGeneration is not null)
        {
            s.ActiveGeneration = null;
            _generation++;
            Counters.Hides++;
            return new GateDecision(GateAction.Hide, _generation, "released");
        }

        return GateDecision.Ignore("nothing showing");
    }

    /// <summary>Focus loss, minimize, lock, resolution change, scroll or panel change: clear everything.
    /// Held keys stay held, so auto-repeat cannot start a new read until a fresh press.</summary>
    public GateDecision Invalidate(string reason)
    {
        var any = false;
        foreach (var s in _commands.Values)
        {
            any |= s.ActiveGeneration is not null;
            s.ActiveGeneration = null;
        }

        _generation++;
        if (any)
        {
            Counters.Hides++;
        }

        return any ? new GateDecision(GateAction.Hide, _generation, reason) : GateDecision.Ignore(reason);
    }

    /// <summary>True only if <paramref name="generation"/> is still the one showing for <paramref name="command"/>.
    /// Call before painting any result; a false return is counted as a dropped stale result.</summary>
    public bool TryAccept(string command, long generation)
    {
        var ok = State(command).ActiveGeneration == generation && generation == _generation;
        if (!ok)
        {
            Counters.StaleResultsDropped++;
        }

        return ok;
    }

    public bool IsShowing(string command) => State(command).ActiveGeneration is not null;

    private CommandState State(string command)
    {
        if (!_commands.TryGetValue(command, out var s))
        {
            s = new CommandState();
            _commands[command] = s;
        }

        return s;
    }

    private sealed class CommandState
    {
        public HoldMode Mode = HoldMode.Hold;
        public bool IsDown;
        public long? ActiveGeneration;
    }
}
