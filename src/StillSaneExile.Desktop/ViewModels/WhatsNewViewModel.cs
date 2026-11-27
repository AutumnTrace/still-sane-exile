using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Domain.Releases;

namespace StillSaneExile.Desktop.ViewModels;

/// <summary>One version's notes as the card shows them.</summary>
public sealed record WhatsNewSection(string Heading, IReadOnlyList<NoteBlock> Blocks)
{
    /// <summary>What a screen reader says for the section (WPF reads a list row through ToString).</summary>
    public override string ToString() => Heading;
}

/// <summary>"What's new" after an update: the release notes since the version that last ran here, shown
/// once as a card at the top of the main window. Never on the HUD and never a dialog, so a running game
/// is not interrupted: if the game is in front, the card simply waits in the main window.
/// The notes are the CHANGELOG.md sections embedded in this build, rather than Velopack's update notes:
/// they are the same text as the GitHub release, need no network, and cover every skipped version,
/// while Velopack's are only for the newest release and gone once it has restarted.</summary>
public sealed partial class WhatsNewViewModel(Settings settings, UpdateService updates) : ObservableObject
{
    private const string ChangelogResource = "CHANGELOG.md";

    private bool _rememberWhenShown;

    public string CurrentVersion => updates.CurrentVersion;

    /// <summary>For Settings → Version and updates, where the notes stay reachable after "Got it".</summary>
    public string LinkLabel => $"What’s new in {CurrentVersion}";

    public string ReleaseUrl => $"{UpdateService.RepoUrl}/releases/tag/v{CurrentVersion}";

    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    public partial string Title { get; private set; } = "";

    [ObservableProperty]
    public partial string Lead { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSectionHeadings))]
    public partial IReadOnlyList<WhatsNewSection> Sections { get; private set; } = [];

    /// <summary>Version headings matter only when several updates were skipped; with one, the title says it.</summary>
    public bool ShowSectionHeadings => Sections.Count > 1;

    /// <summary>At start: opens the card if this run follows an update. The running version is stored
    /// straight away when there is nothing to show, otherwise by <see cref="MarkShown"/>.</summary>
    public void CheckOnStart()
    {
        var decision = WhatsNew.Decide(CurrentVersion, settings.LastRunVersion, settings.Onboarded, updates.IsInstalled);
        if (decision.Show)
        {
            _rememberWhenShown = decision.Remember;
            Show(decision.Since, afterUpdate: true);
        }
        else if (decision.Remember)
        {
            settings.LastRunVersion = CurrentVersion;
        }
    }

    /// <summary>Called once the card is actually in the window (onboarding can hold it back). Only then is
    /// the version stored, so the notes are seen once rather than lost to a start that never showed them.</summary>
    public void MarkShown()
    {
        if (_rememberWhenShown)
        {
            settings.LastRunVersion = CurrentVersion;
            _rememberWhenShown = false;
        }
    }

    /// <summary>Opens the card with every version's notes after <paramref name="since"/>, or this
    /// version's only when it is null. Also how the snapshot renderer shows it in a development build.</summary>
    public void Show(Version? since, bool afterUpdate)
    {
        var current = ReleaseVersion.Parse(CurrentVersion);
        var sections = current is null ? [] : Changelog.Since(ReadChangelog(), since, current);
        Sections = sections.Select(s => new WhatsNewSection(Heading(s), ReleaseNotes.Parse(s.Body))).ToList();

        // With no section for this version, still say what happened and point at the release.
        Title = afterUpdate && Sections.Count == 0 ? $"Updated to {CurrentVersion}" : $"What’s new in {CurrentVersion}";
        var opening = !afterUpdate ? "" : since is null ? "SSE updated itself. " : $"Updated from {since}. ";
        Lead = opening + Sections.Count switch
        {
            0 => "The notes for this version are on GitHub.",
            1 => afterUpdate ? "Here’s what changed." : "Here’s what changed in this version.",
            _ => "Here’s everything that changed since then.",
        };
        IsOpen = true;
    }

    [RelayCommand]
    private void ShowCurrent() => Show(since: null, afterUpdate: false);

    /// <summary>"Got it": hides the card for good. Settings still has the link.</summary>
    [RelayCommand]
    private void Dismiss()
    {
        MarkShown();
        IsOpen = false;
    }

    [RelayCommand]
    private void OpenRelease() => Browser.Open(ReleaseUrl);

    private static string Heading(ChangelogSection s)
    {
        var date = DateOnly.TryParseExact(s.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)
            : s.Date;
        return date.Length > 0 ? $"{s.Version} · {date}" : s.Version.ToString();
    }

    /// <summary>CHANGELOG.md as it was when this build was made (embedded by the project file).</summary>
    private static string ReadChangelog()
    {
        using var stream = typeof(WhatsNewViewModel).Assembly.GetManifestResourceStream(ChangelogResource);
        if (stream is null)
        {
            return "";
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
