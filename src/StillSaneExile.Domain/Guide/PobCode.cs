using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace StillSaneExile.Domain.Guide;

/// <summary>Reads a Path of Building code: the text Path of Building's "Generate" button and guide
/// sites' Path of Building exports give. The code is the build's XML, zlib-compressed, then base64 with
/// <c>-</c> and <c>_</c> in place of <c>+</c> and <c>/</c> (Path of Building's
/// <c>ImportTab.lua</c> and SimpleGraphic's <c>Deflate</c>). Everything happens on this PC: a link to a
/// build site is refused rather than opened, because SSE never fetches builds.</summary>
public static partial class PobCode
{
    /// <summary>Real codes run to tens of kilobytes; anything near this is not one build.</summary>
    public const int MaxCodeLength = 4_000_000;

    /// <summary>The unpacked build may be at most this big, so a crafted code cannot fill memory.</summary>
    public const int MaxXmlBytes = 32 * 1024 * 1024;

    public const string Empty = "Paste a Path of Building code first.";

    public const string NotACode = "That is not a Path of Building code. A code is one long run of letters and digits: copy it again from Path of Building (Import/Export, Generate) or the guide's Path of Building export.";

    public const string Damaged = "This code is cut short or damaged. Copy all of it again and paste it here.";

    public const string PoE1 = "That is a Path of Exile 1 build. SSE reads Path of Building codes for Path of Exile 2.";

    private static readonly string[] KnownSites = ["pobb.in", "poe.ninja", "poe2.ninja", "maxroll.gg", "poe2db.tw", "pastebin.com", "rentry.co", "mobalytics.gg"];

    public static bool TryRead(string? text, out PobBuild? build, out string? error)
    {
        build = null;
        var s = (text ?? "").Trim();
        if (s.Length == 0)
        {
            error = Empty;
            return false;
        }

        if (LinkProblem(s) is { } link)
        {
            error = link;
            return false;
        }

        if (s.Length > MaxCodeLength)
        {
            error = "That is far too long to be one build code.";
            return false;
        }

        if (!TryUnpack(s, out var xml, out error))
        {
            return false;
        }

        return TryParse(xml!, out build, out error);
    }

    /// <summary>A pasted link gets told what to paste instead. Nothing is opened.</summary>
    private static string? LinkProblem(string s)
    {
        var looksLikeLink = s.Contains("://", StringComparison.Ordinal) || s.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            || KnownSites.Any(site => s.StartsWith(site + "/", StringComparison.OrdinalIgnoreCase));
        if (!looksLikeLink)
        {
            return null;
        }

        var host = Uri.TryCreate(s.Contains("://", StringComparison.Ordinal) ? s : "https://" + s, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : "";
        if (host.StartsWith("www.", StringComparison.Ordinal))
        {
            host = host[4..];
        }

        var site = host.Length > 0 ? host : "a website";
        return host is "mobalytics.gg" || host.EndsWith(".mobalytics.gg", StringComparison.Ordinal)
            ? "That is a Mobalytics link. SSE does not open guide pages: use the build's Path of Building export on the page, then paste that code here."
            : $"That is a link to {site}. SSE does not open build sites: copy the Path of Building code itself and paste it here.";
    }

    /// <summary>URL-safe or plain base64, with or without padding and line breaks, then zlib (or gzip,
    /// which Path of Building's Inflate also accepts).</summary>
    internal static bool TryUnpack(string code, out byte[]? xml, out string? error)
    {
        xml = null;
        var b64 = WhiteSpace().Replace(code, "").Replace('-', '+').Replace('_', '/').TrimEnd('=');
        if (b64.Length < 8 || !Base64().IsMatch(b64))
        {
            error = NotACode;
            return false;
        }

        // A code cut off mid-character leaves one letter that holds no whole byte: drop it, and let the
        // unpacking say the code is cut short.
        if (b64.Length % 4 == 1)
        {
            b64 = b64[..^1];
        }

        b64 += new string('=', (4 - (b64.Length % 4)) % 4);
        byte[] packed;
        try
        {
            packed = Convert.FromBase64String(b64);
        }
        catch (FormatException)
        {
            error = NotACode;
            return false;
        }

        var gzip = packed.Length > 2 && packed[0] == 0x1f && packed[1] == 0x8b;
        var zlib = packed.Length > 2 && (packed[0] & 0x0f) == 8 && ((packed[0] << 8) | packed[1]) % 31 == 0;
        if (!gzip && !zlib)
        {
            error = NotACode;
            return false;
        }

        try
        {
            using var input = new MemoryStream(packed);
            using Stream inflate = gzip ? new GZipStream(input, CompressionMode.Decompress) : new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = inflate.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > MaxXmlBytes)
                {
                    error = "That build unpacks to far more than one build. SSE did not read it.";
                    return false;
                }

                output.Write(buffer, 0, read);
            }

            xml = output.ToArray();
        }
        catch (InvalidDataException)
        {
            error = Damaged;
            return false;
        }

