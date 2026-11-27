using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using StillSaneExile.CaptureSpike.Capture;
using StillSaneExile.CaptureSpike.Interop;
using StillSaneExile.Domain.Capture;

namespace StillSaneExile.CaptureSpike;

/// <summary>For the owner, in the game: a low-level keyboard hook (observe only, like SSE's) feeds the
/// real gate; each fresh physical press of the key while Path of Exile 2 is in front takes one frame
/// of its window, prints what came back, and discards it. Auto-repeat, synthetic input, other windows
/// and the release take nothing. Focus changes invalidate. Ctrl+C quits.</summary>
internal static class Live
{
    private static FrameReader? _reader;
    private static ISpikeSource? _source;
    private static int _vk;
    private static bool _ownWindows;
    private static string? _dump;
    private static nint _hook;
    private static nint _winEvent;
    private static readonly Win32.LowLevelHookProc HookProc = OnHook;
    private static readonly Win32.WinEventDelegate WinEventProc = OnWinEvent;

    /// <summary>VK 0x97: unassigned, so sending it does nothing in any program.</summary>
    private const int UnassignedVk = 0x97;

    public static int Run(Args args)
    {
        var selfTest = args.Has("selftest-injected");
        if (selfTest && IsGame(Win32.GetForegroundWindow()))
        {
            Console.Error.WriteLine("The self-test sends synthetic key presses; it will not run with the game in front.");
            return 1;
        }

        _vk = selfTest ? UnassignedVk : ParseKey(args.Get("key") ?? "RightAlt");
        _ownWindows = args.Has("own-windows");
        _dump = args.DumpFolder;
        _source = Args.Source(args.Get("backend") ?? "wgc", args.Format, args.Has("no-border"));
        _reader = new FrameReader(new CaptureGate(), Hold.Command, _source);
        var thread = Win32.GetCurrentThreadId();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Win32.PostThreadMessage(thread, Win32.WM_QUIT, 0, 0);
        };

