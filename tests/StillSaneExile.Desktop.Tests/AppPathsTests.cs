using System.IO;
using StillSaneExile.Desktop.Infrastructure;

namespace StillSaneExile.Desktop.Tests;

public class AppPathsTests
{
    [Fact]
    public void The_data_flag_wins_over_a_portable_copy_and_appdata()
    {
        using var dir = new TempDir();
        var paths = new AppPaths(dir.Sub("flag"), portableRoot: dir.Sub("portable"), appDataRoot: dir.Sub("appdata"));

        Assert.Equal(dir.Sub("flag"), paths.DataDir);
        Assert.False(paths.Portable);
        Assert.Equal(Path.Combine(dir.Sub("flag"), "sse.db"), paths.Database);
        Assert.True(Directory.Exists(dir.Sub("flag")));
        Assert.False(Directory.Exists(dir.Sub("portable")));
        Assert.False(Directory.Exists(dir.Sub("appdata")));
    }

    [Fact]
    public void A_portable_copy_keeps_its_data_beside_itself()
    {
        using var dir = new TempDir();
        var paths = new AppPaths(dataOverride: null, portableRoot: dir.Sub("portable"), appDataRoot: dir.Sub("appdata"));

        Assert.Equal(Path.Combine(dir.Sub("portable"), "data"), paths.DataDir);
        Assert.True(paths.Portable);
        Assert.True(Directory.Exists(paths.DataDir));
        Assert.False(Directory.Exists(dir.Sub("appdata")));
    }

    [Fact]
    public void Otherwise_data_goes_to_appdata()
    {
        using var dir = new TempDir();
        var paths = new AppPaths(dataOverride: null, portableRoot: null, appDataRoot: dir.Sub("appdata"));

        Assert.Equal(Path.Combine(dir.Sub("appdata"), "StillSaneExile"), paths.DataDir);
        Assert.False(paths.Portable);
        Assert.True(Directory.Exists(paths.DataDir));
    }

    [Fact]
    public void Fixtures_ship_next_to_the_app()
    {
        using var dir = new TempDir();
        var paths = new AppPaths(dir.Sub("data"));

        Assert.Equal(AppContext.BaseDirectory, paths.AppDir);
        Assert.True(File.Exists(Path.Combine(paths.Fixtures, "demo", "pricebook.json")));
        Assert.True(File.Exists(Path.Combine(paths.Fixtures, "items", "rare-crossbow-advanced.txt")));
    }
}
