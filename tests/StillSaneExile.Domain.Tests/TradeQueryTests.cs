using System.Text.Json;
using System.Text.RegularExpressions;
using StillSaneExile.Domain.Items;
using StillSaneExile.Domain.Reading;
using StillSaneExile.Domain.Trade;

namespace StillSaneExile.Domain.Tests;

public class TradeQueryTests
{
    private const string League = "Runes of Aldur";

    private static readonly Lazy<string> Json = new(() => Fixtures.DataText("trade/trade-data.json"));
    private static readonly Lazy<TradeData> Data = new(() => TradeData.Parse(Json.Value));

    private static (ComparisonWorksheet W, ItemSnapshot Item) Sheet(ItemSnapshot item)
    {
        var reading = ItemReader.Read(item, Fixtures.Pack());
        return (WorksheetFactory.Create(reading, trade: Data.Value), item);
    }

    private static (ComparisonWorksheet W, ItemSnapshot Item) Practice(string name) => Sheet(Fixtures.Item(name));

    private static (ComparisonWorksheet W, ItemSnapshot Item) Typed(string text)
    {
        var result = ItemTextParser.Parse(text, Fixtures.DemoNow, "typed");
        Assert.True(result.Ok, result.Error);
        return Sheet(result.Item!);
    }

    private static TradeSearch Build((ComparisonWorksheet W, ItemSnapshot Item) s) => TradeQuery.Build(s.W, s.Item, League, Data.Value);

    private static WorksheetRow Tick(ComparisonWorksheet w, RowKind kind, string template)
    {
        var row = w.Rows.Single(r => r.Kind == kind && r.Template == template);
        row.Selected = true;
        return row;
    }

    /// <summary>The text the committed data file gives an id, read from the file itself.</summary>
    private static string TextInFile(string id)
    {
        using var doc = JsonDocument.Parse(Json.Value);
        var group = id[..id.IndexOf('.', StringComparison.Ordinal)];
        return doc.RootElement.GetProperty("stats").GetProperty(group).EnumerateArray()
            .Single(e => e.GetProperty("id").GetString() == id).GetProperty("text").GetString()!;
    }

    [Fact]
    public void The_data_file_says_where_and_when_it_came_from_and_is_sorted()
    {
        using var doc = JsonDocument.Parse(Json.Value);
        var root = doc.RootElement;
        Assert.Matches(new Regex(@"^\d{4}-\d{2}-\d{2}$"), root.GetProperty("fetched").GetString());
        var sources = root.GetProperty("sources");
        Assert.Equal("https://www.pathofexile.com/api/trade2/data/stats", sources.GetProperty("stats").GetString());
        Assert.Equal("https://www.pathofexile.com/api/trade2/data/filters", sources.GetProperty("filters").GetString());
        Assert.Equal("https://www.pathofexile.com/api/trade2/data/items", sources.GetProperty("items").GetString());

        // Sorted by id, so a refresh shows only what the site changed.
        foreach (var group in root.GetProperty("stats").EnumerateObject())
        {
            var ids = group.Value.EnumerateArray().Select(e => e.GetProperty("id").GetString()!).ToList();
            Assert.Equal(ids.Order(StringComparer.Ordinal), ids);
        }

        Assert.False(Data.Value.IsEmpty);
        Assert.Equal(Data.Value.Fetched, root.GetProperty("fetched").GetString());
    }

    [Fact]
    public void Every_filter_key_and_option_the_link_uses_is_in_the_data_file()
    {
        var d = Data.Value;
        Assert.All(["category", "rarity", "ilvl"], k => Assert.Contains(k, d.Filters["type_filters"]));
        Assert.Contains("pdps", d.Filters["equipment_filters"]);
        Assert.Contains("corrupted", d.Filters["misc_filters"]);
        Assert.Contains("price", d.Filters["trade_filters"]);
        Assert.Contains(TradeQuery.BuyNowStatus, d.StatusOptions);
        Assert.Contains(TradeQuery.AnyListingStatus, d.StatusOptions);
        Assert.All(["normal", "magic", "rare", "unique"], r => Assert.Contains(r, d.Rarities));
        Assert.All(TradeQuery.KnownCategories, c => Assert.Contains(c, d.Categories));
    }

