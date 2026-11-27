namespace StillSaneExile.Domain.Guide;

/// <summary>The build guide link a player pastes. Mobalytics' terms restrict software access,
/// downloads, mirroring and framing, and it offers no authorized export, so SSE only saves the link and
/// asks Windows to open it in the browser when the player clicks. It never fetches, embeds or reads
/// the page. Any https link is accepted, since the same rule holds for every guide site.</summary>
public static class BuildLink
{
    public const int MaxLength = 2048;

    public static bool TryParse(string? text, out Uri? link, out string? error)
    {
        link = null;
        var s = (text ?? "").Trim();
        if (s.Length == 0)
        {
            error = "Paste the link to your build guide first.";
            return false;
        }

        if (s.Length > MaxLength || s.Any(char.IsWhiteSpace))
        {
            error = "That doesn't look like one link. Copy it from the browser's address bar.";
            return false;
        }

        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            error = "Paste the full link, starting with https://.";
            return false;
        }

        if (uri.UserInfo.Length > 0 || !uri.Host.Contains('.', StringComparison.Ordinal) || uri.HostNameType != UriHostNameType.Dns)
        {
            error = "That link has no proper web address in it.";
            return false;
        }

        link = uri;
        error = null;
        return true;
    }

    public static bool IsMobalytics(Uri link) =>
        link.Host.Equals("mobalytics.gg", StringComparison.OrdinalIgnoreCase) || link.Host.EndsWith(".mobalytics.gg", StringComparison.OrdinalIgnoreCase);
}
