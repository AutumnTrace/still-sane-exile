using System.Globalization;
using System.Text.RegularExpressions;

namespace StillSaneExile.Domain.Items;

public sealed record ParseResult(ItemSnapshot? Item, string? Error)
{
    public bool Ok => Item is not null;
}

/// <summary>Parses the English text the game puts on the clipboard for a normal (Ctrl+C) or advanced
/// (Ctrl+Alt+C) item copy. Lines it cannot place are kept in <see cref="ItemSnapshot.UnparsedLines"/>,
/// never dropped and never guessed into a modifier.</summary>
public static partial class ItemTextParser
{
    public const string Separator = "--------";

    private static readonly HashSet<string> PropertyKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Quality", "Physical Damage", "Elemental Damage", "Fire Damage", "Cold Damage", "Lightning Damage",
        "Chaos Damage", "Critical Hit Chance", "Attacks per Second", "Reload Time", "Armour", "Evasion Rating",
        "Energy Shield", "Block chance", "Spirit", "Stack Size", "Level", "Item Level", "Sockets", "Requires",
        "Requirements", "Grants Skill", "Charm Slots", "Limited to", "Radius", "Waystone Tier", "Area Level",
        "Cost", "Reservation", "Cast Time", "Experience", "Str", "Dex", "Int", "Note", "Recovers", "Consumes",
        "Currently has", "Lasts", "Charges",
    };

    [GeneratedRegex(@"^(?<k>[A-Za-z][A-Za-z' ]*?):\s*(?<v>.*)$")]
    private static partial Regex PropertyLine();

    [GeneratedRegex(@"^\{\s*(?<kind>[^""}—–]+?)\s*(?:""(?<name>[^""]*)"")?\s*(?:\((?:Tier|Rank):\s*(?<tier>\d+)\))?\s*(?:[—–-]\s*(?<tags>[^}]*?))?\s*\}$")]
    private static partial Regex AdvancedHeader();

    [GeneratedRegex(@"(?<lo>\d+(?:\.\d+)?)\s*-\s*(?<hi>\d+(?:\.\d+)?)")]
    private static partial Regex DamageRange();

    [GeneratedRegex(@"\d")]
    private static partial Regex Digit();

    [GeneratedRegex(@"^\+?(?<q>\d+)%")]
    private static partial Regex QualityValue();

    [GeneratedRegex(@"(?<n>\d+)\s*/\s*(?<m>\d+)")]
    private static partial Regex StackValue();

    public static ParseResult Parse(string? text, DateTimeOffset capturedAt, string captureId)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ParseResult(null, "The clipboard is empty.");
        }

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n')
            .Select(l => l.TrimEnd())
            .ToList();
        while (lines.Count > 0 && lines[0].Length == 0)
        {
            lines.RemoveAt(0);
        }

        if (lines.Count == 0 || !(lines[0].StartsWith("Item Class:", StringComparison.Ordinal) || lines[0].StartsWith("Rarity:", StringComparison.Ordinal)))
        {
            return new ParseResult(null, "The clipboard does not hold an English item copy. Hover an item in game and press Ctrl+C (or Ctrl+Alt+C) first.");
        }

        var sections = SplitSections(lines);
        var b = new Builder { Raw = text, CapturedAt = capturedAt, CaptureId = captureId };
        b.Copy = lines.Any(l => l.TrimStart().StartsWith('{') && AdvancedHeader().IsMatch(l.Trim())) ? CopyKind.Advanced : CopyKind.Normal;

        ParseHeader(sections[0], b);
        if (b.BaseType.Length == 0)
        {
            return new ParseResult(null, "The item text has no name or base line.");
        }

        var explicitSeen = false;
        for (var i = 1; i < sections.Count; i++)
        {
            var section = sections[i];
            if (section.Count == 0)
            {
                continue;
            }

            if (section.Any(l => l.TrimStart().StartsWith('{')))
            {
                ParseAdvancedSection(section, b);
                explicitSeen |= b.Mods.Any(m => m.Source is ModSource.Explicit or ModSource.Unique);
                continue;
            }

            if (section.All(IsStateLine))
            {
                foreach (var l in section)
                {
                    b.States |= StateOf(l);
                }

                continue;
            }

            if (section[0].StartsWith("Note:", StringComparison.Ordinal))
            {
                b.Note = section[0]["Note:".Length..].Trim();
                b.Unparsed.AddRange(section.Skip(1));
                continue;
            }

            if (section[0].Equals("Requirements:", StringComparison.OrdinalIgnoreCase))
            {
                ParseRequirementsBlock(section.Skip(1), b);
                continue;
            }

            if (section.All(IsPropertyLine))
            {
                foreach (var l in section)
                {
                    ParseProperty(l, b);
                }

                continue;
            }

            if (b.Rarity == ItemRarity.Unique && explicitSeen && !section.Any(l => Digit().IsMatch(l)))
            {
                b.Flavour.AddRange(section);
                continue;
            }

            if (b.Rarity is ItemRarity.Currency or ItemRarity.Gem && !section.Any(l => Digit().IsMatch(l)))
            {
                // Usage instructions on currency and gems: keep, but they are not modifiers.
                b.Flavour.AddRange(section);
                continue;
            }

            ParseNormalModSection(section, b);
            explicitSeen |= b.Mods.Any(m => m.Source is ModSource.Explicit or ModSource.Unique);
        }

        return new ParseResult(b.Build(), null);
    }

    private static List<List<string>> SplitSections(List<string> lines)
    {
        var sections = new List<List<string>> { new() };
        foreach (var line in lines)
        {
            if (line.Trim() == Separator)
            {
                sections.Add([]);
                continue;
            }

            if (line.Length > 0)
            {
                sections[^1].Add(line.Trim());
            }
        }

        return sections;
    }

    private static void ParseHeader(List<string> header, Builder b)
    {
        var names = new List<string>();
        foreach (var line in header)
        {
            if (line.StartsWith("Item Class:", StringComparison.Ordinal))
            {
                b.ItemClass = line["Item Class:".Length..].Trim();
            }
            else if (line.StartsWith("Rarity:", StringComparison.Ordinal))
            {
                b.Rarity = line["Rarity:".Length..].Trim() switch
                {
                    "Normal" => ItemRarity.Normal,
                    "Magic" => ItemRarity.Magic,
                    "Rare" => ItemRarity.Rare,
                    "Unique" => ItemRarity.Unique,
                    "Currency" => ItemRarity.Currency,
                    "Gem" => ItemRarity.Gem,
                    _ => ItemRarity.Other,
                };
            }
            else
            {
                names.Add(line);
            }
        }

        if (names.Count >= 2 && b.Rarity is ItemRarity.Rare or ItemRarity.Unique)
        {
            b.Name = names[0];
            b.BaseType = names[1];
        }
        else if (names.Count >= 1)
        {
            // Magic names wrap the base in affix words; resolving the base needs a base catalogue, so the
            // full displayed name stays as the base type rather than a guess.
            b.BaseType = names[0];
        }
    }

    private static bool IsStateLine(string line) => StateOf(line) != ItemStates.None;

    private static ItemStates StateOf(string line) => line.Trim() switch
    {
        "Corrupted" => ItemStates.Corrupted,
        "Unidentified" => ItemStates.Unidentified,
        "Mirrored" => ItemStates.Mirrored,
        "Fractured Item" => ItemStates.Fractured,
        _ => ItemStates.None,
    };

    private static bool IsPropertyLine(string line)
    {
        var m = PropertyLine().Match(line);
        return m.Success && PropertyKeys.Contains(m.Groups["k"].Value.Trim());
    }

    private static void ParseProperty(string line, Builder b)
    {
        var m = PropertyLine().Match(line);
        var key = m.Groups["k"].Value.Trim();
        var (value, _) = StatText.StripTags(m.Groups["v"].Value);
        b.Properties[key] = value;

        switch (key)
        {
            case "Item Level":
                b.ItemLevel = IntOrNull(value);
                break;
            case "Quality":
                var q = QualityValue().Match(value);
                b.Quality = q.Success ? int.Parse(q.Groups["q"].Value, CultureInfo.InvariantCulture) : null;
                break;
            case "Physical Damage":
                var d = DamageRange().Match(value);
                if (d.Success)
                {
                    b.PhysMin = StatText.Dec(d.Groups["lo"].Value);
                    b.PhysMax = StatText.Dec(d.Groups["hi"].Value);
                }

                break;
            case "Attacks per Second":
                b.Aps = DecOrNull(value);
                break;
            case "Critical Hit Chance":
                b.Crit = DecOrNull(value.TrimEnd('%'));
                break;
            case "Reload Time":
                b.Reload = DecOrNull(value);
                break;
            case "Sockets":
                b.Sockets = value;
                break;
            case "Stack Size":
                var s = StackValue().Match(value.Replace(",", "", StringComparison.Ordinal));
                if (s.Success)
                {
                    b.Stack = int.Parse(s.Groups["n"].Value, CultureInfo.InvariantCulture);
                    b.MaxStack = int.Parse(s.Groups["m"].Value, CultureInfo.InvariantCulture);
                }

                break;
            case "Requires":
                ParseRequiresLine(value, b);
                break;
            case "Level":
                b.ReqLevel ??= IntOrNull(value);
                break;
            case "Str":
                b.ReqStr = IntOrNull(value);
                break;
            case "Dex":
                b.ReqDex = IntOrNull(value);
                break;
            case "Int":
                b.ReqInt = IntOrNull(value);
                break;
        }
    }

    private static void ParseRequiresLine(string value, Builder b)
    {
        // "Level 79, 89 Str, 89 Dex" (PoE2 style). Tags such as "(augmented)" were stripped per part.
        foreach (var raw in value.Split(','))
        {
            var (part, _) = StatText.StripTags(raw.Trim());
            if (part.StartsWith("Level", StringComparison.OrdinalIgnoreCase))
            {
                b.ReqLevel = IntOrNull(part["Level".Length..].Trim());
            }
            else if (part.EndsWith("Str", StringComparison.OrdinalIgnoreCase))
            {
                b.ReqStr = IntOrNull(part[..^3].Trim());
            }
            else if (part.EndsWith("Dex", StringComparison.OrdinalIgnoreCase))
            {
                b.ReqDex = IntOrNull(part[..^3].Trim());
            }
            else if (part.EndsWith("Int", StringComparison.OrdinalIgnoreCase))
            {
                b.ReqInt = IntOrNull(part[..^3].Trim());
            }
        }
    }

    private static void ParseRequirementsBlock(IEnumerable<string> lines, Builder b)
    {
        foreach (var l in lines)
        {
            if (IsPropertyLine(l))
            {
                ParseProperty(l, b);
            }
            else
            {
                b.Unparsed.Add(l);
            }
        }
    }

    private static void ParseNormalModSection(List<string> section, Builder b)
    {
        foreach (var line in section)
        {
            if (IsPropertyLine(line))
            {
                ParseProperty(line, b);
                continue;
            }

            var (_, tags) = StatText.StripTags(line);
            var source = SourceFromTags(tags, b.Rarity);
            var parsed = StatText.Parse(line, advanced: false);
            if (parsed.IsReminderText)
            {
                continue;
            }

            if (tags.Contains("fractured"))
            {
                b.States |= ItemStates.Fractured;
            }

            b.Mods.Add(new ModGroup(source, AffixSide.None, null, null, tags, [parsed], FromAdvancedCopy: false));
        }
    }

    private static ModSource SourceFromTags(IReadOnlyList<string> tags, ItemRarity rarity)
    {
        if (tags.Contains("implicit"))
        {
            return ModSource.Implicit;
        }

        if (tags.Contains("rune"))
        {
            return ModSource.Rune;
        }

        if (tags.Contains("enchant"))
        {
            return ModSource.Enchant;
        }

        if (tags.Contains("desecrated"))
        {
            return ModSource.Desecrated;
        }

        if (tags.Contains("fractured"))
        {
            return ModSource.Fractured;
        }

        if (tags.Contains("crafted"))
        {
            return ModSource.Crafted;
        }

        return rarity == ItemRarity.Unique ? ModSource.Unique : ModSource.Explicit;
    }

    private static void ParseAdvancedSection(List<string> section, Builder b)
    {
        HeaderInfo? current = null;
        var lines = new List<ModLine>();

        void Flush()
        {
            if (current is not null)
            {
                b.Mods.Add(new ModGroup(current.Source, current.Side, current.Name, current.Tier, current.Tags, lines.ToList(), FromAdvancedCopy: true));
                if (current.Source == ModSource.Fractured)
                {
                    b.States |= ItemStates.Fractured;
                }
            }

            lines.Clear();
        }

        foreach (var line in section)
        {
            var header = AdvancedHeader().Match(line.Trim());
            if (header.Success)
            {
                Flush();
                current = ReadHeader(header, b.Rarity);
                continue;
            }

            if (current is null)
            {
                // A line before any header in an advanced section: keep it but do not attribute it.
                b.Unparsed.Add(line);
                continue;
            }

            var parsed = StatText.Parse(line, advanced: true);
            if (!parsed.IsReminderText)
            {
                lines.Add(parsed);
            }
        }

        Flush();
    }

    private sealed record HeaderInfo(ModSource Source, AffixSide Side, string? Name, int? Tier, IReadOnlyList<string> Tags);

    private static HeaderInfo ReadHeader(Match header, ItemRarity rarity)
    {
        var kind = header.Groups["kind"].Value.Trim();
        var side = kind.Contains("Prefix", StringComparison.OrdinalIgnoreCase) ? AffixSide.Prefix
            : kind.Contains("Suffix", StringComparison.OrdinalIgnoreCase) ? AffixSide.Suffix
            : AffixSide.None;
        var source = kind switch
        {
            _ when kind.Contains("Implicit", StringComparison.OrdinalIgnoreCase) => ModSource.Implicit,
            _ when kind.Contains("Rune", StringComparison.OrdinalIgnoreCase) || kind.Contains("Socket", StringComparison.OrdinalIgnoreCase) => ModSource.Rune,
            _ when kind.Contains("Enchant", StringComparison.OrdinalIgnoreCase) => ModSource.Enchant,
            _ when kind.Contains("Desecrated", StringComparison.OrdinalIgnoreCase) => ModSource.Desecrated,
            _ when kind.Contains("Fractured", StringComparison.OrdinalIgnoreCase) => ModSource.Fractured,
            _ when kind.Contains("Crafted", StringComparison.OrdinalIgnoreCase) || kind.Contains("Master", StringComparison.OrdinalIgnoreCase) => ModSource.Crafted,
            _ when kind.Contains("Unique", StringComparison.OrdinalIgnoreCase) => ModSource.Unique,
            _ when side != AffixSide.None => rarity == ItemRarity.Unique ? ModSource.Unique : ModSource.Explicit,
            _ => ModSource.Unknown,
        };
        var name = header.Groups["name"].Success && header.Groups["name"].Value.Length > 0 ? header.Groups["name"].Value : null;
        int? tier = header.Groups["tier"].Success ? int.Parse(header.Groups["tier"].Value, CultureInfo.InvariantCulture) : null;
        var tags = header.Groups["tags"].Success
            ? header.Groups["tags"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : [];
        return new HeaderInfo(source, side, name, tier, tags);
    }

    private static int? IntOrNull(string s)
    {
        var digits = new string(s.TakeWhile(c => char.IsDigit(c) || c == ' ').Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static decimal? DecOrNull(string s) =>
        decimal.TryParse(s.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) ? v : null;

    private sealed class Builder
    {
        public string Raw = "";
        public DateTimeOffset CapturedAt;
        public string CaptureId = "";
        public CopyKind Copy;
        public string ItemClass = "";
        public ItemRarity Rarity;
        public string? Name;
        public string BaseType = "";
        public int? ItemLevel;
        public int? Quality;
        public int? ReqLevel;
        public int? ReqStr;
        public int? ReqDex;
        public int? ReqInt;
        public decimal? PhysMin;
        public decimal? PhysMax;
        public decimal? Aps;
        public decimal? Crit;
        public decimal? Reload;
        public string? Sockets;
        public int? Stack;
        public int? MaxStack;
        public string? Note;
        public ItemStates States;
        public readonly Dictionary<string, string> Properties = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<ModGroup> Mods = [];
        public readonly List<string> Flavour = [];
        public readonly List<string> Unparsed = [];

        public ItemSnapshot Build()
        {
            var weapon = PhysMin is not null || Aps is not null
                ? new WeaponProperties(PhysMin, PhysMax, Aps, Crit, Reload)
                : null;
            return new ItemSnapshot(
                CaptureId, Copy, "en", Patch: null, CapturedAt, ItemClass, Rarity, Name, BaseType, StableId: null,
                ItemLevel, new Requirements(ReqLevel, ReqStr, ReqDex, ReqInt), Quality, weapon,
                Properties, Mods, States, Sockets, Stack, MaxStack, Note, Flavour, Unparsed, Raw);
        }
    }
}
