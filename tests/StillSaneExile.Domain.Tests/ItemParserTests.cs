using StillSaneExile.Domain.Items;
using StillSaneExile.Domain.Reading;

namespace StillSaneExile.Domain.Tests;

public class ItemParserTests
{
    [Fact]
    public void Advanced_copy_keeps_affix_groups_tiers_and_game_ranges()
    {
        var item = Fixtures.Item("rare-crossbow-advanced");

        Assert.Equal(CopyKind.Advanced, item.Copy);
        Assert.Equal("Crossbows", item.ItemClass);
        Assert.Equal(ItemRarity.Rare, item.Rarity);
        Assert.Equal("Doom Thirst", item.Name);
        Assert.Equal("Siege Crossbow", item.BaseType);
        Assert.Equal(82, item.ItemLevel);
        Assert.Equal(79, item.Requirements.Level);
        Assert.Equal(89, item.Requirements.Strength);
        Assert.Equal(20, item.Quality);
        Assert.Equal("S S", item.Sockets);

        var merciless = item.Mods.Single(m => m.AffixName == "Merciless");
        Assert.Equal(AffixSide.Prefix, merciless.Side);
        Assert.Equal(1, merciless.Tier);
        Assert.Contains("Physical", merciless.Tags);
        var c = Assert.Single(merciless.Lines.Single().Components);
        Assert.Equal(175m, c.Value);
        Assert.Equal(170m, c.Min);
        Assert.Equal(179m, c.Max);
        Assert.Equal(RangeProvenance.AdvancedCopy, c.RangeSource);
    }

    [Fact]
    public void Adds_x_to_y_keeps_both_components_with_their_own_ranges()
    {
        var item = Fixtures.Item("rare-crossbow-advanced");
        var line = item.Mods.Single(m => m.AffixName == "Tempered").Lines.Single();

        Assert.Equal("Adds # to # Physical Damage", line.Template);
        Assert.Equal([33m, 61m], line.Components.Select(x => x.Value));
        Assert.Equal([29m, 50m], line.Components.Select(x => x.Min!.Value));
        Assert.Equal([44m, 75m], line.Components.Select(x => x.Max!.Value));
    }

    [Fact]
    public void Implicit_rune_and_explicit_stay_separate_sources()
    {
        var item = Fixtures.Item("rare-crossbow-advanced");

        Assert.Single(item.ModsFrom(ModSource.Implicit));
        var rune = Assert.Single(item.ModsFrom(ModSource.Rune)); // two Greater Desert Runes, as a weapon has them
        Assert.Equal("Adds # to # Fire Damage", rune.Lines.Single().Template);
        Assert.Equal([26m, 32m], rune.Lines.Single().Components.Select(c => c.Value));
        Assert.Equal("26-32", item.Properties["Fire Damage"]);
        Assert.Equal(5, item.ModsFrom(ModSource.Explicit).Count());
    }

    [Fact]
    public void Normal_copy_reads_weapon_properties_and_derives_physical_dps()
    {
        var item = Fixtures.Item("rare-crossbow-normal-hybrid");

        Assert.Equal(CopyKind.Normal, item.Copy);
        Assert.Equal(85m, item.Weapon!.PhysicalMin);
        Assert.Equal(336m, item.Weapon.PhysicalMax);
        Assert.Equal(1.95m, item.Weapon.AttacksPerSecond);
        Assert.Equal(8.47m, item.Weapon.CriticalHitChance);
        Assert.Equal(0.64m, item.Weapon.ReloadTime);
        // (85 + 336) / 2 × 1.95 — displayed numbers already include local mods and quality.
        Assert.Equal(410.475m, WeaponMath.PhysicalDps(item.Weapon));
    }

    [Fact]
    public void Item_level_and_required_level_are_distinct()
    {
        var item = Fixtures.Item("rare-crossbow-normal-hybrid");
        Assert.Equal(81, item.ItemLevel);
        Assert.Equal(79, item.Requirements.Level);
    }

