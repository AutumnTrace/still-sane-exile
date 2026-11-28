using System.IO.Compression;
using System.Text;
using StillSaneExile.Domain.Guide;

namespace StillSaneExile.Domain.Tests;

/// <summary>Path of Building codes: the XML, zlib-compressed, then base64 with '-' and '_' (Path of
/// Building's ImportTab.lua). Every build here is synthetic, written for SSE in the real shape.</summary>
public class PobCodeTests
{
    internal static string Encode(string xml, bool urlSafe = true, bool gzip = false)
    {
        using var output = new MemoryStream();
        using (Stream pack = gzip ? new GZipStream(output, CompressionLevel.SmallestSize) : new ZLibStream(output, CompressionLevel.SmallestSize))
        {
            var bytes = Encoding.UTF8.GetBytes(xml);
            pack.Write(bytes, 0, bytes.Length);
        }

        var b64 = Convert.ToBase64String(output.ToArray());
        return urlSafe ? b64.Replace('+', '-').Replace('/', '_') : b64;
    }

    internal static PobBuild Read(string code)
    {
        Assert.True(PobCode.TryRead(code, out var build, out var error), error);
        return build!;
    }

    internal static PobBuild Practice() => Read(Fixtures.Text("builds/practice-witch.pob.txt"));

    private static string Error(string? text)
    {
        Assert.False(PobCode.TryRead(text, out var build, out var error));
        Assert.Null(build);
        return error!;
    }

    private static string Minimal(string inner = "", string root = "PathOfBuilding2") =>
        $"""<?xml version="1.0" encoding="UTF-8"?><{root}><Build level="12" className="Ranger" ascendClassName="None" targetVersion="0_5"/>{inner}</{root}>""";

    [Fact]
    public void The_practice_code_reads_class_level_skill_sets_trees_items_notes_and_stats()
    {
        var build = Practice();

        Assert.Equal("Witch", build.ClassName);
        Assert.Equal("Blood Mage", build.Ascendancy);
        Assert.Equal("Blood Mage (Witch)", build.Title);
        Assert.Equal(38, build.Level);
        Assert.Equal("0_5", build.TargetVersion);

        Assert.Equal(new[] { "Act 1", "Act 2", "Endgame" }, build.SkillSets.Select(s => s.Title));
        Assert.Equal(1, build.ActiveSkillSet);
        var opener = build.SkillSets[0].Groups[0];
        Assert.Equal("Opener", opener.Label);
        Assert.Equal("Entangle", opener.MainSkill?.Name);
        Assert.Equal(["Zenith I"], opener.Supports.Select(g => g.Name));
        Assert.Equal(3, opener.Gems[0].Level);
        Assert.Equal("Practice note: cast it first.", opener.Gems[0].Note);
        Assert.False(build.SkillSets[1].Groups.Single(g => g.Label == "Switched off").Enabled);

        Assert.Equal(new[] { "Act 1", "Act 2" }, build.Trees.Select(t => t.Title));
        var tree = build.Tree!;
        Assert.Equal("Act 2", tree.Title);
        Assert.Equal("0_5", tree.TreeVersion);
        Assert.Equal("Witch2", tree.AscendancyId);
        Assert.Equal(27, tree.Nodes.Count);
        Assert.Equal(1, tree.WeaponSets[44871]);
        Assert.Equal("Practice note: take it last.", tree.NodeNotes[49363]);

        var items = build.Items!;
        Assert.Equal("Campaign", items.Title);
        Assert.Equal(new[] { "Weapon 1", "Ring 1" }, items.Slots.Select(s => s.Slot)); // the empty helmet is left out
        Assert.Equal(new PobItem(1, "RARE", "Practice Bane", "Withered Wand"), items.Slots[0].Item);
        Assert.Equal(new PobItem(2, "MAGIC", "Practice Ring of the Test", null), items.Slots[1].Item);

        Assert.StartsWith("Practice build written for SSE's tests.", build.Notes, StringComparison.Ordinal); // colour codes gone
        Assert.DoesNotContain('^', build.Notes);
        Assert.Contains(new PobStat("Life", "812"), build.Stats);
    }

    [Fact]
    public void The_code_fixture_is_the_xml_fixture()
    {
        var fromXml = Read(Encode(Fixtures.Text("builds/practice-witch.xml")));
        var fromCode = Practice();

        Assert.Equal(fromXml.Title, fromCode.Title);
        Assert.Equal(fromXml.Notes, fromCode.Notes);
        Assert.Equal(fromXml.Tree!.Nodes, fromCode.Tree!.Nodes);
        Assert.Equal(
            fromXml.SkillSets.SelectMany(s => s.Groups).SelectMany(g => g.Gems).Select(g => g.Name),
            fromCode.SkillSets.SelectMany(s => s.Groups).SelectMany(g => g.Gems).Select(g => g.Name));
    }

    [Fact]
    public void Supports_are_told_apart_by_the_ids_path_of_building_writes()
    {
        var gems = Practice().SkillSets.SelectMany(s => s.Groups).SelectMany(g => g.Gems).DistinctBy(g => g.Name).ToDictionary(g => g.Name);

        Assert.True(gems["Zenith I"].IsSupport);
        Assert.True(gems["Controlled Destruction"].IsSupport);
        Assert.False(gems["Entangle"].IsSupport);
        Assert.False(gems["Herald of Ash"].IsSupport);
        Assert.True(new PobGem("Old support", "SupportOldPlayer", null, null, null, true, null).IsSupport);
    }

