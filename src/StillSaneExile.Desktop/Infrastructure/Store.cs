using System.Globalization;
using Microsoft.Data.Sqlite;
using StillSaneExile.Domain.Pricing;
using StillSaneExile.Domain.Wealth;

namespace StillSaneExile.Desktop.Infrastructure;

/// <summary>What one wealth write did. <see cref="Conflict"/> is set when nothing was saved.</summary>
public sealed record WealthWrite(int Added, int AlreadyStored, string? Conflict);

/// <summary>Local SQLite store: settings, the player's private comparison sets and the wealth ledger.
/// Nothing here is sent anywhere. Clipboard text is never stored; an item's text is stored only with a
/// comparison set the player created by adding a comparable.</summary>
public sealed class Store
{
    /// <summary>Snapshot times are stored as fixed-width UTC text, so the same instant always has the same
    /// text (the wealth key relies on it) and text order is time order. A missing variant is stored as ""
    /// rather than NULL, because SQLite treats every NULL as distinct in a primary key.</summary>
    private const string UtcFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    private readonly string _connectionString;

    public Store(string path)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
        using var c = Open();
        Exec(c, """
            CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS comparison_sets (
                profile TEXT NOT NULL, league TEXT NOT NULL, set_key TEXT NOT NULL, item_text TEXT NOT NULL, created_at TEXT NOT NULL,
                PRIMARY KEY (profile, league, set_key));
            CREATE TABLE IF NOT EXISTS comparables (
                id TEXT PRIMARY KEY, profile TEXT NOT NULL, league TEXT NOT NULL, set_key TEXT NOT NULL,
                amount TEXT NOT NULL, currency TEXT NOT NULL, inspected_at TEXT NOT NULL,
                seller TEXT NULL, listing_id TEXT NULL, listing_link TEXT NULL, notes TEXT NULL,
                origin TEXT NOT NULL, expires_at TEXT NULL, created_at TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_comparables_set ON comparables (profile, league, set_key);
            CREATE TABLE IF NOT EXISTS wealth_observations (
                profile TEXT NOT NULL, league TEXT NOT NULL, tab TEXT NOT NULL, item_id TEXT NOT NULL, variant TEXT NOT NULL,
                snapshot_at TEXT NOT NULL, quantity INTEGER NOT NULL CHECK (quantity >= 0),
                origin TEXT NOT NULL, source TEXT NULL, batch_id TEXT NOT NULL, recorded_at TEXT NOT NULL,
                PRIMARY KEY (profile, league, tab, item_id, variant, snapshot_at));
            """);
    }

    public string? Get(string key)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public void Set(string key, string value)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO settings(key, value) VALUES($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<Comparable> Comparables(string profile, string league, string setKey)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT id, league, amount, currency, inspected_at, seller, listing_id, listing_link, notes, origin, expires_at
            FROM comparables WHERE profile = $p AND league = $l AND set_key = $s ORDER BY inspected_at
            """;
        cmd.Parameters.AddWithValue("$p", profile);
        cmd.Parameters.AddWithValue("$l", league);
        cmd.Parameters.AddWithValue("$s", setKey);
        using var r = cmd.ExecuteReader();
        var list = new List<Comparable>();
        while (r.Read())
        {
            list.Add(new Comparable(
                r.GetString(0), r.GetString(1), decimal.Parse(r.GetString(2), CultureInfo.InvariantCulture), r.GetString(3),
                DateTimeOffset.Parse(r.GetString(4), CultureInfo.InvariantCulture),
                r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7),
                r.IsDBNull(8) ? null : r.GetString(8), Enum.Parse<ComparableOrigin>(r.GetString(9)),
                r.IsDBNull(10) ? null : DateTimeOffset.Parse(r.GetString(10), CultureInfo.InvariantCulture)));
        }

        return list;
    }

    /// <summary>Saves the comparable and, the first time, the item it compares against. One transaction.</summary>
    public void AddComparable(string profile, string setKey, string itemText, Comparable comp, DateTimeOffset now)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        using (var set = c.CreateCommand())
        {
            set.Transaction = tx;
            set.CommandText = "INSERT OR IGNORE INTO comparison_sets(profile, league, set_key, item_text, created_at) VALUES($p, $l, $s, $t, $c)";
            set.Parameters.AddWithValue("$p", profile);
            set.Parameters.AddWithValue("$l", comp.League);
            set.Parameters.AddWithValue("$s", setKey);
            set.Parameters.AddWithValue("$t", itemText);
            set.Parameters.AddWithValue("$c", now.ToString("O", CultureInfo.InvariantCulture));
            set.ExecuteNonQuery();
        }

        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO comparables(id, profile, league, set_key, amount, currency, inspected_at, seller, listing_id, listing_link, notes, origin, expires_at, created_at)
                VALUES($id, $p, $l, $s, $a, $cur, $i, $sel, $lid, $link, $n, $o, $e, $c)
                """;
            cmd.Parameters.AddWithValue("$id", comp.Id);
            cmd.Parameters.AddWithValue("$p", profile);
            cmd.Parameters.AddWithValue("$l", comp.League);
            cmd.Parameters.AddWithValue("$s", setKey);
            cmd.Parameters.AddWithValue("$a", comp.Amount.ToString(CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$cur", comp.Currency);
            cmd.Parameters.AddWithValue("$i", comp.InspectedAt.ToString("O", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$sel", (object?)comp.SellerAlias ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$lid", (object?)comp.ListingId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$link", (object?)comp.ListingLink ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$n", (object?)comp.Notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$o", comp.Origin.ToString());
            cmd.Parameters.AddWithValue("$e", comp.ExpiresAt is { } e ? e.ToString("O", CultureInfo.InvariantCulture) : DBNull.Value);
            cmd.Parameters.AddWithValue("$c", now.ToString("O", CultureInfo.InvariantCulture));
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public void RemoveComparable(string id)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM comparables WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Every wealth observation of one profile in one league, oldest first. The ledger is
    /// append-only; current holdings are derived from it (<see cref="WealthLedger.Current"/>).</summary>
    public IReadOnlyList<HoldingEntry> WealthObservations(string profile, string league)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT tab, item_id, variant, snapshot_at, quantity, origin, source
            FROM wealth_observations WHERE profile = $p AND league = $l ORDER BY snapshot_at
            """;
        cmd.Parameters.AddWithValue("$p", profile);
        cmd.Parameters.AddWithValue("$l", league);
        using var r = cmd.ExecuteReader();
        var list = new List<HoldingEntry>();
        while (r.Read())
        {
            list.Add(new HoldingEntry(
                league, r.GetString(0), r.GetString(1), r.GetString(2) is { Length: > 0 } v ? v : null, r.GetInt64(4),
                DateTimeOffset.ParseExact(r.GetString(3), UtcFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                Enum.Parse<HoldingOrigin>(r.GetString(5)), r.IsDBNull(6) ? null : r.GetString(6)));
        }

        return list;
    }

    /// <summary>Adds observations in one transaction: all of them or none. A row already stored with the
    /// same quantity is skipped, so applying the same import twice adds nothing. A row stored with a
    /// different quantity rolls the whole write back; the preview reports those before Apply, so this
    /// only happens if the data changed in between.</summary>
    public WealthWrite AddWealthObservations(string profile, string league, IReadOnlyList<HoldingEntry> entries, DateTimeOffset now)
    {
        if (entries.Any(e => !string.Equals(e.League, league, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Wealth rows from another league can't be stored in this one.");
        }

        var batch = Guid.NewGuid().ToString("N");
        using var c = Open();
        using var tx = c.BeginTransaction();
        using var insert = c.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT INTO wealth_observations(profile, league, tab, item_id, variant, snapshot_at, quantity, origin, source, batch_id, recorded_at)
            VALUES($p, $l, $t, $i, $v, $s, $q, $o, $src, $b, $r)
            ON CONFLICT (profile, league, tab, item_id, variant, snapshot_at) DO NOTHING
            """;
        using var probe = c.CreateCommand();
        probe.Transaction = tx;
        probe.CommandText = """
            SELECT quantity FROM wealth_observations
            WHERE profile = $p AND league = $l AND tab = $t AND item_id = $i AND variant = $v AND snapshot_at = $s
            """;
        foreach (var cmd in new[] { insert, probe })
        {
            cmd.Parameters.AddWithValue("$p", profile);
            cmd.Parameters.AddWithValue("$l", league);
            foreach (var name in new[] { "$t", "$i", "$v", "$s" })
            {
                cmd.Parameters.Add(name, SqliteType.Text);
            }
        }

        insert.Parameters.Add("$q", SqliteType.Integer);
        insert.Parameters.Add("$o", SqliteType.Text);
        insert.Parameters.Add("$src", SqliteType.Text);
        insert.Parameters.AddWithValue("$b", batch);
        insert.Parameters.AddWithValue("$r", Utc(now));

        int added = 0, skipped = 0;
        foreach (var e in entries)
        {
            foreach (var cmd in new[] { insert, probe })
            {
                cmd.Parameters["$t"].Value = e.Tab;
                cmd.Parameters["$i"].Value = e.ItemId;
                cmd.Parameters["$v"].Value = e.Variant ?? "";
                cmd.Parameters["$s"].Value = Utc(e.SnapshotAt);
            }

            insert.Parameters["$q"].Value = e.Quantity;
            insert.Parameters["$o"].Value = e.Origin.ToString();
            insert.Parameters["$src"].Value = (object?)e.Source ?? DBNull.Value;
            if (insert.ExecuteNonQuery() == 1)
            {
                added++;
                continue;
            }

            var stored = (long)probe.ExecuteScalar()!;
            if (stored != e.Quantity)
            {
                tx.Rollback();
                return new WealthWrite(0, 0, $"{e.Tab} · {e.ItemId} at {e.SnapshotAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC is already stored as {stored}, not {e.Quantity}. Nothing was saved.");
            }

            skipped++;
        }

        tx.Commit();
        return new WealthWrite(added, skipped, null);
    }

    /// <summary>Removes everything the demo profile saved: comparables and holdings. Live data is untouched.</summary>
    public void ResetDemo()
    {
        using var c = Open();
        Exec(c, """
            DELETE FROM comparables WHERE profile = 'demo';
            DELETE FROM comparison_sets WHERE profile = 'demo';
            DELETE FROM wealth_observations WHERE profile = 'demo';
            """);
    }

    private static string Utc(DateTimeOffset t) => t.UtcDateTime.ToString(UtcFormat, CultureInfo.InvariantCulture);

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
