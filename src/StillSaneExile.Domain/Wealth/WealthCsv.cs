using System.Globalization;
using System.Text;

namespace StillSaneExile.Domain.Wealth;

/// <summary>One data row of an import: its line number in the file, and either the entry it reads as or
/// every reason it can't be used. <see cref="Raw"/> is the row's values as written, for the preview.</summary>
public sealed record WealthCsvRow(int Row, HoldingEntry? Entry, IReadOnlyList<string> Errors, string Raw)
{
    public bool Ok => Entry is not null;
}

/// <summary>The whole file, read and checked. Nothing in it has been stored. <see cref="FileError"/> is
/// set when the file as a whole can't be read (no header, too big, not UTF-8); then there are no rows.</summary>
public sealed record WealthCsvResult(string? FileError, IReadOnlyList<WealthCsvRow> Rows, IReadOnlyList<string> OtherLeagues)
{
    public int ReadyCount => Rows.Count(r => r.Ok);
    public int ErrorCount => Rows.Count - ReadyCount;

    /// <summary>True when every row is usable: an import applies all of its rows or none.</summary>
    public bool Ok => FileError is null && Rows.Count > 0 && ErrorCount == 0;

    public IReadOnlyList<HoldingEntry> Entries => Rows.Where(r => r.Ok).Select(r => r.Entry!).ToList();

    internal static WealthCsvResult Fail(string error) => new(error, [], []);
}

/// <summary>Reads the wealth CSV format (docs/WEALTH-CSV.md): a header row naming the columns in any
/// order, then one holding per row. Every row is checked and reported on its own, so the preview can
/// show all problems at once instead of stopping at the first.</summary>
public static class WealthCsv
{
    public const int MaxRows = 10_000;
    public const int MaxBytes = 4 * 1024 * 1024;

    public const string League = "league";
    public const string Tab = "tab";
    public const string ItemId = "item_id";
    public const string Variant = "variant";
    public const string Quantity = "quantity";
    public const string SnapshotTime = "snapshot_time";

    /// <summary>The columns in their documented order. All but <see cref="Variant"/> are required.</summary>
    public static readonly IReadOnlyList<string> Columns = [League, Tab, ItemId, Variant, Quantity, SnapshotTime];

    private const string ColumnList = "league, tab, item_id, variant (optional), quantity and snapshot_time";

    /// <summary>Reads a file's bytes: strict UTF-8, with or without a byte-order mark.</summary>
    public static WealthCsvResult Read(byte[] bytes, string fileName, string activeLeague, DateTimeOffset now)
    {
        if (bytes.Length > MaxBytes)
        {
            return TooBig(bytes.Length);
        }

        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return WealthCsvResult.Fail("The file isn't UTF-8 text. In Excel, save it as \"CSV UTF-8 (Comma delimited)\".");
        }

