using System.Windows.Input;
using StillSaneExile.Desktop.Infrastructure;

namespace StillSaneExile.Desktop.Services;

/// <summary>Path of Exile 2's own default keys that an SSE binding could clash with, for the Settings
/// warnings. Deliberately short: only defaults we are confident about. Left, right and middle click
/// are skill slots too; Settings refuses to bind them at all.
/// This list must be verified against the live game's keybinding screen (Options, Input) before it is
/// relied on; that real-game check is tracked separately.</summary>
public static class GameKeys
{
    private static readonly (Key Key, string Action)[] Defaults =
    [
        (Key.Q, "skill"),
        (Key.W, "skill"),
        (Key.E, "skill"),
        (Key.R, "skill"),
        (Key.T, "skill"),
        (Key.D1, "life flask"),
        (Key.D2, "mana flask"),
        (Key.Space, "dodge roll"),
        (Key.X, "weapon swap"),
        (Key.I, "inventory"),
    ];

    /// <summary>What the game does with this binding's key by default, or null. Modifiers are
    /// ignored: held with one of these keys, a modifier may not stop the game from using it.</summary>
    public static string? DefaultAction(Hotkey binding)
    {
        foreach (var (key, action) in Defaults)
        {
            if (KeyInterop.VirtualKeyFromKey(key) == binding.Vk)
            {
                return action;
            }
        }

        return null;
    }
}
