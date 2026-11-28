using System.Diagnostics;
using System.IO;

namespace StillSaneExile.Desktop.Infrastructure;

/// <summary>Where the app keeps its files. Program files live in the installer's folder and are
/// replaced on every update; settings and comparables never live there.</summary>
public sealed class AppPaths
{
    /// <param name="dataOverride"><c>--data &lt;dir&gt;</c>, for tests and clean starts.</param>
    /// <param name="portableRoot">Root of a portable copy (the folder holding <c>current\</c>), if any.</param>
    /// <param name="appDataRoot">Stands in for <c>%APPDATA%</c>, so tests never create folders in the real profile.</param>
    public AppPaths(string? dataOverride = null, string? portableRoot = null, string? appDataRoot = null)
    {
        AppDir = AppContext.BaseDirectory;
        Portable = dataOverride is null && portableRoot is not null;
        DataDir = dataOverride
            ?? (portableRoot is not null
                ? Path.Combine(portableRoot, "data")
                : Path.Combine(appDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StillSaneExile"));
        Directory.CreateDirectory(DataDir);
    }

    public string AppDir { get; }
    public string DataDir { get; }
    public bool Portable { get; }
    public string Database => Path.Combine(DataDir, "sse.db");
    public string Fixtures => Path.Combine(AppDir, "fixtures");

    /// <summary>Route packs that ship with the app. Replaced on every update like the program files.</summary>
    public string RoutePacks => Path.Combine(AppDir, "data", "routes");

    /// <summary>The trade site's filter ids (GGG's public trade metadata) that trade site links are built from.</summary>
    public string TradeData => Path.Combine(AppDir, "data", "trade", "trade-data.json");

    /// <summary>Path of Exile 2's passive tree, trimmed from GGG's official export, that pasted builds are drawn on.</summary>
    public string PassiveTree => Path.Combine(AppDir, "data", "tree", "poe2-tree.json");

    /// <summary>The passive tree's pictures (GGG's export sheets as JPEG and PNG) and their atlas.</summary>
    public string PassiveTreeArt => Path.Combine(AppDir, "data", "tree", "art");

    /// <summary>The last route pack that passed its checks, so a broken pack in an update cannot take
    /// the route away.</summary>
    public string RouteCache => Path.Combine(DataDir, "route-cache");

    /// <summary>Live price snapshots from poe.ninja, one folder per league, the newest few kept.</summary>
    public string Prices => Path.Combine(DataDir, "prices");

    /// <summary>The market client's cooldowns and holds, so a restart keeps waiting out a provider's hold.</summary>
    public string ProviderState => Path.Combine(DataDir, "provider-cooldowns.json");

    /// <summary>Stash pictures the player collected (Settings, "Collect stash pictures"): one folder per
    /// day, each picture a PNG with a JSON file of notes beside it.</summary>
    public string Captures => Path.Combine(DataDir, "captures");

    /// <summary>Which poe.ninja categories a refresh asks for. Ships with the app like the route packs.</summary>
    public string PoeNinjaCategories => Path.Combine(AppDir, "data", "poe-ninja.json");
}

/// <summary>Clock for the demo profile: starts at a fixed instant so every demo run shows the same ages.</summary>
public sealed class DemoTimeProvider(DateTimeOffset start) : TimeProvider
{
    public static readonly DateTimeOffset Start = new(2026, 11, 27, 11, 30, 0, TimeSpan.Zero);

    private readonly long _t0 = Stopwatch.GetTimestamp();

    public override DateTimeOffset GetUtcNow() => start + Stopwatch.GetElapsedTime(_t0);
}
