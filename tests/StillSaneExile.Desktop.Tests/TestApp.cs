using System.IO;
using Microsoft.Data.Sqlite;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Desktop.ViewModels;

namespace StillSaneExile.Desktop.Tests;

/// <summary>A folder of its own under %TEMP% for one test, deleted with everything in it (the SQLite
/// database included) on Dispose. Nothing a test writes lands in the real user profile.</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Root = Path.Combine(Path.GetTempPath(), "sse-desktop-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string Sub(string name) => Path.Combine(Root, name);

    public void Dispose()
    {
        // Microsoft.Data.Sqlite pools connections, and a pooled connection keeps the database file open.
        SqliteConnection.ClearAllPools();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Delete(Root, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(50); // a virus scanner can hold a fresh file for a moment
            }
        }
    }
}

/// <summary>The app's object graph as <c>App.OnStartup</c> builds it, minus the windows and hooks, with
/// its data in a temp folder and a fake clipboard. Build it on an STA thread (<see cref="Sta"/>).</summary>
internal sealed class TestApp : IDisposable
{
    private readonly TempDir _dir = new();

    /// <param name="configure">Runs on the fresh settings before the shell is built. The player has
    /// finished onboarding unless it says otherwise, so the app opens on the Price check page.</param>
    public TestApp(Action<Settings>? configure = null)
    {
        try
        {
            Paths = new AppPaths(dataOverride: _dir.Sub("data"));
            Store = new Store(Paths.Database);
            Settings = new Settings(Store) { Onboarded = true };
            configure?.Invoke(Settings);
            Shell = new ShellViewModel(Paths, Store, Settings, Clipboard, new UpdateService());
        }
        catch
        {
            _dir.Dispose(); // nobody else can: the caller never gets this object
            throw;
        }
    }

    public AppPaths Paths { get; }
    public Store Store { get; }
    public Settings Settings { get; }
    public FakeClipboard Clipboard { get; } = new();
    public ShellViewModel Shell { get; }

    public string ItemText(string file) => File.ReadAllText(Path.Combine(Paths.Fixtures, "items", file));

    public void Dispose() => _dir.Dispose();
}
