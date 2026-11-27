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

    /// <summary>The last route pack that passed its checks, so a broken pack in an update cannot take
    /// the route away.</summary>
    public string RouteCache => Path.Combine(DataDir, "route-cache");
}

/// <summary>Clock for the demo profile: starts at a fixed instant so every demo run shows the same ages.</summary>
public sealed class DemoTimeProvider(DateTimeOffset start) : TimeProvider
{
    public static readonly DateTimeOffset Start = new(2026, 11, 27, 11, 30, 0, TimeSpan.Zero);

    private readonly long _t0 = Stopwatch.GetTimestamp();

    public override DateTimeOffset GetUtcNow() => start + Stopwatch.GetElapsedTime(_t0);
}
