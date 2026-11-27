using StillSaneExile.Domain.Releases;

namespace StillSaneExile.Domain.Tests;

public class WhatsNewTests
{
    [Fact]
    public void First_install_shows_nothing_and_remembers_the_version_at_once()
    {
        var d = WhatsNew.Decide("0.3.0", lastRun: null, onboarded: false, installed: true);
        Assert.False(d.Show);
        Assert.True(d.Remember);
    }

    [Fact]
    public void Update_shows_the_notes_since_the_last_run_version()
    {
        var d = WhatsNew.Decide("0.4.0", "0.2.0", onboarded: true, installed: true);
        Assert.True(d.Show);
        Assert.Equal(new Version(0, 2, 0), d.Since);
        Assert.True(d.Remember);
    }

    [Fact]
    public void Update_from_a_build_that_stored_no_version_shows_this_version_only()
    {
        // 0.2.0 and earlier never wrote the setting; having finished onboarding is what gives them away.
        var d = WhatsNew.Decide("0.3.0", lastRun: null, onboarded: true, installed: true);
        Assert.True(d.Show);
        Assert.Null(d.Since);
        Assert.True(d.Remember);
    }

    [Fact]
    public void Same_version_shows_nothing()
    {
        Assert.Equal(WhatsNewDecision.Nothing, WhatsNew.Decide("0.3.0", "0.3.0", onboarded: true, installed: true));
    }

    [Fact]
    public void Downgrade_shows_nothing_and_keeps_the_newer_record()
    {
        var d = WhatsNew.Decide("0.3.0", "0.4.0", onboarded: true, installed: true);
        Assert.False(d.Show);
        Assert.False(d.Remember);
    }

    [Fact]
    public void Development_build_never_shows_or_remembers()
    {
        Assert.Equal(WhatsNewDecision.Nothing, WhatsNew.Decide("0.4.0", "0.2.0", onboarded: true, installed: false));
        Assert.Equal(WhatsNewDecision.Nothing, WhatsNew.Decide("0.4.0", null, onboarded: true, installed: false));
    }

    [Fact]
    public void Versions_compare_as_numbers_not_text()
    {
        var d = WhatsNew.Decide("0.10.0", "0.9.0", onboarded: true, installed: true);
        Assert.True(d.Show);
        Assert.Equal(new Version(0, 9, 0), d.Since);
    }

    [Fact]
    public void Update_before_onboarding_is_finished_still_shows_later()
    {
        // The app keeps it out of the way while onboarding is open; the decision itself still says show.
        var d = WhatsNew.Decide("0.4.0", "0.3.0", onboarded: false, installed: true);
        Assert.True(d.Show);
        Assert.Equal(new Version(0, 3, 0), d.Since);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Unreadable_stored_version_counts_as_none(bool onboarded, bool show)
    {
        var d = WhatsNew.Decide("0.3.0", "not a version", onboarded, installed: true);
        Assert.Equal(show, d.Show);
        Assert.Null(d.Since);
        Assert.True(d.Remember);
    }

    [Fact]
    public void Unreadable_running_version_shows_nothing()
    {
        Assert.Equal(WhatsNewDecision.Nothing, WhatsNew.Decide("dev", "0.2.0", onboarded: true, installed: true));
    }

    [Theory]
    [InlineData("0.2.0", 0, 2, 0)]
    [InlineData("v0.2.0", 0, 2, 0)]
    [InlineData("0.2.0+3980160", 0, 2, 0)]
    [InlineData(" 1.10.3 ", 1, 10, 3)]
    public void Release_versions_parse(string text, int major, int minor, int patch)
    {
        Assert.Equal(new Version(major, minor, patch), ReleaseVersion.Parse(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0.2")]
    [InlineData("0.2.0.1")]
    [InlineData("0.2.0-beta")]
    [InlineData("Unreleased")]
    public void Anything_else_is_not_a_release_version(string? text)
    {
        Assert.Null(ReleaseVersion.Parse(text));
    }
}