    [Fact]
    public void Practice_crossbow_searches_physical_dps_and_its_local_attack_speed()
    {
        var search = Build(Practice("rare-crossbow-advanced")); // preset: physical DPS ≥ 319, attack speed ≥ 15

        Assert.Equal(
            """{"query":{"status":{"option":"securable"},"stats":[{"type":"and","filters":[{"id":"explicit.stat_210067635","value":{"min":15}}]}],"filters":{"type_filters":{"filters":{"category":{"option":"weapon.crossbow"},"rarity":{"option":"rare"}}},"equipment_filters":{"filters":{"pdps":{"min":319}}},"misc_filters":{"filters":{"corrupted":{"option":"false"}}}}}}""",
            search.Json);
        Assert.Equal("#% increased Attack Speed (Local)", TextInFile("explicit.stat_210067635"));

        // The practice base is made up ("Expert …" bases are not on the site), so the link searches any
        // rare crossbow and says so instead of sending a base the site would not know.
        Assert.Equal(["Item base (Expert Bombard Crossbow)"], search.ByHand);
        Assert.Empty(search.Unmapped);
    }

    [Fact]
    public void Every_line_on_the_practice_crossbow_maps_to_its_own_group()
    {
        var s = Practice("rare-crossbow-advanced");
        Tick(s.W, RowKind.Explicit, ComparisonWorksheet.LocalPhysPercent);
        Tick(s.W, RowKind.Explicit, ComparisonWorksheet.LocalFlatPhys);
        Tick(s.W, RowKind.Explicit, "#% increased Critical Hit Chance");
        Tick(s.W, RowKind.Explicit, "# to Accuracy Rating");
        Tick(s.W, RowKind.Rune, "#% to Fire Resistance");
        Assert.All(s.W.Rows, r => Assert.True(r.Supported, r.Label));

        var search = Build(s);

        Assert.Contains("""{"id":"explicit.stat_1509134228","value":{"min":157}}""", search.Json);
        Assert.Contains("""{"id":"explicit.stat_587431675","value":{"min":27}}""", search.Json);
        Assert.Contains("""{"id":"explicit.stat_691932474","value":{"min":176}}""", search.Json); // accuracy: local on a weapon
        Assert.Contains("""{"id":"rune.stat_3372524247","value":{"min":16}}""", search.Json);
        Assert.Equal("# to Accuracy Rating (Local)", TextInFile("explicit.stat_691932474"));
        Assert.Equal("#% to Fire Resistance", TextInFile("rune.stat_3372524247"));
        Assert.Empty(search.Unmapped);
    }

    [Fact]
    public void Adds_x_to_y_is_searched_by_its_average_as_the_site_compares_it()
    {
        var s = Practice("rare-crossbow-advanced");
        var flat = Tick(s.W, RowKind.Explicit, ComparisonWorksheet.LocalFlatPhys); // Adds 24 to 39: average 31.5
        Assert.Equal(31.5m, flat.Actual);

        var search = Build(s);

        Assert.Contains("""{"id":"explicit.stat_1940865751","value":{"min":28}}""", search.Json); // ⌊31.5 × 0.9⌋
        Assert.Equal("Adds # to # Physical Damage", TextInFile("explicit.stat_1940865751"));
    }

