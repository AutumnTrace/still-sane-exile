using StillSaneExile.Domain.Releases;

namespace StillSaneExile.Domain.Tests;

public class ChangelogTests
{
    private const string Sample = """
        # Changelog

        Every release is a tag. See [docs/RELEASING.md](docs/RELEASING.md).

        ## [Unreleased]

        - Not out yet.

        ## [0.4.0] - 2026-12-10

        - Four.

        ## [0.3.0] - 2026-12-01

        - Three, line one
          wrapped.
        - **Bold** and `code`.

        ## [0.2.0] - 2026-11-27

        - Two.

        ## [0.1.0] - 2026-11-20

        - One.
        """;

    private static readonly Version V010 = new(0, 1, 0);
    private static readonly Version V020 = new(0, 2, 0);
    private static readonly Version V030 = new(0, 3, 0);
    private static readonly Version V040 = new(0, 4, 0);
    private static readonly Version V050 = new(0, 5, 0);

    [Fact]
    public void One_version_gives_its_own_section()
    {
        var s = Assert.Single(Changelog.Since(Sample, since: null, V030));
        Assert.Equal(V030, s.Version);
        Assert.Equal("2026-12-01", s.Date);
        Assert.Equal("- Three, line one\n  wrapped.\n- **Bold** and `code`.", s.Body);
    }

    [Fact]
    public void Skipped_versions_are_all_shown_newest_first()
    {
        var sections = Changelog.Since(Sample, since: V010, V040);
        Assert.Equal([V040, V030, V020], sections.Select(s => s.Version));
    }

    [Fact]
    public void Missing_section_gives_nothing()
    {
        Assert.Empty(Changelog.Since(Sample, since: null, V050));
        Assert.Empty(Changelog.Since(Sample, since: V040, V050));
    }

    [Fact]
    public void Missing_current_section_still_shows_the_skipped_ones()
    {
        var sections = Changelog.Since(Sample, since: V020, V050);
        Assert.Equal([V040, V030], sections.Select(s => s.Version));
    }

    [Fact]
    public void Unreleased_and_intro_text_belong_to_no_version()
    {
        var sections = Changelog.Parse(Sample);
        Assert.Equal([V040, V030, V020, V010], sections.Select(s => s.Version));
        Assert.Equal("- Four.", sections[0].Body);
        Assert.DoesNotContain(sections, s => s.Body.Contains("Not out yet", StringComparison.Ordinal));
    }

    [Fact]
    public void Malformed_headings_end_a_section_but_start_none()
    {
        const string md = """
            ## [0.3.0] – 2026-12-01
            - Three.
            ### Fixed
            - Kept: a level-3 heading stays inside the section.
            ## 0.2.9 - no brackets
            - Lost.
            ## [0.2] - two parts
            - Lost too.
            ## [0.2.0]
            - Two.
            """;
        var sections = Changelog.Parse(md);
        Assert.Equal([V030, V020], sections.Select(s => s.Version));
        Assert.Equal("2026-12-01", sections[0].Date);
        Assert.Equal("- Three.\n### Fixed\n- Kept: a level-3 heading stays inside the section.", sections[0].Body);
        Assert.Equal("", sections[1].Date);
        Assert.Equal("- Two.", sections[1].Body);
    }

    [Fact]
    public void Windows_line_endings_and_duplicate_versions()
    {
        var md = "## [0.2.0] - 2026-11-27\r\n\r\n- First.\r\n\r\n## [0.2.0] - again\r\n- Second.\r\n";
        var s = Assert.Single(Changelog.Parse(md));
        Assert.Equal("- First.", s.Body);
    }

    [Fact]
    public void Bullets_join_their_wrapped_lines()
    {
        var blocks = ReleaseNotes.Parse("- Three, line one\n  wrapped.\n- Next.");
        Assert.Equal(2, blocks.Count);
        Assert.All(blocks, b => Assert.Equal(NoteBlockKind.Bullet, b.Kind));
        Assert.Equal("Three, line one wrapped.", Text(blocks[0]));
    }

    [Fact]
    public void Headings_paragraphs_and_rules()
    {
        var blocks = ReleaseNotes.Parse("### Added\n\nFirst public build:\nthe foundation.\n\n---\n* One thing");
        Assert.Equal([NoteBlockKind.Heading, NoteBlockKind.Paragraph, NoteBlockKind.Bullet], blocks.Select(b => b.Kind));
        Assert.Equal("Added", Text(blocks[0]));
        Assert.Equal("First public build: the foundation.", Text(blocks[1]));
        Assert.Equal("One thing", Text(blocks[2]));
    }

    [Fact]
    public void Bold_code_and_links_become_spans()
    {
        var spans = ReleaseNotes.ParseInline("**Price check** replaces `F3` and [the release](https://example.com/r).");
        Assert.Equal(
            [
                new NoteSpan("Price check", NoteStyle.Bold),
                new NoteSpan(" replaces "),
                new NoteSpan("F3", NoteStyle.Code),
                new NoteSpan(" and "),
                new NoteSpan("the release", NoteStyle.None, "https://example.com/r"),
                new NoteSpan("."),
            ],
            spans);
    }

    [Fact]
    public void Styles_nest_inside_bold_and_links()
    {
        Assert.Equal(
            [new NoteSpan("Press ", NoteStyle.Bold), new NoteSpan("F3", NoteStyle.Bold | NoteStyle.Code)],
            ReleaseNotes.ParseInline("**Press `F3`**"));
        Assert.Equal(
            [new NoteSpan("sse.svg", NoteStyle.Code, "https://example.com/a")],
            ReleaseNotes.ParseInline("[`sse.svg`](https://example.com/a)"));
    }

    [Fact]
    public void Links_that_are_not_web_links_keep_only_their_text()
    {
        Assert.Equal([new NoteSpan("docs/RELEASING.md")], ReleaseNotes.ParseInline("[docs/RELEASING.md](docs/RELEASING.md)"));
        Assert.Equal([new NoteSpan("run me")], ReleaseNotes.ParseInline("[run me](file:///C:/Windows/notepad.exe)"));
    }

    [Fact]
    public void Unclosed_markers_stay_as_typed()
    {
        Assert.Equal([new NoteSpan("a **b and `c and [d](")], ReleaseNotes.ParseInline("a **b and `c and [d]("));
    }

    private static string Text(NoteBlock block) => string.Concat(block.Spans.Select(s => s.Text));
}