        return Read(text, fileName, activeLeague, now);
    }

    public static WealthCsvResult Read(string text, string fileName, string activeLeague, DateTimeOffset now)
    {
        if (text.Length > MaxBytes)
        {
            return TooBig(Encoding.UTF8.GetByteCount(text));
        }

        if (text.Length > 0 && text[0] == '﻿')
        {
            text = text[1..];
        }

        using var records = Records(text).Where(r => r.Error is not null || !r.Fields.All(string.IsNullOrWhiteSpace)).GetEnumerator();
        if (!records.MoveNext())
        {
            return WealthCsvResult.Fail("The file is empty.");
        }

        var (columns, headerError) = Header(records.Current);
        if (headerError is not null)
        {
            return WealthCsvResult.Fail(headerError);
        }

        var rows = new List<WealthCsvRow>();
        var seen = new Dictionary<(HoldingKey, DateTimeOffset), int>();
        var otherLeagues = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        while (records.MoveNext())
        {
            if (rows.Count == MaxRows)
            {
                return WealthCsvResult.Fail($"The file has more than {MaxRows.ToString("#,0", CultureInfo.InvariantCulture)} rows. Split it into smaller files.");
            }

            var record = records.Current;
            var raw = string.Join(", ", record.Fields.Select(f => f.Trim()));
            if (record.Error is not null)
            {
                rows.Add(new WealthCsvRow(record.Line, null, [record.Error], raw));
                continue;
            }

            if (record.Fields.Count != columns.Count)
            {
                rows.Add(new WealthCsvRow(record.Line, null, [FieldCount(record.Fields.Count, columns.Count)], raw));
                continue;
            }

            string? Field(string column) => columns.TryGetValue(column, out var i) ? record.Fields[i] : null;
            var errors = new List<string>();
            void Check(string? error)
            {
                if (error is not null)
                {
                    errors.Add(error);
                }
            }

            var leagueError = HoldingFields.League(Field(League), activeLeague, out var league);
            if (leagueError is not null && league.Length > 0)
            {
                otherLeagues.Add(league);
            }

            Check(leagueError);
            Check(HoldingFields.Tab(Field(Tab), out var tab));
            Check(HoldingFields.ItemId(Field(ItemId), out var id));
            Check(HoldingFields.Variant(Field(Variant), out var variant));
            Check(HoldingFields.Quantity(Field(Quantity), out var quantity));
            Check(HoldingFields.SnapshotTime(Field(SnapshotTime), now, out var at));

            HoldingEntry? entry = null;
            if (errors.Count == 0)
            {
                var key = (new HoldingKey(tab, id, variant), at);
                if (seen.TryGetValue(key, out var first))
                {
                    errors.Add($"Same tab, item, variant and snapshot time as row {first}.");
                }
                else
                {
                    seen[key] = record.Line;
                    entry = new HoldingEntry(league, tab, id, variant, quantity, at, HoldingOrigin.CsvImport, fileName);
                }
            }

            rows.Add(new WealthCsvRow(record.Line, entry, errors, raw));
        }

        return rows.Count == 0
            ? WealthCsvResult.Fail("The file has a header but no rows.")
            : new WealthCsvResult(null, rows, otherLeagues.ToList());
    }

    /// <summary>The answer for a file over <see cref="MaxBytes"/>, so a caller can refuse it without reading it.</summary>
    public static WealthCsvResult TooBig(long bytes) =>
        WealthCsvResult.Fail($"The file is {bytes / (1024m * 1024m):0.#} MB; the limit is {MaxBytes / (1024 * 1024)} MB. Split it into smaller files.");

    private static string FieldCount(int got, int want) => got > want
        ? $"This row has {got} values; the header has {want}. A comma inside a value needs quotes around the value, and quantities need no separators (1200, not 1,200)."
        : $"This row has {got} values; the header has {want}.";

    /// <summary>Maps each known column to its position. Names ignore case, spaces, hyphens and
    /// underscores ("Item ID" is item_id). An unknown column is an error rather than ignored: a misspelt
    /// optional column ("varient") would otherwise drop every variant without a word.</summary>
    private static (Dictionary<string, int> Columns, string? Error) Header(Record header)
    {
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        if (header.Error is not null)
        {
            return (columns, $"The header row can't be read: {header.Error}");
        }

        if (header.Fields.Count == 1 && header.Fields[0].Contains(';', StringComparison.Ordinal))
        {
            return (columns, "The values are separated by semicolons. Save the file with commas between values (in Excel: \"CSV UTF-8 (Comma delimited)\").");
        }

        var names = header.Fields.Select(Normalize).ToList();
        if (!names.Any(n => Columns.Contains(n)))
        {
            return (columns, $"The first row must be the header: {string.Join(",", Columns)}.");
        }

        var unknown = new List<string>();
        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];
            if (name.Length == 0)
            {
                return (columns, $"Column {i + 1} of the header has no name. Remove the extra comma, or name the column.");
            }

            if (!Columns.Contains(name))
            {
                unknown.Add($"\"{HoldingFields.Clip(header.Fields[i].Trim())}\"");
            }
            else if (!columns.TryAdd(name, i))
            {
                return (columns, $"The column {name} appears twice in the header.");
            }
        }

        if (unknown.Count > 0)
        {
            var which = unknown.Count == 1 ? $"Unknown column {unknown[0]}" : $"Unknown columns {string.Join(", ", unknown)}";
            return (columns, $"{which}. The columns are {ColumnList}; remove any others.");
        }

        var missing = Columns.Where(c => c != Variant && !columns.ContainsKey(c)).ToList();
        return missing.Count > 0
            ? (columns, $"The header has no {string.Join(", ", missing)} column. The columns are {ColumnList}.")
            : (columns, null);
    }

    private static string Normalize(string name)
    {
        var s = new string(name.Trim().Where(c => c is not (' ' or '_' or '-')).ToArray()).ToLowerInvariant();
        return s switch
        {
            "itemid" => ItemId,
            "snapshottime" => SnapshotTime,
            _ => s,
        };
    }

    /// <summary>A record as read: the line it starts on, its values, and a quoting problem if it has one.</summary>
    private sealed record Record(int Line, List<string> Fields, string? Error);

    /// <summary>RFC 4180 records: values separated by commas; a value in double quotes may hold commas,
    /// line breaks and doubled quotes (""). Lines end in CRLF, LF or CR. An unclosed quote swallows the
    /// rest of the file, so it is reported on the row it starts and reading stops there.</summary>
    private static IEnumerable<Record> Records(string text)
    {
        var i = 0;
        var line = 1;
        var value = new StringBuilder();
        while (i < text.Length)
        {
            var start = line;
            var fields = new List<string>();
            string? error = null;
            while (true)
            {
                value.Clear();
                if (i < text.Length && text[i] == '"')
                {
                    i++;
                    var closed = false;
                    while (i < text.Length)
                    {
                        var c = text[i];
                        if (c == '"')
                        {
                            if (i + 1 < text.Length && text[i + 1] == '"')
                            {
                                value.Append('"');
                                i += 2;
                                continue;
                            }

                            i++;
                            closed = true;
                            break;
                        }

                        if (c == '\n' || (c == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n')))
                        {
                            line++;
                        }

                        value.Append(c);
                        i++;
                    }

                    if (!closed)
                    {
                        fields.Add(value.ToString());
                        yield return new Record(start, fields, "A quoted value starts on this row and never ends. Check for a missing closing quote.");
                        yield break;
                    }

                    var after = i;
                    while (i < text.Length && text[i] is not (',' or '\r' or '\n'))
                    {
                        i++;
                    }

                    if (text.AsSpan(after, i - after).Trim().Length > 0)
                    {
                        error ??= $"Value {fields.Count + 1} has text after its closing quote. Put the whole value inside the quotes.";
                    }
                }
                else
                {
                    while (i < text.Length && text[i] is not (',' or '\r' or '\n'))
                    {
                        value.Append(text[i]);
                        i++;
                    }
                }

                fields.Add(value.ToString());
                if (i < text.Length && text[i] == ',')
                {
                    i++;
                    continue;
                }

                if (i < text.Length)
                {
                    i += text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
                    line++;
                }

                break;
            }

            yield return new Record(start, fields, error);
        }
    }
}
