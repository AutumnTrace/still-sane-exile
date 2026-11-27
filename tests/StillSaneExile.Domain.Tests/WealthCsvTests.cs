using System.Text;
using StillSaneExile.Domain.Wealth;

namespace StillSaneExile.Domain.Tests;

public class WealthCsvTests
{
    private const string Demo = "Demo League (synthetic)";
    private const string Header = "league,tab,item_id,variant,quantity,snapshot_time";
    private static readonly DateTimeOffset Now = Fixtures.DemoNow;

    private static WealthCsvResult Read(string text, string league = Demo) => WealthCsv.Read(text, "test.csv", league, Now);

    private static WealthCsvResult Rows(params string[] rows) => Read(string.Join("\n", [Header, .. rows]));

    private static string Row(string quantity = "5", string time = "2026-11-27T10:00:00Z", string tab = "Currency", string id = "divine", string variant = "", string league = Demo) =>
        $"{league},{tab},{id},{variant},{quantity},{time}";

    private static WealthCsvRow One(string row)
    {
        var result = Rows(row);
        Assert.Null(result.FileError);
        return Assert.Single(result.Rows);
    }

    private static string Error(string row) => Assert.Single(One(row).Errors);

    [Fact]
    public void Example_file_reads_cleanly()
    {
        var result = Read(Fixtures.Text("wealth/example.csv"));

        Assert.True(result.Ok, result.FileError ?? string.Join(" | ", result.Rows.SelectMany(r => r.Errors)));
        Assert.Equal(8, result.ReadyCount);
        Assert.Equal([2, 3, 4, 5, 6, 7, 8, 9], result.Rows.Select(r => r.Row));
        Assert.Empty(result.OtherLeagues);

        var ring = result.Entries.Single(e => e.ItemId == "unique.sanitys-last-loop");
        Assert.Equal("Rings, keep", ring.Tab);
        Assert.Null(ring.Variant);
        Assert.Equal(1, ring.Quantity);
        Assert.Equal(HoldingOrigin.CsvImport, ring.Origin);
        Assert.Equal("test.csv", ring.Source);
        Assert.Equal(Demo, ring.League);

        var rune = result.Entries.Single(e => e.ItemId == "greater-iron-rune");
        Assert.Equal(new DateTimeOffset(2026, 11, 27, 10, 5, 0, TimeSpan.Zero), rune.SnapshotAt);
        Assert.Equal(TimeSpan.Zero, rune.SnapshotAt.Offset);
    }

    [Fact]
    public void File_with_mistakes_reports_each_row_on_its_own()
    {
        var result = Read(Fixtures.Text("wealth/needs-fixing.csv"));

        Assert.Null(result.FileError);
        Assert.False(result.Ok);
        Assert.Equal(2, result.ReadyCount);
        Assert.Equal(5, result.ErrorCount);
        string Only(int row) => Assert.Single(result.Rows.Single(r => r.Row == row).Errors);

        Assert.Contains("thousands separator", Only(4));
        Assert.Contains("1200", Only(4));
        Assert.Contains("has a space in it", Only(5));
        var rune = result.Rows.Single(r => r.Row == 6).Errors;
        Assert.Equal(2, rune.Count);
        Assert.Contains(rune, e => e.Contains("isn't a whole number", StringComparison.Ordinal));
        Assert.Contains(rune, e => e.Contains("has no time zone", StringComparison.Ordinal));
        Assert.Equal($"This row is for league \"Standard\"; you are on \"{Demo}\".", Only(7));
        Assert.Equal("Same tab, item, variant and snapshot time as row 2.", Only(8));
        Assert.Equal(["Standard"], result.OtherLeagues);
        Assert.Equal("Demo League (synthetic), Currency, Chaos Orb, , 40, 2026-11-27T11:00:00Z", result.Rows.Single(r => r.Row == 5).Raw);
    }

