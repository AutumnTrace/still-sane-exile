using StillSaneExile.Domain.Guide;

namespace StillSaneExile.Domain.Tests;

public class BuildLinkTests
{
    [Theory]
    [InlineData("https://mobalytics.gg/poe-2/builds/some-build", true)]
    [InlineData("  https://www.mobalytics.gg/poe-2/builds  ", true)]
    [InlineData("https://example.com/my-guide", false)]
    public void Any_https_link_is_saved_and_mobalytics_is_recognised(string text, bool mobalytics)
    {
        Assert.True(BuildLink.TryParse(text, out var link, out var error), error);
        Assert.Equal(mobalytics, BuildLink.IsMobalytics(link!));
        Assert.Equal(text.Trim(), link!.OriginalString);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("mobalytics.gg/poe-2/builds")]
    [InlineData("http://mobalytics.gg/poe-2/builds")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("https://user:secret@mobalytics.gg/")]
    [InlineData("https://localhost/guide")]
    [InlineData("https://127.0.0.1/guide")]
    [InlineData("https://mobalytics.gg/a https://mobalytics.gg/b")]
    public void Anything_else_is_refused_with_a_reason(string text)
    {
        Assert.False(BuildLink.TryParse(text, out var link, out var error));
        Assert.Null(link);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void A_lookalike_host_is_not_mobalytics()
    {
        Assert.True(BuildLink.TryParse("https://mobalytics.gg.example.com/x", out var link, out _));
        Assert.False(BuildLink.IsMobalytics(link!));
    }
}
