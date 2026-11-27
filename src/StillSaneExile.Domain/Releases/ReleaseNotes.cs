using System.Text;
using System.Text.RegularExpressions;

namespace StillSaneExile.Domain.Releases;

public enum NoteBlockKind
{
    Paragraph,
    Bullet,
    Heading,
}

[Flags]
public enum NoteStyle
{
    None = 0,
    Bold = 1,
    Code = 2,
}

/// <summary>A run of text in one style. <see cref="Url"/> is set only for http(s) links.</summary>
public sealed record NoteSpan(string Text, NoteStyle Style = NoteStyle.None, string? Url = null);

public sealed record NoteBlock(NoteBlockKind Kind, IReadOnlyList<NoteSpan> Spans);

/// <summary>The small markdown subset the changelog uses (bullets, <c>### </c> headings, <c>**bold**</c>,
/// <c>`code`</c> and <c>[links](https://…)</c>), turned into blocks and spans the app can draw with plain
/// text runs. Anything else stays literal text, so a stray marker can never hide words.</summary>
public static partial class ReleaseNotes
{
    [GeneratedRegex(@"^#{1,6}\s+(?<text>.*?)\s*#*$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^(?:-{3,}|\*{3,}|_{3,})$")]
    private static partial Regex Rule();

    public static IReadOnlyList<NoteBlock> Parse(string markdown)
    {
        var blocks = new List<NoteBlock>();
        NoteBlockKind? kind = null;
        var text = new StringBuilder();

        void Flush()
        {
            if (kind is not null && text.Length > 0)
            {
                blocks.Add(new NoteBlock(kind.Value, ParseInline(text.ToString())));
            }

            kind = null;
            text.Clear();
        }

        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || Rule().IsMatch(line))
            {
                Flush();
                continue;
            }

            var heading = Heading().Match(line);
            if (heading.Success)
            {
                Flush();
                if (heading.Groups["text"].Length > 0)
                {
                    blocks.Add(new NoteBlock(NoteBlockKind.Heading, ParseInline(heading.Groups["text"].Value)));
                }

                continue;
            }

            if (line.Length > 2 && (line[0] is '-' or '*' or '+') && line[1] == ' ')
            {
                Flush();
                kind = NoteBlockKind.Bullet;
                text.Append(line[2..].TrimStart());
                continue;
            }

            // A wrapped line continues the bullet or paragraph above it.
            kind ??= NoteBlockKind.Paragraph;
            if (text.Length > 0)
            {
                text.Append(' ');
            }

            text.Append(line);
        }

        Flush();
        return blocks;
    }

    /// <summary>Bold, code and links within one block. Unclosed markers stay as typed; links that are
    /// not http(s) (relative paths in the repo, for one) keep their text and lose the link.</summary>
    public static IReadOnlyList<NoteSpan> ParseInline(string text)
    {
        var spans = new List<NoteSpan>();
        Inline(text, NoteStyle.None, null, spans);
        return spans;
    }

    private static void Inline(string s, NoteStyle style, string? url, List<NoteSpan> spans)
    {
        var plain = new StringBuilder();

        void Flush()
        {
            if (plain.Length > 0)
            {
                spans.Add(new NoteSpan(plain.ToString(), style, url));
                plain.Clear();
            }
        }

        var i = 0;
        while (i < s.Length)
        {
            if (s[i] == '`')
            {
                var end = s.IndexOf('`', i + 1);
                if (end > i + 1)
                {
                    Flush();
                    spans.Add(new NoteSpan(s[(i + 1)..end], style | NoteStyle.Code, url));
                    i = end + 1;
                    continue;
                }
            }
            else if (s[i] == '*' && i + 1 < s.Length && s[i + 1] == '*')
            {
                var end = s.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (end > i + 2)
                {
                    Flush();
                    Inline(s[(i + 2)..end], style | NoteStyle.Bold, url, spans);
                    i = end + 2;
                    continue;
                }
            }
            else if (s[i] == '[' && url is null)
            {
                var middle = s.IndexOf("](", i + 1, StringComparison.Ordinal);
                var close = middle < 0 ? -1 : s.IndexOf(')', middle + 2);
                if (middle > i + 1 && close > middle + 2)
                {
                    Flush();
                    var target = s[(middle + 2)..close].Trim();
                    Inline(s[(i + 1)..middle], style, IsWebLink(target) ? target : null, spans);
                    i = close + 1;
                    continue;
                }
            }

            plain.Append(s[i]);
            i++;
        }

        Flush();
    }

    private static bool IsWebLink(string target) =>
        Uri.TryCreate(target, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
