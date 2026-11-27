using System.Globalization;
using StillSaneExile.Domain.Pricing;

namespace StillSaneExile.Domain.Tests;

public sealed class PriceBookArchiveTests : IDisposable
{
    private const string League = "Runes of Aldur";
    private static readonly DateTimeOffset T0 = Fixtures.DemoNow;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "sse-tests", Guid.NewGuid().ToString("N"), "prices");

    public void Dispose()
    {
        var parent = Path.GetDirectoryName(_root)!;
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private static PriceBook Book(string league, DateTimeOffset at, decimal alch = 0.005m) =>
        PoeNinjaBook.Build(league, [new PoeNinjaPayload(new PoeNinjaCategory(PoeNinjaOverview.Exchange, "Currency"),
            Fixtures.Text("poeninja/exchange-currency.json").Replace("\"primaryValue\":0.005,", $"\"primaryValue\":{alch.ToString(CultureInfo.InvariantCulture)},", StringComparison.Ordinal), at)], at).Book;

    [Fact]
    public void The_newest_snapshot_of_the_league_loads_and_leagues_never_share_one()
    {
        var archive = new PriceBookArchive(_root);
        archive.Save(Book(League, T0, 0.004m));
        archive.Save(Book(League, T0.AddHours(1), 0.006m));
        archive.Save(Book("HC Runes of Aldur", T0.AddHours(2), 0.009m));

        var newest = archive.Newest(League)!;

        Assert.Equal(League, newest.League);
        Assert.Equal(T0.AddHours(1), newest.CreatedAt);
        Assert.Equal(0.006m, newest.Find("alch")!.Amount);
        Assert.Equal(0.009m, archive.Newest("HC Runes of Aldur")!.Find("alch")!.Amount);
        Assert.Null(archive.Newest("Standard"));
        Assert.NotEqual(archive.FolderFor(League), archive.FolderFor("HC Runes of Aldur"));
        Assert.NotEqual(archive.FolderFor("HC X"), archive.FolderFor("hc-x"));
    }

    [Fact]
    public void Only_the_newest_few_are_kept()
    {
        var archive = new PriceBookArchive(_root, keep: 3);
        for (var h = 0; h < 6; h++)
        {
            archive.Save(Book(League, T0.AddHours(h)));
        }

        var files = archive.Files(League);
        Assert.Equal(3, files.Count);
        Assert.EndsWith("poe.ninja-20261127T163000Z.json", files[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_snapshot_is_written_once_and_never_changed()
    {
        var archive = new PriceBookArchive(_root);
        var file = archive.Save(Book(League, T0, 0.004m))!;

        archive.Save(Book(League, T0, 0.008m)); // same snapshot id

        Assert.Equal(0.004m, PriceBook.FromJson(File.ReadAllText(file)).Find("alch")!.Amount);
    }

    [Fact]
    public void A_book_for_another_league_in_the_folder_is_never_loaded()
    {
        var archive = new PriceBookArchive(_root);
        var folder = archive.FolderFor(League);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "poe.ninja-20991231T000000Z.json"), Book("Standard", T0).ToJson());

        Assert.Null(archive.Newest(League));
    }

    [Fact]
    public void A_damaged_snapshot_falls_back_to_the_one_before()
    {
        var archive = new PriceBookArchive(_root);
        archive.Save(Book(League, T0, 0.004m));
        File.WriteAllText(Path.Combine(archive.FolderFor(League), "poe.ninja-20991231T000000Z.json"), "{ half a file");

        Assert.Equal(0.004m, archive.Newest(League)!.Find("alch")!.Amount);
    }

    [Fact]
    public void Synthetic_and_empty_books_are_never_saved()
    {
        var archive = new PriceBookArchive(_root);

        Assert.Null(archive.Save(Fixtures.DemoBook()));
        Assert.Null(archive.Save(PriceBook.Empty(League)));
        Assert.False(Directory.Exists(_root));
    }
}
