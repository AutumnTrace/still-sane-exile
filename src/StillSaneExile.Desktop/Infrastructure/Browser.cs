using System.Diagnostics;

namespace StillSaneExile.Desktop.Infrastructure;

/// <summary>Opens a web page in the player's default browser. Only ever on a click: apart from the
/// update check, the app itself makes no web requests.</summary>
public static class Browser
{
    public static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
