using StillSaneExile.Domain.Data;
using StillSaneExile.Domain.Items;
using StillSaneExile.Domain.Pricing;

namespace StillSaneExile.Domain.Tests;

internal static class Fixtures
{
    public static readonly DateTimeOffset DemoNow = new(2026, 11, 27, 11, 30, 0, TimeSpan.Zero);

    private static readonly Lazy<string> Root = new(() =>
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "fixtures")))
        {
            dir = dir.Parent;
        }

        return dir is null ? throw new DirectoryNotFoundException("fixtures/ not found above the test output.") : Path.Combine(dir.FullName, "fixtures");
    });

    public static string Text(string relative) => File.ReadAllText(Path.Combine(Root.Value, relative));

    /// <summary>A file from the shipped <c>data/</c> folder (route packs), next to <c>fixtures/</c>.</summary>
    public static string DataText(string relative) => File.ReadAllText(Path.Combine(Root.Value, "..", "data", relative));

    public static ItemSnapshot Item(string name)
    {
        var result = ItemTextParser.Parse(Text($"items/{name}.txt"), DemoNow, name);
        Assert.True(result.Ok, result.Error);
        return result.Item!;
    }

    /// <summary>The practice pack: the game's 0.5.5 stats for the practice items, with made-up weights.</summary>
    public const string PackFile = "packs/practice-0.5.5.json";

    public static DataPack Pack()
    {
        var result = DataPackLoader.Load(Text(PackFile));
        Assert.True(result.Pack is not null, result.Error);
        return result.Pack!;
    }

    public static PriceBook DemoBook() => PriceBook.FromJson(Text("demo/pricebook.json"));
}
