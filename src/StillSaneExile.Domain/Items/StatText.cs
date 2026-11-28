using System.Globalization;
using System.Text.RegularExpressions;

namespace StillSaneExile.Domain.Items;

/// <summary>Turns a displayed modifier line into a stable template ("#% increased Physical Damage")
/// and its numeric components. Signs are part of the value, never the template, so "+18%" and "-5%"
/// to the same resistance share one template.</summary>
public static partial class StatText
{
    // A number as the game prints it, thousands separated by commas from 1,000 up: "1,043".
    private const string Unsigned = @"(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d+)?";
    private const string Num = @"[+-]?" + Unsigned;

    // Advanced copy: 179(170-179). A reduction prints positive, high to low, "13(15-10)% reduced", and the
    // bounds are swapped below. The range sits directly after the value.
    [GeneratedRegex(@"(?<v>" + Num + @")\((?<lo>-?" + Unsigned + @")\s*[-–]\s*(?<hi>-?" + Unsigned + @")\)")]
    private static partial Regex RangedNumber();

    [GeneratedRegex(@"(?<![\w.#])" + Num + @"(?![\w.])")]
    private static partial Regex PlainNumber();

    // Source tags and "(augmented)", the element tags of property lines ("Lightning Damage: 11-78 (lightning)"),
    // and the advanced copy's " — Unscalable Value".
    [GeneratedRegex(@"(?:\s*\((implicit|rune|enchant|crafted|fractured|desecrated|augmented|unscalable value|local|physical|fire|cold|lightning|chaos)\)|\s+[—–]\s+(Unscalable Value))\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingTag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>Strips trailing source tags such as "(implicit)" and returns them lower-cased.</summary>
    public static (string Text, IReadOnlyList<string> Tags) StripTags(string line)
    {
        var tags = new List<string>();
        var text = line.Trim();
        while (true)
        {
            var m = TrailingTag().Match(text);
            if (!m.Success)
            {
                break;
            }

            tags.Insert(0, (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).ToLowerInvariant());
            text = text[..m.Index].TrimEnd();
        }

        return (text, tags);
    }

    /// <summary>Parses a line. <paramref name="advanced"/> means bare numbers had no printed range,
    /// i.e. the game marks them as fixed.</summary>
    public static ModLine Parse(string line, bool advanced)
    {
        var (text, _) = StripTags(line);
        var trimmed = text.Trim();
        if (trimmed.StartsWith('(') && trimmed.EndsWith(')'))
        {
            return new ModLine(trimmed, trimmed, [], IsReminderText: true);
        }

        var components = new List<(int Index, StatComponent Component)>();
        var unit = trimmed.Contains('%') ? "%" : "";

        var templ = RangedNumber().Replace(trimmed, m =>
        {
            var lo = Dec(m.Groups["lo"].Value);
            var hi = Dec(m.Groups["hi"].Value);
            if (lo > hi)
            {
                (lo, hi) = (hi, lo);
            }

            components.Add((m.Index, new StatComponent(Dec(m.Groups["v"].Value), lo, hi, StatDirection.Unknown, RangeProvenance.AdvancedCopy, unit)));
            return "#";
        });

        // Remaining numbers: in an advanced copy they printed no range, so the game says they are fixed.
        var plainIndex = 0;
        templ = PlainNumber().Replace(templ, m =>
        {
            var value = Dec(m.Value);
            var provenance = advanced ? RangeProvenance.FixedByGame : RangeProvenance.None;
            decimal? fixedAt = advanced ? value : null;
            components.Add((1_000_000 + plainIndex++, new StatComponent(value, fixedAt, fixedAt, StatDirection.Unknown, provenance, unit)));
            return "#";
        });

        // Ranged and plain numbers were collected in two passes; restore reading order by position in the template.
        var ordered = OrderByTemplatePosition(trimmed, components.Select(c => c.Component).ToList());
        return new ModLine(trimmed, NormalizeTemplate(templ), ordered);
    }

    /// <summary>"175(170-179)% increased …" → "175% increased …".</summary>
    public static string WithoutRanges(string text) => RangedNumber().Replace(text, m => m.Groups["v"].Value);

    /// <summary>Template of an already-plain line (catalogue entries are written this way).</summary>
    public static string TemplateOf(string displayed)
    {
        var (text, _) = StripTags(displayed);
        var templ = RangedNumber().Replace(text, "#");
        templ = PlainNumber().Replace(templ, "#");
        return NormalizeTemplate(templ);
    }

    public static string NormalizeTemplate(string template) =>
        Spaces().Replace(template.Replace("+#", "#", StringComparison.Ordinal), " ").Trim();

    public static decimal Dec(string s) => decimal.Parse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

    private static List<StatComponent> OrderByTemplatePosition(string text, List<StatComponent> components)
    {
        // Walk the original text left to right and pick components in the order their numbers appear.
        var positions = new List<(int Pos, decimal Value, bool Ranged)>();
        foreach (Match m in RangedNumber().Matches(text))
        {
            positions.Add((m.Index, Dec(m.Groups["v"].Value), true));
        }

        var masked = RangedNumber().Replace(text, m => new string('~', m.Length));
        foreach (Match m in PlainNumber().Matches(masked))
        {
            positions.Add((m.Index, Dec(m.Value), false));
        }

        positions.Sort((a, b) => a.Pos.CompareTo(b.Pos));
        var ranged = new Queue<StatComponent>(components.Where(c => c.RangeSource == RangeProvenance.AdvancedCopy));
        var plain = new Queue<StatComponent>(components.Where(c => c.RangeSource != RangeProvenance.AdvancedCopy));
        var result = new List<StatComponent>(components.Count);
        foreach (var p in positions)
        {
            var queue = p.Ranged ? ranged : plain;
            if (queue.Count > 0)
            {
                result.Add(queue.Dequeue());
            }
        }

        return result;
    }
}
