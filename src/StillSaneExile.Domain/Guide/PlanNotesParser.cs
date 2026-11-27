using System.Globalization;
using System.Text.RegularExpressions;

namespace StillSaneExile.Domain.Guide;

public enum NoteLineKind
{
    Blank,
    Comment,

    /// <summary>A stage label on its own ("Act 2:", "After ascendancy").</summary>
    Stage,

    Step,
    NotUnderstood,
}

/// <summary>One line of the player's notes and what SSE made of it, for the preview.</summary>
public sealed record NoteLine(int Number, string Text, NoteLineKind Kind, PlanStage Stage, PlanMilestone? Milestone, string? Problem)
{
    /// <summary>The reading the preview shows next to the line.</summary>
    public string Reading => Kind switch
    {
        NoteLineKind.Step => string.Join(" · ", new[] { Stage.Label, Milestone!.Describe() }.Concat(Details(Milestone))),
        NoteLineKind.Stage => $"From here on: {Stage.Label}",
        NoteLineKind.NotUnderstood => Problem ?? "",
        _ => "",
    };

    private static IEnumerable<string> Details(PlanMilestone m)
    {
        if (m.Uncut is { } u)
        {
            yield return $"uncut {PlanDiff.Uncut(u)}";
        }

        if (m.Where is { } w)
        {
            yield return m.ZoneId is null ? $"at {w}" : $"once you reach {w}";
        }

        if (m.Needs.Count > 0)
        {
            yield return $"needs {string.Join(", ", m.Needs)}";
        }

        yield return $"priority: {m.PriorityText}";
    }
}

/// <summary>What the notes turned into: every line with its reading, and the plan from the lines that
/// were understood. Nothing is saved until the player saves it.</summary>
public sealed record PlanDraft(IReadOnlyList<NoteLine> Lines, BuildPlan Plan)
{
    public int Steps => Lines.Count(l => l.Kind == NoteLineKind.Step);

    public IReadOnlyList<NoteLine> Problems => Lines.Where(l => l.Problem is not null).ToList();
}

/// <summary>Turns the player's own notes into a checklist. It only reads what the player typed or
/// pasted into SSE; it never fetches a guide. Recognised lines, case-insensitive:
/// <c>cut Entangle</c>, <c>cut support Zenith I</c>, <c>upgrade Entangle to level 5</c>,
/// <c>socket Zenith I into Entangle</c>, <c>move Zenith I: Entangle → Essence Drain</c> (or
/// <c>move … from … to …</c>), <c>reserve …</c>, <c>respec …</c>. A stage label (<c>Act 2:</c>,
/// <c>after ascendancy:</c>, <c>after 2nd ascendancy:</c>) on its own line or in front of a step applies
/// from there down. Optional extras: <c>at Mud Burrow</c> (a zone or NPC), <c>(level 3 uncut)</c>,
/// <c>(needs Essence Drain)</c>, <c>(priority: high)</c>, <c>(optional)</c>. Bullets and numbering are
/// ignored, as are lines starting with <c>#</c> or <c>//</c>.</summary>
public static partial class PlanNotesParser
{
    public const int MaxGemNameLength = 60;

    public const string NotAStep = "Not a gem step. Start the line with cut, cut support, upgrade, socket, move, reserve or respec, or label a stage like Act 2: or after ascendancy:.";