    [Fact]
    public void Local_stats_follow_the_item_weapon_and_armour_own_global_elsewhere()
    {
        var gloves = Typed("Item Class: Gloves\nRarity: Rare\nGrim Grip\nStocky Mitts\n--------\nItem Level: 80\n--------\n+120 to Armour\n+60 to Accuracy Rating\n10% increased Attack Speed");
        foreach (var r in gloves.W.Rows)
        {
            r.Selected = true;
        }

        var ring = Typed("Item Class: Rings\nRarity: Rare\nKeen Coil\nIron Ring\n--------\nItem Level: 80\n--------\n+40 to Armour");
        ring.W.Rows.Single().Selected = true;

        var g = Build(gloves).Json;
        Assert.Contains("\"explicit.stat_3484657501\"", g); // the gloves' own armour: local
        Assert.Contains("\"explicit.stat_803737631\"", g); // accuracy on gloves counts for the character
        Assert.Contains("\"explicit.stat_681332047\"", g); // so does attack speed
        Assert.Contains("\"armour.gloves\"", g);
        Assert.Contains("\"type\":\"Stocky Mitts\"", g);
        Assert.Contains("\"explicit.stat_809229260\"", Build(ring).Json); // a ring's armour is global
        Assert.Equal("# to Armour", TextInFile("explicit.stat_809229260"));
    }

    [Fact]
    public void A_unique_is_searched_by_name_and_base()
    {
        var search = Build(Typed("Item Class: Belts\nRarity: Unique\nMageblood\nUtility Belt\n--------\nItem Level: 84\n--------\n+30 to Dexterity"));

        Assert.Equal(
            """{"query":{"status":{"option":"securable"},"name":"Mageblood","type":"Utility Belt","filters":{"type_filters":{"filters":{"category":{"option":"accessory.belt"}}},"misc_filters":{"filters":{"corrupted":{"option":"false"}}}}}}""",
            search.Json);
        Assert.Empty(search.ByHand);
        Assert.True(search.Identifies);
    }

