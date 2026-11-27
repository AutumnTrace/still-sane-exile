using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StillSaneExile.Domain.Pricing;

/// <summary>Live price snapshots on disk, one folder per league: <c>&lt;root&gt;/&lt;league&gt;/&lt;snapshot id&gt;.json</c>.
/// A snapshot is written once and never changed; the newest few per league are kept, so the app starts
/// with prices when offline. Reading checks the league inside the file, not just the folder, and never
/// returns a synthetic book. Best effort: a file that cannot be written or read is skipped, never thrown.</summary>
public sealed class PriceBookArchive(string root, int keep = 5)
{
    public string Root { get; } = root;

    public int Keep { get; } = Math.Max(1, keep);

    /// <summary>"runes-of-aldur-1c0f9d2a": readable, plus a hash of the exact name so "HC X" and "hc-x" never share a folder.</summary>
    public string FolderFor(string league)
    {
        var slug = PoeNinjaBook.Slug(league);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(league)))[..8];
        return Path.Combine(Root, slug.Length > 0 ? $"{slug}-{hash}" : hash);
    }

    /// <summary>Writes <paramref name="book"/> as a new snapshot (an existing one with the same id is left
    /// as it is), then removes all but the newest <see cref="Keep"/>. Returns the file, or null when it
    /// could not be written. Synthetic and empty books are refused.</summary>
    public string? Save(PriceBook book)
    {
        ArgumentNullException.ThrowIfNull(book);
        if (book.IsSynthetic || book.Quotes.Count == 0)
        {
            return null;
        }

        try
        {
            var folder = FolderFor(book.League);
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, FileName(book.SnapshotId));
            if (!File.Exists(file))
            {
                var temp = file + ".tmp";
                File.WriteAllText(temp, book.ToJson());
                File.Move(temp, file, overwrite: false);
            }

            Prune(folder);
            return file;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The newest readable, non-synthetic snapshot whose league is exactly <paramref name="league"/>.</summary>
    public PriceBook? Newest(string league)
    {
        foreach (var file in Files(league))
        {
            try
            {
                var book = PriceBook.FromJson(File.ReadAllText(file));
                if (!book.IsSynthetic && string.Equals(book.League, league, StringComparison.Ordinal) && book.Quotes.Count > 0)
                {
                    return book;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or NotSupportedException)
            {
                // A damaged snapshot: try the one before it.
            }
        }

        return null;
    }

    /// <summary>Snapshot files for the league, newest first (the ids sort by time).</summary>
    public IReadOnlyList<string> Files(string league)
    {
        var folder = FolderFor(league);
        try
        {
            return Directory.Exists(folder)
                ? [.. Directory.EnumerateFiles(folder, "*.json").OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)]
                : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void Prune(string folder)
    {
        var files = Directory.EnumerateFiles(folder, "*.json").OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal).ToList();
        foreach (var old in files.Skip(Keep))
        {
            try
            {
                File.Delete(old);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Removed next time.
            }
        }
    }

    private static string FileName(string snapshotId)
    {
        var safe = new StringBuilder(snapshotId.Length);
        foreach (var ch in snapshotId)
        {
            safe.Append(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '.' or '_' ? ch : '_');
        }

        return safe + ".json";
    }
}
