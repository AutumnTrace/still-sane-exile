using System.Diagnostics;
using System.Runtime.InteropServices;

namespace StillSaneExile.Desktop.Infrastructure;

/// <summary>Observes the two side mouse buttons (Mouse 4 and 5) system-wide with a low-level mouse hook
/// and reports them as key events with Windows' own virtual-key codes for them, so a side button binds
/// like a key. Like the keyboard observer it never swallows, delays or sends input: every mouse
/// message is passed on unchanged with CallNextHookEx. Only the side buttons' own messages are read;
/// movement, the wheel and the other buttons are passed straight on. The hook runs in this process
/// (no DLL is loaded into the game).</summary>
public sealed class MouseButtonObserver : IDisposable
{
    private readonly NativeMethods.LowLevelHookProc _proc;
    private readonly Func<Modifiers> _modifiers;
    private nint _hook;

    /// <param name="modifiers">Ctrl, Shift and Alt as the keyboard observer tracks them.</param>
    public MouseButtonObserver(Func<Modifiers> modifiers)
    {
        _modifiers = modifiers;
        _proc = HookProc; // keep the delegate alive for the hook's lifetime
    }

    public event Action<KeyEvent>? Button;

    public bool IsRunning => _hook != 0;

    public void Start()
    {
        if (_hook != 0)
        {
            return;
        }

        using var module = Process.GetCurrentProcess().MainModule;
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _proc, NativeMethods.GetModuleHandle(module?.ModuleName), 0);
        if (_hook == 0)
        {
            throw new InvalidOperationException($"Could not observe the side mouse buttons (error {Marshal.GetLastWin32Error()}).");
        }
    }

    public void Stop()
    {
        if (_hook != 0)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
    }

    public void Dispose() => Stop();

    /// <summary>Called for every mouse message in the system, so anything but a side button returns at once.</summary>
    private nint HookProc(int nCode, nint wParam, nint lParam)
    {
        var msg = (int)wParam;
        if (nCode >= 0 && msg is NativeMethods.WM_XBUTTONDOWN or NativeMethods.WM_XBUTTONUP)
        {
            try
            {
                var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                var vk = (data.mouseData >> 16) switch
                {
                    NativeMethods.XBUTTON1 => Hotkey.VkXButton1,
                    NativeMethods.XBUTTON2 => Hotkey.VkXButton2,
                    _ => 0,
                };
                if (vk != 0)
                {
                    var injected = (data.flags & (NativeMethods.LLMHF_INJECTED | NativeMethods.LLMHF_LOWER_IL_INJECTED)) != 0;
                    Button?.Invoke(new KeyEvent(vk, msg == NativeMethods.WM_XBUTTONDOWN, injected, _modifiers(),
                        NativeMethods.GetClipboardSequenceNumber(), Stopwatch.GetTimestamp()));
                }
            }
            catch (Exception ex)
            {
                // Never let an exception cross into the OS callback; never block the click.
                Debug.WriteLine(ex);
            }
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}