    public static PlanDraft Parse(string? notes, RoutePack? route = null)
    {
        var lines = new List<NoteLine>();
        var stage = PlanStage.Start;
        var number = 0;
        foreach (var raw in (notes ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            number++;
            var text = raw.Trim();
            if (text.Length == 0)
            {
                lines.Add(new NoteLine(number, raw, NoteLineKind.Blank, stage, null, null));
                continue;
            }

            if (text.StartsWith('#') || text.StartsWith("//", StringComparison.Ordinal))
            {
                lines.Add(new NoteLine(number, raw, NoteLineKind.Comment, stage, null, null));
                continue;
            }

            text = Bullet().Replace(text, "");
            var (rest, labelled, stageProblem) = ReadStage(text, ref stage);
            if (rest.Length == 0)
            {
                lines.Add(new NoteLine(number, raw, labelled ? NoteLineKind.Stage : NoteLineKind.NotUnderstood, stage, null, stageProblem ?? (labelled ? null : NotAStep)));
                continue;
            }

            var (milestone, problem) = ReadStep(rest, number, raw.Trim(), stage, route);
            lines.Add(milestone is null
                ? new NoteLine(number, raw, NoteLineKind.NotUnderstood, stage, null, problem)
                : new NoteLine(number, raw, NoteLineKind.Step, stage, milestone, stageProblem ?? problem));
        }

        return new PlanDraft(lines, new BuildPlan(lines.Where(l => l.Milestone is not null).Select(l => l.Milestone!)));
    }

    /// <summary>Strips stage labels from the front of the line and moves the running stage forward.
    /// Stages only move forward: notes run in play order, so a label that would go back (Act 1 after
    /// Act 2) keeps the later stage and says so, rather than letting a later step show up early.</summary>
    private static (string Remaining, bool Labelled, string? Problem) ReadStage(string text, ref PlanStage stage)
    {
        var labelled = false;
        string? problem = null;
        while (StageLabel().Match(text) is { Success: true } m)
        {
            labelled = true;
            text = text[m.Length..].Trim();
            int? act = m.Groups["n"].Success ? ActNumber(m.Groups["n"].Value) : null;
            int? asc = m.Groups["asc"].Success ? AscendancyNumber(m.Groups["num"].Success ? m.Groups["num"].Value : m.Groups["ord"].Value) : null;
            var before = m.Groups["before"].Success;
            if (act is null && asc is null && !before)
            {
                problem = "That act number is not one SSE knows (Act 1 to Act 10).";
                continue;
            }

            var wanted = new PlanStage(act ?? stage.Act, asc ?? (before ? 0 : stage.Ascendancies));
            if (wanted.IsBefore(stage))
            {
                problem = $"Stages only move forward in your notes, so this stays at {stage.Label}. Move the line up if it belongs earlier.";
                wanted = new PlanStage(Math.Max(wanted.Act, stage.Act), Math.Max(wanted.Ascendancies, stage.Ascendancies));
            }

            stage = wanted;
        }

        return (text, labelled, problem);
    }

    private static (PlanMilestone? Milestone, string? Problem) ReadStep(string text, int line, string source, PlanStage stage, RoutePack? route)
    {
        string? priority = null;
        var needs = new List<string>();
        UncutKind? kind = null;
        int? level = null;

        // Bracketed extras: (priority: high), (optional), (needs Essence Drain), (level 3 uncut skill gem).
        text = Bracketed().Replace(text, b =>
        {
            var inner = b.Groups[1].Value.Trim();
            if (PriorityNote().Match(inner) is { Success: true } p)
            {
                priority = (p.Groups[1].Success ? p.Groups[1].Value : p.Groups[2].Value).Trim();
            }
            else if (inner.Equals("optional", StringComparison.OrdinalIgnoreCase))
            {
                priority = "optional";
            }
            else if (NeedsNote().Match(inner) is { Success: true } n)
            {
                needs.AddRange(ListSplit().Split(n.Groups[1].Value).Select(Clean).Where(g => g.Length > 0));
            }
            else
            {
                ReadUncut(inner, ref kind, ref level);
            }

            return " ";
        }).Trim();

        // Trailing "with a level 5 uncut skill gem" / "lvl 3".
        if (TrailingUncut().Match(text) is { Success: true } tu)
        {
            ReadUncut(tu.Value, ref kind, ref level);
            text = text[..tu.Index].Trim();
        }

        // Trailing "at Mud Burrow" (zone or NPC), or "in Clearfell" when it names a route zone.
        string? where = null;
        if (TrailingAt().Match(text) is { Success: true } at)
        {
            where = Clean(at.Groups[1].Value);
            text = text[..at.Index].Trim();
        }
        else if (TrailingIn().Match(text) is { Success: true } inZone && route?.ResolveZone(inZone.Groups[1].Value) is not null)
        {
            where = Clean(inZone.Groups[1].Value);
            text = text[..inZone.Index].Trim();
        }

        var (action, gem, from, target, toLevel) = Action(text);
        if (action is null)
        {
            // A known verb with a missing part gets told what is missing; anything else the general hint.
            return (null, text.Split(' ')[0].ToLowerInvariant() switch
            {
                "socket" => "Say which skill it goes into, like socket Zenith I into Entangle.",
                "move" => "Say where it goes, like move Zenith I: Entangle → Essence Drain.",
                _ => NotAStep,
            });
        }

        level ??= toLevel;
        if (gem.Length > MaxGemNameLength || (from?.Length ?? 0) > MaxGemNameLength || (target?.Length ?? 0) > MaxGemNameLength)
        {
            return (null, "That gem name is very long. Check the line: one step per line.");
        }

        if (action != GemAction.Respec && gem.Length == 0)
        {
            return (null, "Which gem? Put its name after the action.");
        }

        if (action is GemAction.Socket or GemAction.MoveSupport && string.IsNullOrEmpty(target))
        {
            return (null, "Which skill does the support go into?");
        }

        if (action == GemAction.MoveSupport && from is not null && GemName.Key(from) == GemName.Key(target))
        {
            return (null, $"That moves {gem} from {from} to the same skill.");
        }

        var uncut = kind is null && level is null
            ? null
            : new UncutGem(kind ?? (action switch { GemAction.CutSupport => UncutKind.Support, GemAction.Reserve => UncutKind.Spirit, _ => UncutKind.Skill }), level);

        var milestone = new PlanMilestone
        {
            Line = line,
            Source = source,
            Stage = stage,
            Action = action.Value,
            Gem = gem,
            From = from,
            Target = target,
            Uncut = uncut,
            Where = where,
            ZoneId = route?.ResolveZone(where)?.Id,
            Needs = needs,
            Priority = priority,
        };
        return (milestone, null);
    }

    private static (GemAction? Action, string Gem, string? From, string? Target, int? Level) Action(string text)
    {
        if (CutSupport().Match(text) is { Success: true } cs)
        {
            return (GemAction.CutSupport, Clean(cs.Groups["gem"].Value), null, null, null);
        }

        if (Cut().Match(text) is { Success: true } c)
        {
            return (GemAction.Cut, Clean(c.Groups["gem"].Value), null, null, null);
        }

        if (Upgrade().Match(text) is { Success: true } u)
        {
            int? lvl = u.Groups["lvl"].Success ? int.Parse(u.Groups["lvl"].Value, CultureInfo.InvariantCulture) : null;
            return (GemAction.Upgrade, Clean(u.Groups["gem"].Value), null, null, lvl);
        }

        if (Socket().Match(text) is { Success: true } s)
        {
            return (GemAction.Socket, Clean(s.Groups["gem"].Value), null, Clean(s.Groups["target"].Value), null);
        }

        foreach (var pattern in new[] { MoveArrow(), MoveFromTo(), MoveTo() })
        {
            if (pattern.Match(text) is { Success: true } m)
            {
                var from = m.Groups["from"].Success ? Clean(m.Groups["from"].Value) : null;
                return (GemAction.MoveSupport, Clean(m.Groups["gem"].Value), from, Clean(m.Groups["target"].Value), null);
            }
        }

        if (Reserve().Match(text) is { Success: true } r)
        {
            return (GemAction.Reserve, Clean(r.Groups["gem"].Value), null, null, null);
        }

        if (Respec().Match(text) is { Success: true } rs)
        {
            return (GemAction.Respec, Clean(rs.Groups["what"].Value), null, null, null);
        }

        return (null, "", null, null, null);
    }

    private static void ReadUncut(string text, ref UncutKind? kind, ref int? level)
    {
        if (LevelNumber().Match(text) is { Success: true } l)
        {
            level = int.Parse(l.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        if (UncutType().Match(text) is { Success: true } t)
        {
            kind = Enum.Parse<UncutKind>(t.Groups[1].Value, ignoreCase: true);
        }
    }

    private static string Clean(string s) =>
        string.Join(' ', s.Trim().Trim('"', '\'', '“', '”').TrimEnd('.', ',', ';', ':').Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static int? ActNumber(string s)
    {
        var n = int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var d) ? d : Roman(s.ToLowerInvariant());
        return n is >= 1 and <= CampaignProgress.MaxAct ? n : null;
    }

    private static int? Roman(string s) => s switch
    {
        "i" => 1, "ii" => 2, "iii" => 3, "iv" => 4, "v" => 5,
        "vi" => 6, "vii" => 7, "viii" => 8, "ix" => 9, "x" => 10,
        _ => null,
    };

    private static int AscendancyNumber(string ordinal) => ordinal.ToLowerInvariant() switch
    {
        "second" or "2nd" or "2" => 2,
        "third" or "3rd" or "3" => 3,
        "fourth" or "4th" or "4" => 4,
        _ => 1,
    };

    [GeneratedRegex(@"^(?:[-*•·>]+|\d{1,3}[.)]|\[[ xX]?\])\s*")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"^(?:act\s*(?<n>\d{1,2}|[ivx]{1,4})\b|(?<asc>(?:after\s+(?:the\s+|your\s+)?(?:(?<ord>first|1st|second|2nd|third|3rd|fourth|4th)\s+)?|post[\s-]?)ascendancy(?:\s+trial)?(?:\s*#?(?<num>[1-4]))?)\b|(?<before>(?:before\s+(?:the\s+|your\s+)?(?:first\s+|1st\s+)?|pre[\s-]?)ascendancy)\b)\s*[:,\-–—]?\s*", RegexOptions.IgnoreCase)]
    private static partial Regex StageLabel();

    [GeneratedRegex(@"\(([^()]*)\)")]
    private static partial Regex Bracketed();

    [GeneratedRegex(@"^(?:priority\s*[:\-]?\s*(.+)|(.+?)\s+priority)$", RegexOptions.IgnoreCase)]
    private static partial Regex PriorityNote();

    [GeneratedRegex(@"^(?:needs?|requires?|once you have)\s+(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex NeedsNote();

    [GeneratedRegex(@"\s*,\s*|\s+and\s+", RegexOptions.IgnoreCase)]
    private static partial Regex ListSplit();

    [GeneratedRegex(@"(?<!\bto\s*)\s+(?:(?:with|using)\s+(?:an?\s+)?(?:(?:level|lvl|lv)\s*\d{1,2}\s+)?uncut(?:\s+(?:skill|support|spirit))?(?:\s+gem)?(?:\s+(?:level|lvl|lv)\s*\d{1,2})?|(?:level|lvl|lv)\s*\d{1,2}(?:\s+uncut(?:\s+(?:skill|support|spirit))?(?:\s+gem)?)?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingUncut();

    [GeneratedRegex(@"(?:level|lvl|lv)\s*(\d{1,2})", RegexOptions.IgnoreCase)]
    private static partial Regex LevelNumber();

    [GeneratedRegex(@"\b(skill|support|spirit)\b", RegexOptions.IgnoreCase)]
    private static partial Regex UncutType();

    [GeneratedRegex(@"\s+at\s+(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingAt();

    [GeneratedRegex(@"\s+in\s+(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingIn();

    [GeneratedRegex(@"^cut\s+support\b(?:\s+gem\b)?\s*[:\-]?\s*(?<gem>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex CutSupport();

    [GeneratedRegex(@"^cut\b\s*[:\-]?\s*(?<gem>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex Cut();

    [GeneratedRegex(@"^(?:upgrade|level\s+up)\s+(?<gem>.+?)(?:\s+to\s+(?:level|lvl|lv)\s*(?<lvl>\d{1,2}))?$", RegexOptions.IgnoreCase)]
    private static partial Regex Upgrade();

    [GeneratedRegex(@"^socket\s+(?<gem>.+?)\s+(?:into|in|on|onto)\s+(?<target>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex Socket();

    [GeneratedRegex(@"^move\s+(?:support\s+)?(?<gem>[^:]+?)\s*:\s*(?<from>.+?)(?:\s*(?:→|->|=>|⇒)\s*|\s+(?:to|into|onto)\s+)(?<target>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex MoveArrow();

    [GeneratedRegex(@"^move\s+(?:support\s+)?(?<gem>.+?)\s+from\s+(?<from>.+?)\s+(?:to|into|onto)\s+(?<target>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex MoveFromTo();

    [GeneratedRegex(@"^move\s+(?:support\s+)?(?<gem>.+?)\s+(?:to|into|onto)\s+(?<target>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex MoveTo();

    [GeneratedRegex(@"^reserve\s+(?:spirit\s+(?:for|on)\s+)?(?<gem>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex Reserve();

    [GeneratedRegex(@"^respec\b\s*[:\-]?\s*(?<what>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex Respec();
}