    [Fact]
    public void Header_names_ignore_case_spacing_and_order_and_variant_is_optional()
    {
        var result = Read($"Snapshot Time,QUANTITY,Item ID,Tab,League\n2026-11-27T10:00:00Z,7,Divine,Currency,{Demo}");

        var entry = Assert.Single(result.Entries);
        Assert.Equal(("Currency", "divine", (string?)null, 7L), (entry.Tab, entry.ItemId, entry.Variant, entry.Quantity));
    }

    [Theory]
    [InlineData("league,tab,item_id,variant,quantity,snapshot_time,notes", "Unknown column \"notes\"")]
    [InlineData("league,tab,item_id,varient,quantity,snapshot_time", "Unknown column \"varient\"")]
    [InlineData("league,tab,item_id,quantity", "no snapshot_time column")]
    [InlineData("league,tab,tab,item_id,quantity,snapshot_time", "tab appears twice")]
    [InlineData("league,tab,item_id,variant,quantity,snapshot_time,", "Column 7 of the header has no name")]
    [InlineData("league;tab;item_id;variant;quantity;snapshot_time", "semicolons")]
    [InlineData("Demo,Currency,divine,,5,2026-11-27T10:00:00Z", "The first row must be the header")]
    public void Header_problems_stop_the_whole_file(string header, string expected)
    {
        var result = Read($"{header}\n{Row()}");

        Assert.Contains(expected, result.FileError);
        Assert.Empty(result.Rows);
        Assert.False(result.Ok);
    }

    [Theory]
    [InlineData("")]
    [InlineData("﻿")]
    [InlineData("\r\n\r\n  \n")]
    public void Empty_file_is_one_clear_error(string text) => Assert.Equal("The file is empty.", Read(text).FileError);

    [Fact]
    public void Header_without_rows_is_an_error() => Assert.Equal("The file has a header but no rows.", Read(Header + "\r\n\r\n").FileError);

