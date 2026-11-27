using System.Globalization;
using Microsoft.Data.Sqlite;
using StillSaneExile.Domain.Guide;
using StillSaneExile.Domain.Pricing;
using StillSaneExile.Domain.Wealth;

namespace StillSaneExile.Desktop.Infrastructure;

/// <summary>What one wealth write did. <see cref="Conflict"/> is set when nothing was saved.</summary>
public sealed record WealthWrite(int Added, int AlreadyStored, string? Conflict);

/// <summary>Local SQLite store: settings, the player's private comparison sets, the wealth ledger and their
/// campaign characters. Nothing here is sent anywhere. Clipboard text is never stored; an item's text is
/// stored only with a comparison set the player created by adding a comparable.</summary>
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
            CREATE TABLE IF NOT EXISTS guide_characters (
                name TEXT NOT NULL COLLATE NOCASE PRIMARY KEY, pack_id TEXT NOT NULL,
                leg INTEGER NOT NULL, zone TEXT NULL, act INTEGER NOT NULL,
                created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS guide_marks (
                character TEXT NOT NULL COLLATE NOCASE, seq INTEGER NOT NULL,
                step_id TEXT NOT NULL, state TEXT NOT NULL, at TEXT NOT NULL,
                PRIMARY KEY (character, seq));
            CREATE TABLE IF NOT EXISTS guide_builds (
                character TEXT NOT NULL COLLATE NOCASE PRIMARY KEY, link TEXT NULL,
                ascendancies INTEGER NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS guide_plans (
                character TEXT NOT NULL COLLATE NOCASE, revision INTEGER NOT NULL, status TEXT NOT NULL,
                notes TEXT NOT NULL, milestones TEXT NOT NULL, created_at TEXT NOT NULL,
                PRIMARY KEY (character, revision));
            CREATE TABLE IF NOT EXISTS guide_plan_done (
                character TEXT NOT NULL COLLATE NOCASE, step_key TEXT NOT NULL, done_at TEXT NOT NULL,
                PRIMARY KEY (character, step_key));
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

    /// <summary>Campaign characters, by name. Guide progress is not tied to the price profile: the same
    /// character is there in the demo and in a live league.</summary>
    public IReadOnlyList<string> GuideCharacters()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT name FROM guide_characters ORDER BY created_at";
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read())
        {
            list.Add(r.GetString(0));
        }

        return list;
    }

    /// <summary>Adds a character. Returns false when one with that name (any case) already exists.</summary>
    public bool AddGuideCharacter(string name, string packId, CampaignPosition start, DateTimeOffset now)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO guide_characters(name, pack_id, leg, zone, act, created_at, updated_at)
            VALUES($n, $p, $l, $z, $a, $t, $t)
            """;
        AddPosition(cmd, name, packId, start, now);
        return cmd.ExecuteNonQuery() == 1;
    }

    public CampaignPosition? GuidePosition(string name, string packId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT leg, zone, act FROM guide_characters WHERE name = $n AND pack_id = $p";
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$p", packId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new CampaignPosition(r.GetInt32(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetInt32(2)) : null;
    }

    public void SaveGuidePosition(string name, string packId, CampaignPosition position, DateTimeOffset now)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE guide_characters SET pack_id = $p, leg = $l, zone = $z, act = $a, updated_at = $t WHERE name = $n";
        AddPosition(cmd, name, packId, position, now);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<StepMark> GuideMarks(string name)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT seq, step_id, state, at FROM guide_marks WHERE character = $n ORDER BY seq";
        cmd.Parameters.AddWithValue("$n", name);
        using var r = cmd.ExecuteReader();
        var list = new List<StepMark>();
        while (r.Read())
        {
            if (Enum.TryParse<StepState>(r.GetString(2), out var state))
            {
                list.Add(new StepMark(r.GetInt64(0), r.GetString(1), state, DateTimeOffset.Parse(r.GetString(3), CultureInfo.InvariantCulture)));
            }
        }

        return list;
    }

    public void AddGuideMark(string name, StepMark mark)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO guide_marks(character, seq, step_id, state, at) VALUES($n, $s, $id, $st, $at)";
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$s", mark.Seq);
        cmd.Parameters.AddWithValue("$id", mark.StepId);
        cmd.Parameters.AddWithValue("$st", mark.State.ToString());
        cmd.Parameters.AddWithValue("$at", mark.At.ToString("O", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    public void RemoveGuideMark(string name, long seq)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM guide_marks WHERE character = $n AND seq = $s";
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$s", seq);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Removes the character and everything saved for them. One transaction.</summary>
    public void RemoveGuideCharacter(string name)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        foreach (var table in GuideTables)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"DELETE FROM {table.Name} WHERE {table.Key} = $n";
            cmd.Parameters.AddWithValue("$n", name);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    /// <summary>The character's saved guide link and how many ascendancies they have done.</summary>
    public GuideBuildRow GuideBuild(string name)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT link, ascendancies FROM guide_builds WHERE character = $n";
        cmd.Parameters.AddWithValue("$n", name);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new GuideBuildRow(r.IsDBNull(0) ? null : r.GetString(0), r.GetInt32(1)) : new GuideBuildRow(null, 0);
    }

    public void SaveGuideBuild(string name, GuideBuildRow build, DateTimeOffset now)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO guide_builds(character, link, ascendancies, updated_at) VALUES($n, $l, $a, $t)
            ON CONFLICT(character) DO UPDATE SET link = excluded.link, ascendancies = excluded.ascendancies, updated_at = excluded.updated_at
            """;
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$l", (object?)build.Link ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$a", build.Ascendancies);
        cmd.Parameters.AddWithValue("$t", now.ToString("O", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    /// <summary>The plan revisions still in play: the active one and a pending one, if any. Retired
    /// revisions stay in the table but are not returned.</summary>
    public IReadOnlyList<GuidePlanRow> GuidePlans(string name)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT revision, status, notes, milestones, created_at FROM guide_plans WHERE character = $n AND status <> 'retired' ORDER BY revision";
        cmd.Parameters.AddWithValue("$n", name);
        using var r = cmd.ExecuteReader();
        var list = new List<GuidePlanRow>();
        while (r.Read())
        {
            list.Add(new GuidePlanRow(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), DateTimeOffset.Parse(r.GetString(4), CultureInfo.InvariantCulture)));
        }

        return list;
    }

    /// <summary>Saves a new revision. As "active" it retires the one in use; as "pending" it replaces
    /// any earlier pending revision and leaves the active one alone. Returns the revision number.</summary>
    public int SaveGuidePlan(string name, string status, string notes, string milestones, DateTimeOffset now)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        Exec(c, tx, name, status == GuidePlanRow.Active ? "UPDATE guide_plans SET status = 'retired' WHERE character = $n AND status IN ('active', 'pending')" : "DELETE FROM guide_plans WHERE character = $n AND status = 'pending'");
        using var next = c.CreateCommand();
        next.Transaction = tx;
        next.CommandText = "SELECT COALESCE(MAX(revision), 0) + 1 FROM guide_plans WHERE character = $n";
        next.Parameters.AddWithValue("$n", name);
        var revision = Convert.ToInt32(next.ExecuteScalar(), CultureInfo.InvariantCulture);

        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO guide_plans(character, revision, status, notes, milestones, created_at) VALUES($n, $r, $s, $notes, $m, $t)";
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$r", revision);
        cmd.Parameters.AddWithValue("$s", status);
        cmd.Parameters.AddWithValue("$notes", notes);
        cmd.Parameters.AddWithValue("$m", milestones);
        cmd.Parameters.AddWithValue("$t", now.ToString("O", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
        tx.Commit();
        return revision;
    }

    /// <summary>The player accepted the pending revision: it becomes the plan in use.</summary>
    public void AcceptGuidePlan(string name, int revision)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        Exec(c, tx, name, "UPDATE guide_plans SET status = 'retired' WHERE character = $n AND status = 'active'");
        Exec(c, tx, name, "UPDATE guide_plans SET status = 'active' WHERE character = $n AND revision = $r AND status = 'pending'", revision);
        tx.Commit();
    }

    public void DiscardGuidePlan(string name, int revision)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM guide_plans WHERE character = $n AND revision = $r AND status = 'pending'";
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$r", revision);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Ticked plan steps, by key. Keys survive revisions, so ticks carry over.</summary>
    public HashSet<string> GuidePlanDone(string name)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT step_key FROM guide_plan_done WHERE character = $n";
        cmd.Parameters.AddWithValue("$n", name);
        using var r = cmd.ExecuteReader();
        var set = new HashSet<string>(StringComparer.Ordinal);
        while (r.Read())
        {
            set.Add(r.GetString(0));
        }

        return set;
    }

    public void SetGuidePlanDone(string name, string key, bool done, DateTimeOffset now)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = done
            ? "INSERT OR IGNORE INTO guide_plan_done(character, step_key, done_at) VALUES($n, $k, $t)"
            : "DELETE FROM guide_plan_done WHERE character = $n AND step_key = $k";
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$t", now.ToString("O", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    /// <summary>Every table that holds something about a guide character, and its name column.</summary>
    private static readonly (string Name, string Key)[] GuideTables =
    [
        ("guide_marks", "character"),
        ("guide_builds", "character"),
        ("guide_plans", "character"),
        ("guide_plan_done", "character"),
        ("guide_characters", "name"),
    ];

    private static void Exec(SqliteConnection c, SqliteTransaction tx, string name, string sql, int revision = 0)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$r", revision);
        cmd.ExecuteNonQuery();
    }

    private static void AddPosition(SqliteCommand cmd, string name, string packId, CampaignPosition position, DateTimeOffset now)
    {
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$p", packId);
        cmd.Parameters.AddWithValue("$l", position.Leg);
        cmd.Parameters.AddWithValue("$z", (object?)position.Zone ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$a", position.Act);
        cmd.Parameters.AddWithValue("$t", now.ToString("O", CultureInfo.InvariantCulture));
    }

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

/// <summary>A guide character's saved build link and the ascendancies they have done.</summary>
public sealed record GuideBuildRow(string? Link, int Ascendancies);

/// <summary>One saved revision of a character's build plan: the notes as typed and the steps as they
/// were read when saved, so a later parser can never change a saved plan.</summary>
public sealed record GuidePlanRow(int Revision, string Status, string Notes, string Milestones, DateTimeOffset CreatedAt)
{
    public const string Active = "active";
    public const string Pending = "pending";

    public bool IsPending => Status == Pending;
}
