using System.Diagnostics;
using System.IO;

namespace StillSaneExile.Desktop.Infrastructure;

/// <summary>Where the app keeps its files. A file named <c>portable.txt</c> next to the executable
/// switches to a <c>data</c> folder beside it (portable build).</summary>
public sealed class AppPaths
{
    public AppPaths(string? dataOverride = null)
    {
        AppDir = AppContext.BaseDirectory;
        Portable = File.Exists(Path.Combine(AppDir, "portable.txt"));
        DataDir = dataOverride
            ?? (Portable ? Path.Combine(AppDir, "data") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StillSaneExile"));
        Directory.CreateDirectory(DataDir);
    }

    public string AppDir { get; }
    public string DataDir { get; }
    public bool Portable { get; }
    public string Database => Path.Combine(DataDir, "sse.db");
    public string Fixtures => Path.Combine(AppDir, "fixtures");
}

/// <summary>Clock for the demo profile: starts at a fixed instant so every demo run shows the same ages.</summary>
public sealed class DemoTimeProvider(DateTimeOffset start) : TimeProvider
{
    public static readonly DateTimeOffset Start = new(2026, 11, 27, 11, 30, 0, TimeSpan.Zero);

    private readonly long _t0 = Stopwatch.GetTimestamp();

    public override DateTimeOffset GetUtcNow() => start + Stopwatch.GetElapsedTime(_t0);
}