    [Fact]
    public void Plain_and_url_safe_base64_with_or_without_padding_line_breaks_or_gzip_all_read()
    {
        var xml = Fixtures.Text("builds/practice-witch.xml");
        var code = Encode(xml);
        string[] codes =
        [
            code,
            Encode(xml, urlSafe: false),
            code.TrimEnd('='),
            string.Join("\r\n", code.Chunk(76).Select(c => new string(c))),
            "  " + code + "\n",
            Encode(xml, gzip: true),
        ];

        foreach (var c in codes)
        {
            Assert.Equal("Blood Mage (Witch)", Read(c).Title);
        }
    }

    [Fact]
    public void A_code_cut_short_says_so()
    {
        var code = Fixtures.Text("builds/practice-witch.pob.txt").Trim();

        Assert.Equal(PobCode.Damaged, Error(code[..(code.Length / 2)]));
        Assert.Equal(PobCode.Damaged, Error(code[..(code.Length - 40)]));
    }

    [Theory]
    [InlineData("hello there, exile")]
    [InlineData("Item Class: Wands\nRarity: Rare\nPractice Bane")]
    [InlineData("QUJDREVGR0hJSktMTU5PUA")] // base64, but not compressed
    [InlineData("eNo")]
    public void Text_that_is_not_a_code_says_what_a_code_looks_like(string text) => Assert.Equal(PobCode.NotACode, Error(text));

    [Fact]
    public void Nothing_pasted_asks_for_a_code()
    {
        Assert.Equal(PobCode.Empty, Error(null));
        Assert.Equal(PobCode.Empty, Error("   \n"));
    }

    [Theory]
    [InlineData("https://pobb.in/AbCdEf12", "pobb.in")]
    [InlineData("https://poe.ninja/poe2/pob/1a2b3c", "poe.ninja")]
    [InlineData("pobb.in/AbCdEf12", "pobb.in")]
    [InlineData("https://maxroll.gg/poe2/pob/abc", "maxroll.gg")]
    [InlineData("www.pastebin.com/abc", "pastebin.com")]
    public void A_build_site_link_is_refused_and_never_opened(string link, string site)
    {
        var error = Error(link);

        Assert.Contains(site, error, StringComparison.Ordinal);
        Assert.Contains("SSE does not open build sites", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mobalytics_link_points_to_the_pages_own_path_of_building_export()
    {
        var error = Error("https://mobalytics.gg/poe-2/builds/practice");

        Assert.Contains("Mobalytics", error, StringComparison.Ordinal);
        Assert.Contains("Path of Building export", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_path_of_exile_1_build_is_named_as_one()
    {
        Assert.Equal(PobCode.PoE1, Error(Encode(Minimal(root: "PathOfBuilding"))));
    }

    [Fact]
    public void Xml_that_is_not_a_build_is_refused()
    {
        Assert.Equal("That code holds something other than a Path of Building build.", Error(Encode("<Something/>")));
        Assert.Equal("That code has no build in it.", Error(Encode("<PathOfBuilding2><Notes>hi</Notes></PathOfBuilding2>")));
        Assert.Equal(PobCode.Damaged, Error(Encode("<PathOfBuilding2><Build")));
    }

    [Fact]
    public void A_document_type_is_refused_so_a_code_cannot_pull_in_other_files()
    {
        var xml = """<?xml version="1.0"?><!DOCTYPE b [<!ENTITY x SYSTEM "file:///c:/windows/win.ini">]><PathOfBuilding2><Build className="Witch"/><Notes>&x;</Notes></PathOfBuilding2>""";

        Assert.Equal(PobCode.Damaged, Error(Encode(xml)));
    }

    [Fact]
    public void A_code_that_unpacks_to_far_more_than_a_build_is_not_read()
    {
        var huge = Minimal($"<Notes>{new string(' ', PobCode.MaxXmlBytes)}</Notes>");

        Assert.Contains("far more than one build", Error(Encode(huge)), StringComparison.Ordinal);
    }

    [Fact]
    public void The_older_format_without_skill_sets_reads_as_one_set()
    {
        var build = Read(Encode(Minimal("""
            <Skills><Skill enabled="true"><Gem nameSpec="Lightning Arrow" skillId="LightningArrowPlayer"/></Skill></Skills>
            <Spec treeVersion="0_4" nodes="1,2,x,3"/>
            """)));

        Assert.Null(build.Ascendancy); // "None"
        var set = Assert.Single(build.SkillSets);
        Assert.Null(set.Title);
        Assert.Equal("Lightning Arrow", set.Groups.Single().MainSkill?.Name);
        Assert.Equal(new[] { 1, 2, 3 }, build.Tree!.Nodes);
        Assert.Empty(build.ItemSets);
    }

    [Fact]
    public void Groups_path_of_building_removed_are_left_out_and_item_skills_are_marked()
    {
        var build = Read(Encode(Minimal("""
            <Skills activeSkillSet="1"><SkillSet id="1">
              <Skill enabled="true" removed="true"><Gem nameSpec="Gone" skillId="GonePlayer"/></Skill>
              <Skill enabled="true" source="Item:3:Practice Bane"><Gem nameSpec="Granted Skill" skillId="GrantedPlayer"/></Skill>
            </SkillSet></Skills>
            """)));

        var group = Assert.Single(build.SkillSets[0].Groups);
        Assert.True(group.FromItem);
    }
}