    [Fact]
    public void Byte_order_mark_is_skipped()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes($"{Header}\r\n{Row()}\r\n")).ToArray();

        Assert.True(WealthCsv.Read(bytes, "bom.csv", Demo, Now).Ok);
    }

    [Fact]
    public void Text_that_is_not_utf8_is_refused()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes($"{Header}\r\n{Row()}\r\n")).ToArray();

        Assert.Contains("isn't UTF-8", WealthCsv.Read(bytes, "utf16.csv", Demo, Now).FileError);
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    [InlineData("\r")]
    public void Any_line_ending_works_and_blank_lines_keep_row_numbers(string eol)
    {
        var text = string.Join(eol, Header, Row("1"), "", Row("2", tab: "Dump"), "   ", ",,,,,", Row("3", tab: "Maps")) + eol;

        var result = Read(text);

        Assert.True(result.Ok);
        Assert.Equal([2, 4, 7], result.Rows.Select(r => r.Row));
        Assert.Equal([1L, 2L, 3L], result.Entries.Select(e => e.Quantity));
    }

    [Fact]
    public void Quoted_values_keep_commas_and_doubled_quotes()
    {
        var entry = One($"\"{Demo}\",\"Bob's \"\"best\"\", tab\",\"divine\",\"\",\"12\",\"2026-11-27T10:00:00Z\"").Entry!;

        Assert.Equal("Bob's \"best\", tab", entry.Tab);
        Assert.Equal(12, entry.Quantity);
        Assert.Null(entry.Variant);
    }

    [Fact]
    public void Line_break_inside_a_quoted_value_is_an_error_and_later_rows_keep_their_numbers()
    {
        var result = Read($"{Header}\r\n{Demo},\"Two\r\nlines\",divine,,1,2026-11-27T10:00:00Z\r\n{Row()}\r\n");

        Assert.Equal([2, 4], result.Rows.Select(r => r.Row));
        Assert.Contains("line break", Assert.Single(result.Rows[0].Errors));
        Assert.True(result.Rows[1].Ok);
    }

    [Fact]
    public void Unclosed_quote_is_reported_where_it_starts()
    {
        var result = Read($"{Header}\n{Row()}\n{Demo},\"Currency,divine,,1,2026-11-27T10:00:00Z\n{Row("9", tab: "Dump")}\n");

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(3, result.Rows[1].Row);
        Assert.Contains("never ends", Assert.Single(result.Rows[1].Errors));
    }

    [Fact]
    public void Text_after_a_closing_quote_is_an_error() =>
        Assert.Contains("text after its closing quote", Error($"{Demo},\"Currency\"x,divine,,1,2026-11-27T10:00:00Z"));

    [Fact]
    public void Unquoted_thousands_separator_shows_up_as_an_extra_value() =>
        Assert.Contains("1200, not 1,200", Error(Row("1,200")));

    [Fact]
    public void Too_few_values_is_an_error() =>
        Assert.Equal("This row has 5 values; the header has 6.", Error($"{Demo},Currency,divine,5,2026-11-27T10:00:00Z"));

    [Theory]
    [InlineData("0", 0)]
    [InlineData("7", 7)]
    [InlineData(" 1200 ", 1200)]
    [InlineData("007", 7)]
    [InlineData("1000000000", 1_000_000_000)]
    public void Quantity_is_a_whole_number_of_items(string text, long expected) =>
        Assert.Equal(expected, One(Row($"\"{text}\"")).Entry!.Quantity);

    [Theory]
    [InlineData("", "Quantity is empty.")]
    [InlineData("1.5", "Quantity 1.5 isn't a whole number. Stacks count whole items.")]
    [InlineData("12,5", "isn't a whole number")]
    [InlineData("-3", "Quantity -3 is negative. Use 0 when there are none left.")]
    [InlineData("1,200", "Quantity \"1,200\" has a thousands separator. Write digits only: 1200.")]
    [InlineData("1 200", "thousands separator")]
    [InlineData("1.200.000", "Write digits only: 1200000.")]
    [InlineData("1000000001", "is more than 1,000,000,000")]
    [InlineData("99999999999999999999999", "is more than")]
    [InlineData("+5", "isn't a number")]
    [InlineData("1e3", "isn't a number")]
    [InlineData("lots", "Quantity \"lots\" isn't a number. Write digits only, e.g. 1200.")]
    public void Bad_quantities_say_what_to_write(string text, string expected) =>
        Assert.Contains(expected, Error(Row($"\"{text}\"")));

    [Theory]
    [InlineData("2026-11-27T10:00:00Z")]
    [InlineData("2026-11-27T10:00:00z")]
    [InlineData("2026-11-27 10:00:00Z")]
    [InlineData("2026-11-27T10:00Z")]
    [InlineData("2026-11-27T12:00:00+02:00")]
    [InlineData("2026-11-27T12:00:00+0200")]
    [InlineData("2026-11-27T12:00+02")]
    [InlineData("2026-11-27T05:30:00-04:30")]
    [InlineData("2026-11-27T10:00:00.000Z")]
    public void Snapshot_time_accepts_iso_8601_with_a_zone(string time) =>
        Assert.Equal(new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero), One(Row(time: time)).Entry!.SnapshotAt);

    [Fact]
    public void Fractions_of_a_second_are_kept() =>
        Assert.Equal(new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero).AddTicks(1_234_500), One(Row(time: "2026-11-27T10:00:00.12345Z")).Entry!.SnapshotAt);

    [Theory]
    [InlineData("", "Snapshot time is empty.")]
    [InlineData("2026-11-27T10:00:00", "Snapshot time 2026-11-27T10:00:00 has no time zone. Add Z for UTC, or an offset like +01:00.")]
    [InlineData("27/11/2026 10:00", "isn't an ISO-8601 time")]
    [InlineData("2026-11-27", "isn't an ISO-8601 time")]
    [InlineData("1764237600", "isn't an ISO-8601 time")]
    [InlineData("2026-02-30T10:00:00Z", "isn't a real date and time")]
    [InlineData("2026-11-27T24:00:00Z", "isn't a real date and time")]
    [InlineData("2026-11-27T10:00:00+02:75", "isn't a real date and time")]
    [InlineData("2026-11-27T10:00:00+15:00", "isn't a real date and time")]
    [InlineData("2026-11-27T11:40:00Z", "is in the future: it is 2026-11-27 11:30 UTC now")]
    public void Bad_snapshot_times_are_refused(string time, string expected) =>
        Assert.Contains(expected, Error(Row(time: $"\"{time}\"")));

    [Fact]
    public void A_few_minutes_of_clock_drift_is_allowed() =>
        Assert.True(One(Row(time: "2026-11-27T11:34:00Z")).Ok);

    [Fact]
    public void Item_id_is_trimmed_and_lower_case_and_variant_too()
    {
        var entry = One(Row(id: "  Greater-Iron-Rune ", variant: " Two Sockets ")).Entry!;

        Assert.Equal("greater-iron-rune", entry.ItemId);
        Assert.Equal("two sockets", entry.Variant);
    }

    [Theory]
    [InlineData("tab", "", "Tab is empty.")]
    [InlineData("id", "", "Item id is empty.")]
    [InlineData("id", "Divine Orb", "Item id \"Divine Orb\" has a space in it.")]
    [InlineData("league", "", "League is empty.")]
    public void Empty_and_mistyped_fields_are_named(string field, string value, string expected)
    {
        var row = field switch
        {
            "tab" => Row(tab: value),
            "id" => Row(id: value),
            _ => Row(league: value),
        };

        Assert.Contains(expected, Error(row));
    }

    [Fact]
    public void Tab_longer_than_the_limit_is_refused() =>
        Assert.Contains("longer than 64", Error(Row(tab: new string('t', 65))));

    [Fact]
    public void Every_problem_in_a_row_is_listed() =>
        Assert.Equal(3, One(Row("x", "soon", tab: "")).Errors.Count);

    [Fact]
    public void League_must_be_the_active_one_but_case_and_spaces_do_not_matter()
    {
        var result = Rows(Row(league: "  demo league (SYNTHETIC) "), Row(league: "Standard", tab: "A"), Row(league: "Hardcore", tab: "B"), Row(league: "standard", tab: "C"));

        Assert.Equal(Demo, result.Rows[0].Entry!.League);
        Assert.All(result.Rows.Skip(1), r => Assert.Null(r.Entry));
        Assert.Equal($"This row is for league \"Hardcore\"; you are on \"{Demo}\".", Assert.Single(result.Rows[2].Errors));
        Assert.Equal(["Hardcore", "Standard"], result.OtherLeagues);
    }

    [Fact]
    public void Live_league_refuses_demo_rows()
    {
        var result = WealthCsv.Read(Fixtures.Text("wealth/example.csv"), "example.csv", "Standard", Now);

        Assert.Equal(0, result.ReadyCount);
        Assert.Equal([Demo], result.OtherLeagues);
    }

    [Fact]
    public void Duplicate_key_is_an_error_on_the_later_row_even_with_another_offset()
    {
        var result = Rows(
            Row("5"),
            Row("5", tab: "Dump"),
            Row("6", time: "2026-11-27T11:00:00+01:00"),
            Row("5", time: "2026-11-27T09:00:00Z"),
            Row("5", variant: "x"));

        Assert.Equal([2, 3, 5, 6], result.Rows.Where(r => r.Ok).Select(r => r.Row));
        Assert.Equal("Same tab, item, variant and snapshot time as row 2.", Assert.Single(result.Rows[2].Errors));
    }

    [Fact]
    public void Row_cap_is_ten_thousand()
    {
        var rows = Enumerable.Range(0, WealthCsv.MaxRows).Select(i => Row(tab: $"T{i}")).ToArray();
        Assert.True(Rows(rows).Ok);

        var result = Rows([.. rows, Row(tab: "one too many")]);
        Assert.Equal("The file has more than 10,000 rows. Split it into smaller files.", result.FileError);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void Size_cap_is_four_megabytes()
    {
        var bytes = new byte[WealthCsv.MaxBytes + 1];

        Assert.Equal("The file is 4 MB; the limit is 4 MB. Split it into smaller files.", WealthCsv.Read(bytes, "big.csv", Demo, Now).FileError);
        Assert.Contains("the limit is 4 MB", WealthCsv.Read(new string('a', WealthCsv.MaxBytes + 1), "big.csv", Demo, Now).FileError);
    }
}
