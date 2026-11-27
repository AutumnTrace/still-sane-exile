using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace StillSaneExile.Desktop.Infrastructure;

public readonly record struct Modifiers(bool Ctrl, bool Shift, bool Alt);

/// <param name="Injected">Windows flagged the event as synthetic (sent by software, not a keyboard).</param>
/// <param name="ClipboardSequence">Clipboard change counter at the moment of the event (not clipboard content).</param>
public readonly record struct KeyEvent(int Vk, bool Down, bool Injected, Modifiers Mods, uint ClipboardSequence, long Timestamp);

/// <summary>A key plus required modifiers. Extra modifiers are allowed, so a Ctrl+C binding also fires
/// on the game's advanced copy, Ctrl+Alt+C.</summary>
public sealed record Hotkey(int Vk, bool Ctrl, bool Shift, bool Alt)
{
    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        bool ctrl = false, shift = false, alt = false;
        Key? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                    ctrl = true;
                    break;
                case "shift":
                    shift = true;
                    break;
                case "alt":
                    alt = true;
                    break;
                default:
                    if (Enum.TryParse<Key>(part, ignoreCase: true, out var k))
                    {
                        key = k;
                    }

                    break;
            }
        }

        return key is { } kk ? new Hotkey(KeyInterop.VirtualKeyFromKey(kk), ctrl, shift, alt) : null;
    }

    public bool Matches(int vk, Modifiers mods) =>
        vk == Vk && (!Ctrl || mods.Ctrl) && (!Shift || mods.Shift) && (!Alt || mods.Alt);

    public bool IsNativeCopy => Ctrl && Vk == KeyInterop.VirtualKeyFromKey(Key.C);

    public override string ToString()
    {
        var parts = new List<string>();
        if (Ctrl)
        {
            parts.Add("Ctrl");
        }

        if (Shift)
        {
            parts.Add("Shift");
        }

        if (Alt)
        {
            parts.Add("Alt");
        }

        parts.Add(KeyInterop.KeyFromVirtualKey(Vk).ToString());
        return string.Join("+", parts);
    }
}

/// <summary>What the HUD needs from the keyboard observer, so its decisions can be tested with a fake.</summary>
public interface IKeySource : IDisposable
{
    /// <summary>Raised on the thread that started the source, inside the hook callback.</summary>
    event Action<KeyEvent>? Key;

    bool AltGrDetected { get; }

    void Start();
}

/// <summary>Observes physical key presses system-wide with a low-level keyboard hook. It never
/// swallows, delays or sends a key: every event is passed on unchanged with CallNextHookEx. The hook
/// runs in this process (no DLL is loaded into the game).</summary>
public sealed class KeyboardObserver : IKeySource
{
    private const int VkLShift = 0xA0, VkRShift = 0xA1, VkLControl = 0xA2, VkRControl = 0xA3, VkLMenu = 0xA4, VkRMenu = 0xA5;
    private const uint AltGrFakeCtrlScan = 0x21D;

    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private nint _hook;
    private bool _lctrl, _rctrl, _lshift, _rshift, _lalt, _ralt;

    public KeyboardObserver()
    {
        _proc = HookProc; // keep the delegate alive for the hook's lifetime
    }

    public event Action<KeyEvent>? Key;

    /// <summary>True once Right Alt arrived together with the synthetic Left Ctrl that AltGr layouts send.</summary>
    public bool AltGrDetected { get; private set; }

    public bool IsRunning => _hook != 0;

    public void Start()
    {
        if (_hook != 0)
        {
            return;
        }

        using var module = Process.GetCurrentProcess().MainModule;
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, NativeMethods.GetModuleHandle(module?.ModuleName), 0);
        if (_hook == 0)
        {
            throw new InvalidOperationException($"Could not observe the keyboard (error {Marshal.GetLastWin32Error()}).");
        }
    }

    public void Dispose()
    {
        if (_hook != 0)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
    }

    private nint HookProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                var msg = (int)wParam;
                var down = msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
                var up = msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;
                var injected = (data.flags & (NativeMethods.LLKHF_INJECTED | NativeMethods.LLKHF_LOWER_IL_INJECTED)) != 0;
                if (down || up)
                {
                    var vk = (int)data.vkCode;
                    var isAltGrCtrl = vk == VkLControl && data.scanCode == AltGrFakeCtrlScan;
                    if (isAltGrCtrl)
                    {
                        AltGrDetected = true;
                    }
                    else if (!injected)
                    {
                        Track(vk, down);
                    }

                    if (!isAltGrCtrl)
                    {
                        Key?.Invoke(new KeyEvent(vk, down, injected, new Modifiers(_lctrl || _rctrl, _lshift || _rshift, _lalt || _ralt),
                            NativeMethods.GetClipboardSequenceNumber(), Stopwatch.GetTimestamp()));
                    }
                }
            }
            catch (Exception ex)
            {
                // Never let an exception cross into the OS callback; never block the key.
                Debug.WriteLine(ex);
            }
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void Track(int vk, bool down)
    {
        switch (vk)
        {
            case VkLControl: _lctrl = down; break;
            case VkRControl: _rctrl = down; break;
            case VkLShift: _lshift = down; break;
            case VkRShift: _rshift = down; break;
            case VkLMenu: _lalt = down; break;
            case VkRMenu: _ralt = down; break;
        }
    }
}
