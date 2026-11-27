using System.Diagnostics;

namespace StillSaneExile.Desktop.Infrastructure;

public enum ForegroundKind
{
    Other,
    Game,
    ThisApp,
}

/// <summary>What the HUD needs to know about the window in front, so its decisions can be tested with a fake.</summary>
public interface IForegroundSource : IDisposable
{
    ForegroundKind Current { get; }

    nint ForegroundWindow { get; }

    /// <summary>The new kind of window in front, and why it changed.</summary>
    event Action<ForegroundKind, string>? Changed;

    void Start();
}

/// <summary>Knows which window is in front, from OS window metadata only (title and owning process
/// name). Raises an event when that changes or a window starts minimizing.</summary>
public sealed class ForegroundWatcher : IForegroundSource
{
    private static readonly string[] GameProcessPrefixes = ["PathOfExile"];
    private const string GameWindowTitle = "Path of Exile 2";

    private readonly NativeMethods.WinEventDelegate _proc;
    private readonly int _ownPid = Environment.ProcessId;
    private readonly Dictionary<uint, string> _processNames = [];
    private nint _foregroundHook;
    private nint _minimizeHook;

    public ForegroundWatcher()
    {
        _proc = OnWinEvent;
    }

    public event Action<ForegroundKind, string>? Changed;

    public void Start()
    {
        _foregroundHook = NativeMethods.SetWinEventHook(NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND, 0, _proc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
        _minimizeHook = NativeMethods.SetWinEventHook(NativeMethods.EVENT_SYSTEM_MINIMIZESTART, NativeMethods.EVENT_SYSTEM_MINIMIZESTART, 0, _proc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
    }

    public nint ForegroundWindow => NativeMethods.GetForegroundWindow();

    public ForegroundKind Current => Classify(NativeMethods.GetForegroundWindow());

    public ForegroundKind Classify(nint hwnd)
    {
        if (hwnd == 0)
        {
            return ForegroundKind.Other;
        }

        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == _ownPid)
        {
            return ForegroundKind.ThisApp;
        }

        if (NativeMethods.WindowTitle(hwnd).Equals(GameWindowTitle, StringComparison.Ordinal))
        {
            return ForegroundKind.Game;
        }

        var name = ProcessName(pid);
        return GameProcessPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) && NativeMethods.WindowTitle(hwnd).StartsWith("Path of Exile", StringComparison.Ordinal)
            ? ForegroundKind.Game
            : ForegroundKind.Other;
    }

    public void Dispose()
    {
        if (_foregroundHook != 0)
        {
            NativeMethods.UnhookWinEvent(_foregroundHook);
            _foregroundHook = 0;
        }

        if (_minimizeHook != 0)
        {
            NativeMethods.UnhookWinEvent(_minimizeHook);
            _minimizeHook = 0;
        }
    }

    private void OnWinEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            var reason = eventType == NativeMethods.EVENT_SYSTEM_MINIMIZESTART ? "minimize" : "foreground changed";
            var kind = eventType == NativeMethods.EVENT_SYSTEM_MINIMIZESTART ? Classify(NativeMethods.GetForegroundWindow()) : Classify(hwnd);
            Changed?.Invoke(eventType == NativeMethods.EVENT_SYSTEM_MINIMIZESTART ? ForegroundKind.Other : kind, reason);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private string ProcessName(uint pid)
    {
        if (_processNames.TryGetValue(pid, out var name))
        {
            return name;
        }

        try
        {
            using var p = Process.GetProcessById((int)pid);
            name = p.ProcessName;
        }
        catch (ArgumentException)
        {
            name = "";
        }
        catch (InvalidOperationException)
        {
            name = "";
        }

        _processNames[pid] = name;
        return name;
    }
}