        _hook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, HookProc, Win32.GetModuleHandle(null), 0);
        if (_hook == 0)
        {
            Console.Error.WriteLine($"Could not observe the keyboard (error {Marshal.GetLastWin32Error()}).");
            return 1;
        }

        _winEvent = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_MINIMIZESTART, 0, WinEventProc, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);
        var keyName = KeyInterop.KeyFromVirtualKey(_vk) is var k && k != Key.None ? k.ToString() : $"VK 0x{_vk:X2}";
        Console.WriteLine($"Press and hold {keyName} with Path of Exile 2 in front{(_ownWindows ? " (or a window of this tool)" : "")}. One frame per press with {_source.Name}. {(_dump is null ? "Nothing is saved." : $"Frames are saved to {_dump}.")} Ctrl+C quits.");
        if (selfTest)
        {
            new Thread(() => SendSynthetic(thread)) { IsBackground = true }.Start();
        }

        while (Win32.GetMessage(out var msg, 0, 0, 0) > 0)
        {
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessage(ref msg);
        }

        Win32.UnhookWinEvent(_winEvent);
        Win32.UnhookWindowsHookEx(_hook);
        Console.WriteLine($"Gate: reads {_reader.GateCounters.Reads}, auto-repeat ignored {_reader.GateCounters.AutoRepeatIgnored}, synthetic ignored {_reader.GateCounters.InjectedIgnored}, not in front {_reader.GateCounters.NotForegroundIgnored}");
        Console.WriteLine($"Reader: {_reader.Counters}");
        Console.WriteLine($"Source: {_source.Counters}");
        _source.Dispose();
        if (selfTest)
        {
            var g = _reader.GateCounters;
            var pass = g.Reads == 0 && g.InjectedIgnored == 31 && _source.Counters.Captures == 0;
            Console.WriteLine(pass ? "PASS: 31 synthetic key-downs through the real keyboard hook, 0 reads, 0 captures." : "FAIL: synthetic input reached the capture.");
            return pass ? 0 : 1;
        }

        return 0;
    }

    /// <summary>Self-test: a held key made with SendInput (30 key-downs, one release), as a macro tool
    /// would. The hook sees each one flagged LLKHF_INJECTED; the gate must take none of them.</summary>
    private static void SendSynthetic(uint loopThread)
    {
        Thread.Sleep(300);
        var size = Marshal.SizeOf<Win32.KeyInput>();
        for (var i = 0; i < 31; i++)
        {
            Win32.SendInput(1, [new Win32.KeyInput { Type = 1, Vk = UnassignedVk }], size);
            Thread.Sleep(15);
        }

        Win32.SendInput(1, [new Win32.KeyInput { Type = 1, Vk = UnassignedVk, Flags = 0x2 }], size);
        Thread.Sleep(500);
        Win32.PostThreadMessage(loopThread, Win32.WM_QUIT, 0, 0);
    }

    public static bool IsGame(nint hwnd)
    {
        if (hwnd == 0)
        {
            return false;
        }

        var title = Win32.WindowTitle(hwnd);
        if (title.Equals("Path of Exile 2", StringComparison.Ordinal))
        {
            return true;
        }

        _ = Win32.GetWindowThreadProcessId(hwnd, out var pid);
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName.StartsWith("PathOfExile", StringComparison.OrdinalIgnoreCase) && title.StartsWith("Path of Exile", StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static int ParseKey(string key)
    {
        if (key.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && int.TryParse(key[2..], System.Globalization.NumberStyles.HexNumber, null, out var hex))
        {
            return hex;
        }

        return Enum.TryParse<Key>(key, ignoreCase: true, out var k) ? KeyInterop.VirtualKeyFromKey(k) : throw new ArgumentException($"Unknown key {key}.");
    }

    private static nint OnHook(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var data = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
                var msg = (uint)wParam;
                if ((int)data.vkCode == _vk && _reader is { } reader)
                {
                    var injected = (data.flags & (Win32.LLKHF_INJECTED | Win32.LLKHF_LOWER_IL_INJECTED)) != 0;
                    if (msg is Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN)
                    {
                        var fg = Win32.GetForegroundWindow();
                        var allowed = IsGame(fg) || (_ownWindows && Win32.OwnedByThisProcess(fg));
                        var d = reader.KeyDown(injected, allowed);
                        if (d.Action == GateAction.Read)
                        {
                            var pressed = Stopwatch.GetTimestamp();
                            _ = ReadAsync(reader, d, fg, pressed);
                        }
                        else if (d.Reason != "auto-repeat")
                        {
                            Console.WriteLine($"press ignored: {d.Reason}");
                        }
                    }
                    else if (msg is Win32.WM_KEYUP or Win32.WM_SYSKEYUP)
                    {
                        var d = reader.KeyUp(injected);
                        if (d.Action == GateAction.Hide)
                        {
                            Console.WriteLine($"released: generation {d.Generation}, auto-repeat ignored so far {reader.GateCounters.AutoRepeatIgnored}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static async Task ReadAsync(FrameReader reader, GateDecision d, nint window, long pressed)
    {
        var result = await reader.ReadAsync(d, window, frame =>
        {
            var stats = FrameStats.Of(frame);
            var saved = _dump is null ? null : Dump.Png(frame, _dump, "poe2");
            return (stats, saved);
        });
        var t = _source?.LastTiming;
        var line = result.Value is var (stats, saved) && result.Outcome == FrameReadOutcome.Accepted
            ? $"{stats.Width}×{stats.Height} {_source?.LastFormat}, average {stats.AverageHex}, {(stats.IsBlank ? "BLACK, " : "")}peak scRGB {stats.PeakScRgb:0.##}{(saved is null ? "" : ", saved " + saved)}"
            : result.Error ?? "";
        Console.WriteLine($"press {d.Generation}: {result.Outcome} after {Ms.Since(pressed):0.0} ms. {line}. create→frame {t?.AcquireMs:0.0} ms, capture live {t?.LiveMs:0.0} ms, frame age {t?.FrameAgeMs:0.0} ms. {reader.Counters}");
    }

    private static void OnWinEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (eventType is Win32.EVENT_SYSTEM_FOREGROUND or Win32.EVENT_SYSTEM_MINIMIZESTART && _reader?.Invalidate(eventType == Win32.EVENT_SYSTEM_FOREGROUND ? "foreground changed" : "minimize") is { Action: GateAction.Hide } d)
        {
            Console.WriteLine($"invalidated: {d.Reason}");
        }
    }
}
