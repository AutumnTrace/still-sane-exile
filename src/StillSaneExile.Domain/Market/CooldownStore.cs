using System.Text.Json;

namespace StillSaneExile.Domain.Market;

/// <summary>Everything about providers that must survive a restart. Times are absolute UTC instants, never
/// "seconds left", so a snapshot read back an hour later still means the same thing.</summary>
public sealed record GovernorSnapshot(
    int Version,
    DateTimeOffset SavedAt,
    IReadOnlyList<PolicySnapshot> Policies,
    IReadOnlyList<RouteSnapshot> Routes,
    IReadOnlyList<IntegrationSnapshot> Integrations)
{
    public const int CurrentVersion = 1;
}

/// <summary>One shared policy bucket of one integration, and the instant its restriction ends (with jitter).</summary>
public sealed record PolicySnapshot(string Integration, string Policy, DateTimeOffset? RestrictedUntil, IReadOnlyList<RuleSnapshot> Rules);

public sealed record RuleSnapshot(string Name, IReadOnlyList<WindowSnapshot> Windows);

/// <summary>A window's limits and the send times of the hits still inside it.</summary>
public sealed record WindowSnapshot(int MaxHits, int PeriodSeconds, int PenaltySeconds, IReadOnlyList<DateTimeOffset> Hits);

/// <summary>Which policy a route last reported. Routes not listed are in conservative mode.</summary>
public sealed record RouteSnapshot(string Integration, string Route, string Policy);

/// <summary>Per-integration state: disabled after 401/403, the 4xx circuit, 5xx backoff, a whole-integration
/// pause after a 429 without a readable policy, and the last conservative send.</summary>
public sealed record IntegrationSnapshot(
    string Name,
    int? DisabledStatus,
    DateTimeOffset? DisabledAt,
    int ClientErrors,
    int? LastClientError,
    int CircuitTrips,
    DateTimeOffset? CircuitOpenUntil,
    int ServerErrors,
    int? LastServerError,
    DateTimeOffset? BackoffUntil,
    DateTimeOffset? PausedUntil,
    DateTimeOffset? LastConservativeSend);

/// <summary>Where <see cref="ProviderGovernor"/> keeps its state between runs. The domain has no database,
/// so the app picks the place.</summary>
public interface ICooldownStore
{
    /// <summary>The last saved state, or null when there is none or it cannot be read.</summary>
    GovernorSnapshot? Load();

    void Save(GovernorSnapshot snapshot);
}

/// <summary>Keeps the snapshot in one JSON file, replaced whole on each save so a crash mid-write leaves the
/// previous copy. Best effort: a file that cannot be read loads as nothing (every route starts in
/// conservative mode, and the next 429 teaches any cooldown again), and a failed save keeps the in-memory
/// state in charge.</summary>
public sealed class JsonFileCooldownStore(string filePath) : ICooldownStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public string FilePath { get; } = filePath;

    public GovernorSnapshot? Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<GovernorSnapshot>(File.ReadAllText(FilePath), Options) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    public void Save(GovernorSnapshot snapshot)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(FilePath));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(snapshot, Options));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Persistence is best effort; the governor in memory still holds every request.
        }
    }
}
