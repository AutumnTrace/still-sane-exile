using System.Text.RegularExpressions;

namespace StillSaneExile.Domain.Releases;

/// <summary>One version's section of CHANGELOG.md.</summary>
/// <param name="Date">Whatever follows the version on the heading line ("2026-11-27"), or empty.</param>
/// <param name="Body">The section's markdown without its heading, trimmed.</param>
public sealed record ChangelogSection(Version Version, string Date, string Body);

/// <summary>Reads CHANGELOG.md the way scripts/release-notes.ps1 does, so the notes "What's new" shows
/// are the ones on the GitHub release: a section starts at <c>## [X.Y.Z]</c> and runs to the next
/// <c>## </c> heading.</summary>
public static partial class Changelog
{
    [GeneratedRegex(@"^##\s+\[(?<version>[^\]]*)\](?<rest>.*)$")]
    private static partial Regex VersionHeading();

    /// <summary>Every version section, in file order. Any <c>## </c> heading ends a section, but only one
    /// with a release version in brackets starts one, so <c>## [Unreleased]</c> and malformed headings
    /// are skipped with their text. If a version appears twice, the first section wins.</summary>
    public static IReadOnlyList<ChangelogSection> Parse(string markdown)
    {
        var sections = new List<ChangelogSection>();
        var seen = new HashSet<Version>();
        Version? version = null;
        var date = "";
        var body = new List<string>();

        void Close()
        {
            if (version is not null && seen.Add(version))
            {
                sections.Add(new ChangelogSection(version, date, string.Join("\n", body).Trim()));
            }

            version = null;
            body.Clear();
        }

        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("## ", StringComparison.Ordinal) || line.StartsWith("##\t", StringComparison.Ordinal))
            {
                Close();
                var m = VersionHeading().Match(line);
                if (m.Success)
                {
                    version = ReleaseVersion.Parse(m.Groups["version"].Value);
                    date = m.Groups["rest"].Value.Trim().TrimStart('-', '–', '—').Trim();
                }

                continue;
            }

            if (version is not null)
            {
                body.Add(line);
            }
        }

        Close();
        return sections;
    }

    /// <summary>The sections a player coming from <paramref name="since"/> has not seen yet, newest first:
    /// every version after <paramref name="since"/> up to <paramref name="current"/>, so skipped updates
    /// are covered too. With <paramref name="since"/> unknown, only <paramref name="current"/>'s section.
    /// Empty when the changelog has no section for those versions.</summary>
    public static IReadOnlyList<ChangelogSection> Since(string markdown, Version? since, Version current) =>
        Parse(markdown)
            .Where(s => since is null ? s.Version == current : s.Version > since && s.Version <= current)
            .OrderByDescending(s => s.Version)
            .ToList();
}