    [Fact]
    public void Unique_flavour_text_is_not_a_modifier()
    {
        var item = Fixtures.Item("unique-ring");

        Assert.Equal(ItemRarity.Unique, item.Rarity);
        Assert.Equal("Sanity's Last Loop", item.Name);
        Assert.Equal(2, item.FlavourText.Count);
        Assert.Equal(2, item.ModsFrom(ModSource.Unique).Count());
        Assert.Single(item.ModsFrom(ModSource.Implicit));
    }

    [Fact]
    public void Currency_reads_stack_size()
    {
        var item = Fixtures.Item("currency-exalted");
        Assert.Equal(ItemRarity.Currency, item.Rarity);
        Assert.Equal("Exalted Orb", item.BaseType);
        Assert.Equal(7, item.StackSize);
        Assert.Equal(20, item.MaxStackSize);
        Assert.Empty(item.Mods);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello from a chat window")]
    [InlineData("Objet de classe: Arbalètes")]
    public void Unrelated_clipboard_text_is_rejected(string text)
    {
        var result = ItemTextParser.Parse(text, Fixtures.DemoNow, "x");
        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }

    // Hand-written in the shape of an item copy, not copied from a game client: only the structure
    // (a translated "label: value" first line and separator lines) matters to these tests.
    [Theory]
    [InlineData("Classe d'objet: Arbalètes\nRareté: Rare\nMarque de tempête\nArbalète de bombardement\n--------\nDégâts physiques: 98-210 (augmented)\n--------\nNiveau de l'objet: 81\n--------\n+212 à la Précision")]
    [InlineData("Gegenstandsklasse: Armbrüste\nSeltenheit: Selten\nSturmmal\nBombardierarmbrust\n--------\nPhysischer Schaden: 98-210\n--------\nGegenstandsstufe: 81")]
    [InlineData("Класс предмета: Арбалеты\nРедкость: Редкий\nМетка бури\nБомбардирский арбалет\n--------\nФизический урон: 98-210\n--------\nУровень предмета: 81")]
    [InlineData("아이템 종류: 쇠뇌\n아이템 희귀도: 희귀\n폭풍의 표식\n폭격 쇠뇌\n--------\n물리 피해：98-210\n--------\n아이템 레벨：81")]
    public void Non_english_item_copy_says_unsupported_language(string text)
    {
        var result = ItemTextParser.Parse(text, Fixtures.DemoNow, "x");

        Assert.False(result.Ok);
        Assert.Equal(ParseFailure.UnsupportedLanguage, result.Failure);
        Assert.Equal(ItemTextParser.UnsupportedLanguageMessage, result.Error);
        Assert.Contains("English", result.Error);
    }

    [Theory]
    [InlineData("Objet de classe: Arbalètes")]
    [InlineData("hello from a chat window\n--------\nsee you\n--------\nbye")]
    [InlineData("this first line is long enough to be a sentence, not a label: really\n--------\nx\n--------")]
    [InlineData("Party: anyone for maps?\nok")]
    public void Other_text_is_not_mistaken_for_a_foreign_item(string text)
    {
        var result = ItemTextParser.Parse(text, Fixtures.DemoNow, "x");

        Assert.False(result.Ok);
        Assert.Equal(ParseFailure.NotAnItem, result.Failure);
    }

    [Fact]
    public void English_copies_are_unaffected_by_the_language_check()
    {
        var result = ItemTextParser.Parse(Fixtures.Text("items/rare-crossbow-normal-hybrid.txt"), Fixtures.DemoNow, "x");
        Assert.True(result.Ok);
        Assert.Equal(ParseFailure.None, result.Failure);
    }

    [Fact]
    public void States_are_read()
    {
        var text = "Item Class: Rings\nRarity: Rare\nGloom Loop\nGold Ring\n--------\nItem Level: 70\n--------\n+30 to Intelligence\n--------\nCorrupted";
        var item = ItemTextParser.Parse(text, Fixtures.DemoNow, "x").Item!;
        Assert.True(item.States.HasFlag(ItemStates.Corrupted));
    }

    [Fact]
    public void Twice_corrupted_is_corrupted()
    {
        var text = "Item Class: Rings\nRarity: Rare\nGloom Loop\nGold Ring\n--------\nItem Level: 70\n--------\n+30 to Intelligence\n--------\nTwice Corrupted";
        var item = ItemTextParser.Parse(text, Fixtures.DemoNow, "x").Item!;

        Assert.True(item.States.HasFlag(ItemStates.Corrupted));
        Assert.Single(item.Mods); // the state line is not read as a stat
    }

    [Fact]
    public void An_advanced_copy_marks_the_enchant_corruption_added()
    {
        var item = Fixtures.Item("rare-helmet-corrupted-advanced");

        Assert.True(item.States.HasFlag(ItemStates.Corrupted));
        var corruption = Assert.Single(item.Mods, m => m.FromCorruption);
        Assert.Equal(ModSource.Enchant, corruption.Source);
        Assert.Equal(["Attack"], corruption.Tags);
        var line = corruption.Lines.Single();
        Assert.Equal("# to Accuracy Rating", line.Template);
        Assert.Equal((74m, 50m, 100m), (line.Components[0].Value, line.Components[0].Min!.Value, line.Components[0].Max!.Value));
        Assert.Equal(4, item.ModsFrom(ModSource.Explicit).Count());
        Assert.Empty(item.UnparsedLines);
    }

    [Fact]
    public void An_anointment_is_an_enchant_but_not_from_corruption()
    {
        var text = "Item Class: Amulets\nRarity: Magic\nSteady Gold Amulet\n--------\nItem Level: 71\n--------\n{ Enhancement }\nAllocates Hallowed — Unscalable Value\n--------\n"
            + "{ Implicit Modifier }\n12(12-20)% increased Rarity of Items found\n--------\n{ Prefix Modifier \"Steady\" (Tier: 3) — Attack }\n+219(168-236) to Accuracy Rating\n--------\nCorrupted";
        var item = ItemTextParser.Parse(text, Fixtures.DemoNow, "x").Item!;

        var anoint = Assert.Single(item.ModsFrom(ModSource.Enchant));
        Assert.False(anoint.FromCorruption);
        Assert.DoesNotContain(item.Mods, m => m.FromCorruption || m.Source == ModSource.Unknown);
    }

    [Fact]
    public void A_normal_copy_cannot_tell_a_corruption_enchant_from_an_anointment()
    {
        // Both end in "(enchant)" in a normal copy, so nothing is marked as added by corruption.
        var text = "Item Class: Helmets\nRarity: Rare\nDusk Vigil\nGladiatorial Helm\n--------\nItem Level: 81\n--------\n+74 to Accuracy Rating (enchant)\n--------\n+79 to maximum Life\n--------\nCorrupted";
        var item = ItemTextParser.Parse(text, Fixtures.DemoNow, "x").Item!;

        var enchant = Assert.Single(item.ModsFrom(ModSource.Enchant));
        Assert.Equal("# to Accuracy Rating", enchant.Lines.Single().Template);
        Assert.DoesNotContain(item.Mods, m => m.FromCorruption);
    }

    [Theory]
    [InlineData("+18% to Fire Resistance", "#% to Fire Resistance", 18)]
    [InlineData("-5% to Fire Resistance", "#% to Fire Resistance", -5)]
    [InlineData("+165 to Accuracy Rating", "# to Accuracy Rating", 165)]
    [InlineData("1.5% of Damage Leeched as Life", "#% of Damage Leeched as Life", 1.5)]
    [InlineData("+18% to Fire Resistance (rune)", "#% to Fire Resistance", 18)]
    public void Templates_drop_signs_and_tags(string line, string template, double value)
    {
        var parsed = StatText.Parse(line, advanced: false);
        Assert.Equal(template, parsed.Template);
        Assert.Equal((decimal)value, parsed.Components.Single().Value);
    }

    [Fact]
    public void Negative_advanced_range_parses()
    {
        var parsed = StatText.Parse("-12(-15--10)% to Cold Resistance", advanced: true);
        var c = parsed.Components.Single();
        Assert.Equal(-12m, c.Value);
        Assert.Equal(-15m, c.Min);
        Assert.Equal(-10m, c.Max);
    }

    [Fact]
    public void Advanced_number_without_range_is_fixed_by_the_game()
    {
        var parsed = StatText.Parse("+1 to Level of all Projectile Skills", advanced: true);
        Assert.Equal(RangeProvenance.FixedByGame, parsed.Components.Single().RangeSource);
    }
}