    [Fact]
    public void A_plural_game_line_finds_the_sites_singular_stat()
    {
        // The game writes "Has 3 Charm Slots"; the site files it as "Has # Charm Slot". A player who asked
        // for at least 3 slots on a Mageblood got every Mageblood, because the filter was left out.
        var s = Typed("Item Class: Belts\nRarity: Unique\nMageblood\nUtility Belt\n--------\nItem Level: 84\n--------\nHas 3 Charm Slots (implicit)\n--------\n+30 to Dexterity");
        var slots = Tick(s.W, RowKind.Implicit, "Has # Charm Slots");
        slots.Min = 3;

        var search = Build(s);

        Assert.Equal("implicit.stat_1416292992", TradeQuery.StatIdFor(RowKind.Implicit, "Has # Charm Slots", "Belts", Data.Value));
        Assert.Contains("""{"id":"implicit.stat_1416292992","value":{"min":3}}""", search.Json);
        Assert.DoesNotContain(search.ByHand, h => h.Contains("Charm", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Has # Charm Slots", "has # charm slot")]
    [InlineData("#% to all Elemental Resistances", "#% to all elemental resistance")]
    [InlineData("Has # Charm Slot", "has # charm slot")]
    [InlineData("#% increased Mana Regeneration Rate", "#% increased mana regeneration rate")]
    public void Plural_endings_are_ignored_only_for_the_fallback(string template, string key) =>
        Assert.Equal(key, TradeData.SingularKey(template));

    [Fact]
    public void A_made_up_unique_keeps_its_base_and_says_to_set_the_name_by_hand()
    {
        var search = Build(Practice("unique-ring"));

        Assert.Equal(
            """{"query":{"status":{"option":"securable"},"type":"Gold Ring","filters":{"type_filters":{"filters":{"category":{"option":"accessory.ring"},"rarity":{"option":"unique"}}},"misc_filters":{"filters":{"corrupted":{"option":"false"}}}}}}""",
            search.Json);
        Assert.Equal(["Name (Sanity's Last Loop)"], search.ByHand);
        Assert.False(search.Identifies); // a search without the name would price every unique gold ring
    }

    [Fact]
    public void Currency_is_a_plain_search_for_the_stack_by_name()
    {
        // The bulk exchange (/trade2/exchange) takes a different query, not checked yet, so currency links
        // to an ordinary item search: no rarity, no stats, no corrupted filter.
        var search = Build(Practice("currency-exalted"));

        Assert.Equal(
            """{"query":{"status":{"option":"securable"},"type":"Exalted Orb","filters":{"type_filters":{"filters":{"category":{"option":"currency"}}}}}}""",
            search.Json);
        Assert.Empty(search.ByHand);
    }

    [Fact]
    public void A_magic_base_is_found_inside_its_name_and_the_item_level_goes_in()
    {
        var search = Build(Practice("magic-crossbow-normal")); // "Merciless Expert Bombard Crossbow", item level 82

        Assert.Contains("\"type\":\"Bombard Crossbow\"", search.Json);
        Assert.Contains("\"rarity\":{\"option\":\"magic\"}", search.Json);
        Assert.Contains("\"ilvl\":{\"min\":82}", search.Json);
    }

    [Fact]
    public void Buy_now_only_searches_instant_buyout_and_unticking_it_adds_in_person_listings()
    {
        var s = Practice("rare-crossbow-advanced");
        Assert.True(s.W.BuyNowOnly);
        Assert.Contains("\"status\":{\"option\":\"securable\"}", Build(s).Json);

        s.W.BuyNowOnly = false;
        Assert.Contains("\"status\":{\"option\":\"available\"}", Build(s).Json);
    }

    [Fact]
    public void Stats_the_link_cannot_carry_stay_out_and_are_listed_by_hand()
    {
        // Two "# to Spirit" ids share the text, so SSE can't tell which one this is.
        var armour = Typed("Item Class: Body Armours\nRarity: Rare\nDoom Shell\nLeather Vest\n--------\nItem Level: 80\n--------\n+30 to Spirit\n+50 to maximum Life");
        var spirit = armour.W.Rows.Single(r => r.Template == "# to Spirit");
        Assert.False(spirit.Supported);
        Assert.True(armour.W.Rows.Single(r => r.Template == "# to maximum Life").Supported);
        foreach (var r in armour.W.Rows)
        {
            r.Selected = true;
        }

        var made = new WorksheetRow { Id = "Explicit:# made-up stat", Label = "12 made-up stat", Kind = RowKind.Explicit, Template = "# made-up stat", Actual = 12, Selected = true, Min = 10 };
        var other = new WorksheetRow { Id = "Other:# odd line", Label = "3 odd line", Kind = RowKind.Other, Template = "# odd line", Actual = 3, Selected = true, Min = 3 };
        armour.W.Rows.Add(made);
        armour.W.Rows.Add(other);

        var search = Build(armour);

        Assert.Equal([spirit, made, other], search.Unmapped);
        Assert.Contains("+30 to Spirit", search.ByHand);
        Assert.Contains("12 made-up stat", search.ByHand);
        Assert.DoesNotContain("Spirit", search.Json);
        Assert.Contains("\"explicit.stat_3299347043\"", search.Json); // the life still goes in
        Assert.Contains(armour.W.Warnings(), x => x.Contains("+30 to Spirit", StringComparison.Ordinal) && x.Contains("set it by hand", StringComparison.Ordinal));
    }

    [Fact]
    public void Optional_stats_become_a_count_group_and_lower_is_better_rows_search_a_maximum()
    {
        var s = Practice("rare-crossbow-advanced");
        s.W.Row($"{RowKind.Explicit}:{ComparisonWorksheet.LocalAttackSpeed}")!.Group = FilterGroup.Optional;
        Tick(s.W, RowKind.Explicit, "#% increased Critical Hit Chance").Group = FilterGroup.Optional;
        s.W.Rows.Add(new WorksheetRow
        {
            Id = "Explicit:#% increased Attribute Requirements", Label = "10% increased Attribute Requirements", Kind = RowKind.Explicit,
            Template = "#% increased Attribute Requirements", Actual = 10, Direction = StatDirection.LowerIsBetter,
            Selected = true, Match = Trade.MatchType.Maximum, Max = 11.0m,
        });

        var json = Build(s).Json;

        Assert.Contains("""{"type":"count","value":{"min":1},"filters":[{"id":"explicit.stat_210067635","value":{"min":15}},{"id":"explicit.stat_587431675","value":{"min":27}}]}""", json);
        Assert.Contains("""{"type":"and","filters":[{"id":"explicit.stat_3639275092","value":{"max":11}}]}""", json);
    }

    [Fact]
    public void Physical_dps_cannot_be_optional_on_the_site_so_it_is_set_by_hand()
    {
        var s = Practice("rare-crossbow-advanced");
        s.W.Row(ComparisonWorksheet.PhysicalDpsId)!.Group = FilterGroup.Optional;

        var search = Build(s);

        Assert.DoesNotContain("pdps", search.Json);
        Assert.Contains("Physical DPS", search.ByHand);
    }

    [Fact]
    public void Item_level_and_the_listing_currency_go_in_when_chosen()
    {
        var s = Practice("rare-crossbow-advanced");
        s.W.MinItemLevel = s.W.SuggestedItemLevel;
        s.W.ListingDenomination = "exalted";

        var json = Build(s).Json;

        Assert.Contains("\"ilvl\":{\"min\":80}", json);
        Assert.Contains("\"trade_filters\":{\"filters\":{\"price\":{\"option\":\"exalted\"}}}", json);

        s.W.ListingDenomination = "made-up-orb";
        Assert.DoesNotContain("trade_filters", Build(s).Json);
    }

    [Fact]
    public void Without_trade_data_the_link_keeps_the_base_and_lists_every_stat_by_hand()
    {
        var s = Practice("rare-crossbow-advanced");

        var search = TradeQuery.Build(s.W, s.Item, League, TradeData.Empty);

        Assert.Contains("\"type\":\"Expert Bombard Crossbow\"", search.Json); // nothing to check it against
        Assert.Contains("\"pdps\":{\"min\":319}", search.Json);
        Assert.Equal(["17% increased Attack Speed"], search.ByHand);
    }

    [Fact]
    public void The_url_carries_the_league_and_the_query_and_decodes_back_to_it()
    {
        var s = Practice("rare-crossbow-advanced");
        Tick(s.W, RowKind.Explicit, ComparisonWorksheet.LocalFlatPhys);
        var search = TradeQuery.Build(s.W, s.Item, "Runes of Aldur (PL1234)", Data.Value);

        var url = search.Url;
        const string prefix = "https://www.pathofexile.com/trade2/search/Runes%20of%20Aldur%20%28PL1234%29?q=";
        Assert.StartsWith(prefix, url);
        Assert.DoesNotMatch(new Regex("[\\s\"{}#\\[\\]]"), url);

        var q = url[prefix.Length..];
        var decoded = Uri.UnescapeDataString(q);
        Assert.Equal(search.Json, decoded);
        using var doc = JsonDocument.Parse(decoded);
        Assert.Equal("securable", doc.RootElement.GetProperty("query").GetProperty("status").GetProperty("option").GetString());
        Assert.Equal(319, doc.RootElement.GetProperty("query").GetProperty("filters").GetProperty("equipment_filters").GetProperty("filters").GetProperty("pdps").GetProperty("min").GetDecimal());

        var parsed = new Uri(url);
        Assert.Equal("www.pathofexile.com", parsed.Host);
        Assert.Equal("Runes of Aldur (PL1234)", Uri.UnescapeDataString(parsed.AbsolutePath["/trade2/search/".Length..]));
    }

    [Fact]
    public void The_same_worksheet_always_gives_the_same_link()
    {
        Assert.Equal(Build(Practice("rare-crossbow-advanced")).Url, Build(Practice("rare-crossbow-advanced")).Url);
    }

    [Fact]
    public void A_broken_data_file_is_reported_not_half_read()
    {
        Assert.Throws<FormatException>(() => TradeData.Parse("{\"about\": \"no stats here\"}"));
        Assert.Throws<FormatException>(() => TradeData.Parse("not json"));
    }
}
