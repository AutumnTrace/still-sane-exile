using Microsoft.Data.Sqlite;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Domain.Wealth;

namespace StillSaneExile.Desktop.Tests;

/// <summary>The SQLite side of the wealth ledger: one transaction per write, idempotent re-imports,
/// conflicts roll back, leagues and profiles never mix, and an older database gains the table.</summary>
public class WealthStoreTests
{
    private const string League = "Standard";
    private static readonly DateTimeOffset At = new(2026, 11, 27, 11, 0, 0, TimeSpan.Zero);

    private static HoldingEntry Entry(string item, long quantity, DateTimeOffset? at = null, string tab = "Currency", string? variant = null, string league = League) =>
        new(league, tab, item, variant, quantity, at ?? At, HoldingOrigin.CsvImport, "example.csv");

    [Fact]
    public void An_import_is_stored_and_read_back_in_time_order()
    {
        using var dir = new TempDir();
        var store = new Store(dir.Sub("sse.db"));

        var write = store.AddWealthObservations("live", League, [Entry("divine", 14, At.AddHours(1)), Entry("chaos", 120), Entry("divine", 12)], At);

        Assert.Equal(new WealthWrite(3, 0, null), write);
        var rows = store.WealthObservations("live", League);
        Assert.Equal(["chaos", "divine", "divine"], rows.Select(r => r.ItemId).Order());
        Assert.Equal(rows.OrderBy(r => r.SnapshotAt).Select(r => r.SnapshotAt), rows.Select(r => r.SnapshotAt));
        Assert.All(rows, r => Assert.Equal("example.csv", r.Source));
    }

    [Fact]
    public void Importing_the_same_rows_twice_adds_nothing()
    {
        using var dir = new TempDir();
        var store = new Store(dir.Sub("sse.db"));
        HoldingEntry[] rows = [Entry("divine", 14), Entry("chaos", 120, variant: null), Entry("greater-iron-rune", 18, tab: "Runes")];

        store.AddWealthObservations("live", League, rows, At);
        var again = store.AddWealthObservations("live", League, rows, At.AddMinutes(5));

        Assert.Equal(new WealthWrite(0, 3, null), again);
        Assert.Equal(3, store.WealthObservations("live", League).Count);
    }

    [Fact]
    public void The_same_instant_in_another_offset_is_the_same_row()
    {
        using var dir = new TempDir();
        var store = new Store(dir.Sub("sse.db"));

        store.AddWealthObservations("live", League, [Entry("divine", 14, At)], At);
        var again = store.AddWealthObservations("live", League, [Entry("divine", 14, At.ToOffset(TimeSpan.FromHours(2)))], At);

        Assert.Equal(new WealthWrite(0, 1, null), again);
    }

    [Fact]
    public void A_conflicting_quantity_rolls_back_the_whole_write()
    {
        using var dir = new TempDir();
        var store = new Store(dir.Sub("sse.db"));
        store.AddWealthObservations("live", League, [Entry("divine", 14)], At);

        // The first row is new and would be added, but the second contradicts what is stored.
        var write = store.AddWealthObservations("live", League, [Entry("chaos", 120), Entry("divine", 15)], At);

        Assert.Equal(0, write.Added);
        Assert.NotNull(write.Conflict);
        Assert.Contains("already stored as 14", write.Conflict);
        var rows = store.WealthObservations("live", League);
        Assert.Equal("divine", Assert.Single(rows).ItemId);
    }

    [Fact]
    public void Rows_from_another_league_are_refused_and_nothing_is_stored()
    {
        using var dir = new TempDir();
        var store = new Store(dir.Sub("sse.db"));

        Assert.Throws<InvalidOperationException>(() =>
            store.AddWealthObservations("live", League, [Entry("divine", 14), Entry("chaos", 3, league: "Hardcore")], At));

        Assert.Empty(store.WealthObservations("live", League));
        Assert.Empty(store.WealthObservations("live", "Hardcore"));
    }

    [Fact]
    public void Profiles_and_leagues_never_see_each_others_holdings()
    {
        using var dir = new TempDir();
        var store = new Store(dir.Sub("sse.db"));
        store.AddWealthObservations("live", League, [Entry("divine", 14)], At);
        store.AddWealthObservations("demo", League, [Entry("divine", 99)], At);
        store.AddWealthObservations("live", "Hardcore", [Entry("divine", 1, league: "Hardcore")], At);

        Assert.Equal(14, Assert.Single(store.WealthObservations("live", League)).Quantity);
        Assert.Equal(99, Assert.Single(store.WealthObservations("demo", League)).Quantity);
        Assert.Equal(1, Assert.Single(store.WealthObservations("live", "Hardcore")).Quantity);
    }

    [Fact]
    public void Resetting_the_demo_clears_demo_holdings_and_keeps_live_ones()
    {
        using var dir = new TempDir();
        var store = new Store(dir.Sub("sse.db"));
        store.AddWealthObservations("live", League, [Entry("divine", 14)], At);
        store.AddWealthObservations("demo", League, [Entry("divine", 99)], At);

        store.ResetDemo();

        Assert.Empty(store.WealthObservations("demo", League));
        Assert.Single(store.WealthObservations("live", League));
    }

    [Fact]
    public void A_database_from_before_wealth_opens_with_its_settings_and_gains_the_table()
    {
        using var dir = new TempDir();
        var path = dir.Sub("sse.db");
        using (var old = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
        {
            old.Open();
            using var cmd = old.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO settings(key, value) VALUES ('onboarded', '1');
                """;
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
        var store = new Store(path);

        Assert.Equal("1", store.Get("onboarded"));
        Assert.Equal(1, store.AddWealthObservations("live", League, [Entry("divine", 14)], At).Added);
    }
}