        if (xml.Length == 0)
        {
            error = Damaged;
            return false;
        }

        error = null;
        return true;
    }

    internal static bool TryParse(byte[] xml, out PobBuild? build, out string? error)
    {
        build = null;
        XDocument doc;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                CheckCharacters = false,
                MaxCharactersInDocument = MaxXmlBytes,
                IgnoreComments = true,
            };
            using var stream = new MemoryStream(xml);
            using var reader = XmlReader.Create(stream, settings);
            doc = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            error = Damaged;
            return false;
        }

        var root = doc.Root!;
        if (root.Name.LocalName == "PathOfBuilding")
        {
            error = PoE1;
            return false;
        }

        if (root.Name.LocalName != "PathOfBuilding2")
        {
            error = "That code holds something other than a Path of Building build.";
            return false;
        }

        if (root.Element("Build") is not { } b)
        {
            error = "That code has no build in it.";
            return false;
        }

        var (sets, activeSet) = ReadSkills(root.Element("Skills"));
        var (trees, activeTree) = ReadTrees(root);
        var (itemSets, activeItems) = ReadItems(root.Element("Items"));
        build = new PobBuild
        {
            ClassName = Attr(b, "className") ?? "",
            Ascendancy = Attr(b, "ascendClassName") is { } a && a != "None" ? a : null,
            Level = Math.Clamp(Int(b, "level") ?? 1, 1, 100),
            TargetVersion = Attr(b, "targetVersion"),
            SkillSets = sets,
            ActiveSkillSet = activeSet,
            Trees = trees,
            ActiveTree = activeTree,
            ItemSets = itemSets,
            ActiveItemSet = activeItems,
            Notes = CleanNotes(root.Element("Notes")?.Value),
            Stats = b.Elements("PlayerStat")
                .Select(s => new PobStat(Attr(s, "stat") ?? "", Attr(s, "value") ?? ""))
                .Where(s => s.Name.Length > 0 && s.Value.Length > 0)
                .ToList(),
        };
        error = null;
        return true;
    }

    private static (IReadOnlyList<PobSkillSet> Sets, int Active) ReadSkills(XElement? skills)
    {
        if (skills is null)
        {
            return ([], 0);
        }

        var sets = new List<PobSkillSet>();
        var loose = skills.Elements("Skill").ToList();
        if (loose.Count > 0)
        {
            // The format before skill sets: groups straight under <Skills>, one set.
            sets.Add(new PobSkillSet(1, null, loose.Select(ReadGroup).OfType<PobSkillGroup>().ToList()));
        }

        foreach (var set in skills.Elements("SkillSet"))
        {
            sets.Add(new PobSkillSet(Int(set, "id") ?? sets.Count + 1, Blank(Attr(set, "title")), set.Elements("Skill").Select(ReadGroup).OfType<PobSkillGroup>().ToList()));
        }

        var activeId = Int(skills, "activeSkillSet") ?? 1;
        var active = sets.FindIndex(s => s.Id == activeId);
        return (sets, Math.Max(active, 0));
    }

    private static PobSkillGroup? ReadGroup(XElement skill)
    {
        // Path of Building keeps groups it removed in its own bookkeeping; they are not part of the build.
        if (Attr(skill, "removed") == "true")
        {
            return null;
        }

        var gems = skill.Elements("Gem").Select(g => new PobGem(
            Name: (Attr(g, "nameSpec") ?? "").Trim(),
            SkillId: Attr(g, "skillId"),
            GemId: Attr(g, "gemId"),
            Level: Int(g, "level"),
            Quality: Int(g, "quality"),
            Enabled: Attr(g, "enabled") != "false",
            Note: Blank(Attr(g, "note")))).ToList();
        var enabled = Attr(skill, "enabled") is { } e ? e == "true" : Attr(skill, "active") != "false";
        return new PobSkillGroup(Blank(Attr(skill, "label")), enabled, Blank(Attr(skill, "source")), gems);
    }

    private static (IReadOnlyList<PobTreeSpec> Trees, int Active) ReadTrees(XElement root)
    {
        var tree = root.Element("Tree");
        var specs = (tree?.Elements("Spec") ?? root.Elements("Spec")).Select(ReadSpec).ToList();
        var active = (tree is null ? null : Int(tree, "activeSpec")) ?? 1;
        return (specs, Math.Clamp(active - 1, 0, Math.Max(specs.Count - 1, 0)));
    }

    private static PobTreeSpec ReadSpec(XElement spec)
    {
        var weaponSets = new Dictionary<int, int>();
        foreach (var ws in spec.Elements().Where(e => e.Name.LocalName.StartsWith("WeaponSet", StringComparison.Ordinal)))
        {
            if (int.TryParse(ws.Name.LocalName["WeaponSet".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var set))
            {
                foreach (var id in Ids(Attr(ws, "nodes")))
                {
                    weaponSets[id] = set;
                }
            }
        }

        var notes = new Dictionary<int, string>();
        foreach (var note in spec.Element("Notes")?.Elements("Note") ?? [])
        {
            var text = Blank(CleanNotes(note.Value)) ?? Blank(CleanNotes(Attr(note, "text")));
            if (Int(note, "nodeId") is { } id && text is not null)
            {
                notes[id] = text;
            }
        }

        return new PobTreeSpec(
            Blank(Attr(spec, "title")),
            Attr(spec, "treeVersion"),
            Blank(Attr(spec, "ascendancyInternalId")),
            Ids(Attr(spec, "nodes")).Distinct().ToList(),
            weaponSets,
            notes);
    }

    private static (IReadOnlyList<PobItemSet> Sets, int Active) ReadItems(XElement? items)
    {
        if (items is null)
        {
            return ([], 0);
        }

        var byId = new Dictionary<int, PobItem>();
        foreach (var item in items.Elements("Item"))
        {
            if (Int(item, "id") is { } id && ReadItem(id, string.Concat(item.Nodes().OfType<XText>().Select(t => t.Value))) is { } read)
            {
                byId[id] = read;
            }
        }

        var sets = items.Elements("ItemSet").Select(set => new PobItemSet(
            Int(set, "id") ?? 0,
            Blank(Attr(set, "title")),
            set.Elements("Slot")
                .Select(slot => (Name: Attr(slot, "name") ?? "", Id: Int(slot, "itemId") ?? 0))
                .Where(slot => slot.Name.Length > 0 && byId.ContainsKey(slot.Id))
                .Select(slot => new PobSlot(slot.Name, byId[slot.Id]))
                .ToList())).ToList();
        var activeId = Int(items, "activeItemSet") ?? 1;
        return (sets, Math.Max(sets.FindIndex(s => s.Id == activeId), 0));
    }

    /// <summary>The first lines of Path of Building's item text: "Rarity: RARE", the name, then the base
    /// type for rares and uniques.</summary>
    internal static PobItem? ReadItem(int id, string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0)
        {
            return null;
        }

        var rarity = "";
        if (lines[0].StartsWith("Rarity:", StringComparison.OrdinalIgnoreCase))
        {
            rarity = lines[0]["Rarity:".Length..].Trim().ToUpperInvariant();
            lines.RemoveAt(0);
        }

        if (lines.Count == 0)
        {
            return null;
        }

        var hasBase = rarity is "RARE" or "UNIQUE" or "RELIC" && lines.Count > 1 && !lines[1].Contains(':', StringComparison.Ordinal);
        return new PobItem(id, rarity, lines[0], hasBase ? lines[1] : null);
    }

    /// <summary>Takes out Path of Building's colour codes (<c>^7</c>, <c>^xRRGGBB</c>) and trailing spaces.</summary>
    public static string CleanNotes(string? notes)
    {
        var text = ColourCode().Replace(notes ?? "", "").Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return string.Join('\n', text.Split('\n').Select(l => l.TrimEnd())).Trim('\n');
    }

    private static IEnumerable<int> Ids(string? list)
    {
        foreach (var part in (list ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                yield return id;
            }
        }
    }

    private static string? Attr(XElement e, string name) => e.Attribute(name)?.Value;

    private static int? Int(XElement e, string name) =>
        int.TryParse(Attr(e, name), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();

    [GeneratedRegex(@"^[A-Za-z0-9+/]+$")]
    private static partial Regex Base64();

    [GeneratedRegex(@"\^(?:x[0-9A-Fa-f]{6}|[0-9])")]
    private static partial Regex ColourCode();
}
