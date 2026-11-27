namespace StillSaneExile.Domain.Releases;

/// <summary>Release versions as this app uses them: MAJOR.MINOR.PATCH, the only form a release tag may
/// take (scripts/verify-release.ps1). A leading "v" and build metadata ("+sha") are tolerated so tags
/// and informational versions parse too; anything else is not a release version.</summary>
public static class ReleaseVersion
{
    public static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var s = text.Trim();
        var plus = s.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            s = s[..plus];
        }

        if (s.StartsWith('v') || s.StartsWith('V'))
        {
            s = s[1..];
        }

        // Version also accepts "1.2" and "1.2.3.4"; a release always has exactly three parts.
        return Version.TryParse(s, out var v) && v.Build >= 0 && v.Revision < 0 ? v : null;
    }
}

/// <param name="Show">This start follows an update: show "What's new".</param>
/// <param name="Since">The version the player came from, so every section after it can be shown.
/// Null when it is unknown (an update from a build older than this feature): show this version's notes only.</param>
/// <param name="Remember">Store the running version as the last-run version: straight away when there is
/// nothing to show, otherwise once the notes have been on screen, so they appear once.</param>
public sealed record WhatsNewDecision(bool Show, Version? Since, bool Remember)
{
    public static readonly WhatsNewDecision Nothing = new(false, null, false);
}

/// <summary>Decides whether this start follows an update. Kept free of settings and UI so every case
/// (first install, update, update from an older build, downgrade, same version) is tested.</summary>
public static class WhatsNew
{
    /// <param name="current">The running version.</param>
    /// <param name="lastRun">The last-run version stored in settings, or null when none is stored.</param>
    /// <param name="onboarded">Whether onboarding was finished. Builds up to 0.2.0 stored no last-run version,
    /// so "onboarded but nothing stored" is an update from one of them, and "neither" is a first install.</param>
    /// <param name="installed">Development builds run from source never show it; pass true to force it.</param>
    public static WhatsNewDecision Decide(string current, string? lastRun, bool onboarded, bool installed)
    {
        var now = ReleaseVersion.Parse(current);
        if (!installed || now is null)
        {
            return WhatsNewDecision.Nothing;
        }

        // An unreadable stored value says no more than a missing one.
        var before = ReleaseVersion.Parse(lastRun);
        if (before is null)
        {
            return onboarded ? new(true, null, true) : new(false, null, true);
        }

        // Same version, or a downgrade: nothing new. A downgrade keeps the newer record, so going back up
        // to it does not repeat notes the player has already seen.
        return before < now ? new(true, before, true) : WhatsNewDecision.Nothing;
    }
}
