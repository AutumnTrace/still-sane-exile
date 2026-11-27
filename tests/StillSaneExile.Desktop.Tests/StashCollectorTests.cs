using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Domain.Capture;

namespace StillSaneExile.Desktop.Tests;

/// <summary>"Collect stash pictures" with a fake capture: off takes nothing; on, one physical press with
/// the game in front saves exactly one PNG and its notes in the data folder, and nothing else does.</summary>
public class StashCollectorTests
{
    private static void On(Settings s) => s.CollectStashPictures = true;

    [Fact]
    public void Collecting_off_takes_no_picture_and_writes_nothing() => Sta.Run(() =>
    {
        using var rig = new StashRig();

        rig.Tap();
        rig.Tap();

        Assert.False(rig.Pictures.IsCollecting);
        Assert.Equal(0, rig.Source.Calls);
        Assert.Equal(0, rig.Source.Prepares);
        Assert.Empty(rig.Files());
        Assert.Empty(rig.Toast.Shown);
        Assert.False(Directory.Exists(rig.App.Paths.Captures));
    });

    [Fact]
    public void One_press_saves_exactly_one_png_and_its_notes() => Sta.Run(() =>
    {
        using var rig = new StashRig(On);

        rig.Press();
        for (var i = 0; i < 30; i++)
        {
            rig.Press(); // Windows auto-repeat while the key is held
        }

        rig.Release();
        rig.Settle();

        Assert.Equal(1, rig.Source.Calls);
        Assert.Equal(FakeForeground.GameWindow, Assert.Single(rig.Source.Windows));
        Assert.All(rig.Source.Handed, f => Assert.True(f.IsDisposed));
        Assert.Equal(30, rig.Collector.Reader.GateCounters.AutoRepeatIgnored);
        Assert.Equal(1, rig.Collector.Reader.Counters.Accepted);

        var files = rig.Files();
        Assert.Equal(2, files.Count);
        var png = Assert.Single(files, f => f.EndsWith(".png", StringComparison.Ordinal));
        var json = Assert.Single(files, f => f.EndsWith(".json", StringComparison.Ordinal));
        Assert.Equal(Path.ChangeExtension(png, ".json"), json);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}[\\/]\d{2}-\d{2}-\d{2}\.png$", png);

        var decoder = new PngBitmapDecoder(new Uri(Path.Combine(rig.App.Paths.Captures, png)), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var bitmap = decoder.Frames[0];
        Assert.Equal((64, 36), (bitmap.PixelWidth, bitmap.PixelHeight));
        var pixel = new byte[4];
        new FormatConvertedBitmap(bitmap, System.Windows.Media.PixelFormats.Bgr32, null, 0).CopyPixels(new System.Windows.Int32Rect(1, 0, 1, 1), pixel, 4, 0);
        Assert.Equal(new byte[] { 1, 90, 160 }, pixel[..3]);

        using var notes = JsonDocument.Parse(File.ReadAllText(Path.Combine(rig.App.Paths.Captures, json)));
        var root = notes.RootElement;
        Assert.Equal("sse-stash-picture", root.GetProperty("kind").GetString());
        Assert.Equal(Path.GetFileName(png), root.GetProperty("file").GetString());
        Assert.Equal("9.9.9-test", root.GetProperty("sseVersion").GetString());
        Assert.True(root.GetProperty("capturedAt").GetDateTimeOffset() > DateTimeOffset.Now.AddMinutes(-5));
        Assert.Equal(64, root.GetProperty("picture").GetProperty("width").GetInt32());
        Assert.Equal(36, root.GetProperty("gameWindow").GetProperty("clientHeight").GetInt32());
        Assert.Equal("borderless fullscreen", root.GetProperty("gameWindow").GetProperty("mode").GetString());
        Assert.Equal(64, root.GetProperty("monitor").GetProperty("width").GetInt32());
        Assert.Equal(125, root.GetProperty("monitor").GetProperty("scalingPercent").GetInt32());
        Assert.False(root.GetProperty("monitor").GetProperty("hdr").GetBoolean());
        Assert.Equal("B8G8R8A8UIntNormalized", root.GetProperty("capture").GetProperty("pixelFormat").GetString());
        Assert.InRange(root.GetProperty("capture").GetProperty("pressToFrameMs").GetDouble(), 0, 60_000);
        Assert.Equal(9.5, root.GetProperty("capture").GetProperty("createToFrameMs").GetDouble());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("labels").GetProperty("tabType").ValueKind);
        Assert.False(root.GetProperty("labels").GetProperty("heldOut").GetBoolean());

        Assert.Equal("Saved stash picture 1", Assert.Single(rig.Toast.Shown));
        Assert.Equal(FakeForeground.GameWindow, rig.Toast.ShownOver);
        Assert.Single(rig.Pictures.Pictures);
    });

    [Fact]
    public void Every_fresh_press_saves_one_more_and_never_overwrites() => Sta.Run(() =>
    {
        using var rig = new StashRig(On);

        rig.Tap();
        rig.Tap();
        rig.Tap();

        Assert.Equal(3, rig.Source.Calls);
        Assert.Equal(3, rig.Files().Count(f => f.EndsWith(".png", StringComparison.Ordinal)));
        Assert.Equal(3, rig.Files().Count(f => f.EndsWith(".json", StringComparison.Ordinal)));
        Assert.Equal(["Saved stash picture 1", "Saved stash picture 2", "Saved stash picture 3"], rig.Toast.Shown);
        Assert.Equal(3, rig.Pictures.Pictures.Select(p => p.Name).Distinct().Count());
    });

    [Fact]
    public void Injected_input_and_another_window_in_front_take_no_picture() => Sta.Run(() =>
    {
        using var rig = new StashRig(On);

        rig.Press(injected: true);
        rig.Release(injected: true);
        rig.Settle();
        rig.Foreground.Current = ForegroundKind.Other;
        rig.Tap();
        rig.Foreground.Current = ForegroundKind.ThisApp; // SSE's own window: never captured
        rig.Tap();

        Assert.Equal(0, rig.Source.Calls);
        Assert.Empty(rig.Files());
        Assert.Equal(1, rig.Collector.Reader.GateCounters.InjectedIgnored);
        Assert.Equal(2, rig.Collector.Reader.GateCounters.NotForegroundIgnored);

        rig.Foreground.Current = ForegroundKind.Game;
        rig.Tap();
        Assert.Equal(1, rig.Source.Calls);
    });

    [Fact]
    public void Right_Alt_takes_nothing_where_it_is_AltGr_and_says_to_pick_another_key() => Sta.Run(() =>
    {
        using var rig = new StashRig(On);
        rig.Keys.AltGrDetected = true; // typing @ in the game's chat must not take pictures of it

        rig.Press();
        rig.Press(); // auto-repeat: no second note
        rig.Release();
        rig.Settle();

        Assert.Equal(0, rig.Source.Calls);
        Assert.Empty(rig.Files());
        Assert.Contains("AltGr", Assert.Single(rig.Toast.Shown), StringComparison.Ordinal);

        rig.App.Settings.StashKey = Hotkey.Parse("F8")!;
        rig.Pictures.ApplySettings();
        rig.Tap();

        Assert.Equal(1, rig.Source.Calls);
        Assert.Equal("Saved stash picture 1", rig.Toast.Shown[^1]);
    });

    [Fact]
    public void A_frame_that_comes_back_after_the_game_lost_the_front_is_not_saved() => Sta.Run(() =>
    {
        using var rig = new StashRig(On);
        rig.Source.HoldUntilReleased = true;

        rig.Press();
        Sta.Drain();
        Assert.True(rig.Source.Entered.Wait(TimeSpan.FromSeconds(10)));
        rig.Foreground.Switch(ForegroundKind.Other); // alt-tab while the frame is on its way
        rig.Source.Let();
        rig.Release();
        rig.Settle();

        Assert.Equal(1, rig.Source.Calls);
        Assert.Equal(1, rig.Collector.Reader.Counters.LateDropped);
        Assert.All(rig.Source.Handed, f => Assert.True(f.IsDisposed));
        Assert.Empty(rig.Files());
        Assert.Empty(rig.Pictures.Pictures);
        Assert.Empty(rig.Toast.Shown);
    });

    [Fact]
    public void A_quick_tap_keeps_its_picture_even_if_released_before_the_frame_is_in() => Sta.Run(() =>
    {
        using var rig = new StashRig(On);
        rig.Source.HoldUntilReleased = true;

        rig.Press();
        Sta.Drain();
        Assert.True(rig.Source.Entered.Wait(TimeSpan.FromSeconds(10)));
        rig.Release();
        rig.Source.Let();
        rig.Settle();

        Assert.Equal(2, rig.Files().Count);
        Assert.Equal("Saved stash picture 1", Assert.Single(rig.Toast.Shown));
    });

    [Fact]
    public void A_black_picture_or_no_picture_saves_nothing_and_says_why() => Sta.Run(() =>
    {
        using var rig = new StashRig(On);

        rig.Source.Black = true;
        rig.Tap();
        rig.Source.Black = false;
        rig.Source.ReturnNothing = true;
        rig.Tap();

        Assert.Empty(rig.Files());
        Assert.Equal(2, rig.Toast.Shown.Count);
        Assert.Contains("came back black", rig.Toast.Shown[0], StringComparison.Ordinal);
        Assert.Contains("try Windowed Fullscreen", rig.Toast.Shown[0], StringComparison.Ordinal);
        Assert.Equal("No stash picture: Windows gave no picture of the game within 500 ms.", rig.Toast.Shown[1]);
    });

    [Fact]
    public void Labels_round_trip_through_the_notes_file() => Sta.Run(() =>
    {
        using var rig = new StashRig(On);
        rig.Tap();
        var picture = Assert.Single(rig.Pictures.Pictures);
        var automatic = picture.Picture.Notes with { Labels = new StashPictureLabels() };

        picture.TabChoices.Single(c => c.Value == StashTabTypes.Currency).ChooseCommand.Execute(null);
        picture.Subtab = "General";
        picture.UiScale = "0,8";
        picture.Note = " tooltip over a stack ";
        picture.HeldOut = true;

        var stored = Assert.Single(new StashPictureStore(rig.App.Paths.Captures).Load());
        Assert.Equal(new StashPictureLabels("currency", "General", 0.8, "tooltip over a stack", true), stored.Notes.Labels);
        Assert.Equal(automatic, stored.Notes with { Labels = new StashPictureLabels() }); // the automatic notes are untouched
        Assert.Equal(1, rig.Pictures.Labelled);

        rig.Pictures.Reload();
        var again = Assert.Single(rig.Pictures.Pictures);
        Assert.Equal("currency", again.TabType);
        Assert.True(again.TabChoices.Single(c => c.Value == "currency").IsChosen);
        Assert.Equal(("General", "0.8", "tooltip over a stack", true), (again.Subtab, again.UiScale, again.Note, again.HeldOut));

        // Something that isn't a number is not saved, and the box says why; the other labels still are.
        again.UiScale = "big";
        again.TabChoices.Single(c => c.Value == StashTabTypes.Quad).ChooseCommand.Execute(null);
        Assert.NotNull(again.UiScaleProblem);
        var after = Assert.Single(new StashPictureStore(rig.App.Paths.Captures).Load()).Notes.Labels;
        Assert.Equal(new StashPictureLabels("quad", null, 0.8, "tooltip over a stack", true), after);
    });

    [Theory]
    [InlineData("100", 100.0)]
    [InlineData("100 %", 100.0)]
    [InlineData("0.8", 0.8)]
    [InlineData("0,8", 0.8)]
    public void The_ui_scale_reads_as_the_game_shows_it(string typed, double expected)
    {
        Assert.True(ViewModels.StashPictureViewModel.TryParseScale(typed, out var scale));
        Assert.Equal(expected, scale);
    }

    [Theory]
    [InlineData("big")]
    [InlineData("-1")]
    [InlineData("0")]
    public void A_ui_scale_that_is_not_a_positive_number_is_refused(string typed) =>
        Assert.False(ViewModels.StashPictureViewModel.TryParseScale(typed, out _));

    [Fact]
    public void Delete_removes_the_picture_and_its_notes() => Sta.Run(() =>
    {
        using var rig = new StashRig(On);
        rig.Tap();
        rig.Tap();
        var older = rig.Pictures.Pictures[^1];

        rig.Pictures.DeleteCommand.Execute(older);

        Assert.False(File.Exists(older.Picture.PngPath));
        Assert.False(File.Exists(older.Picture.NotesPath));
        Assert.Equal(2, rig.Files().Count);
        Assert.Single(rig.Pictures.Pictures);
        Assert.Contains("Deleted the picture from", rig.Pictures.Message, StringComparison.Ordinal);

        rig.Pictures.DeleteCommand.Execute(rig.Pictures.Pictures.Single());

        Assert.Empty(rig.Files());
        Assert.Empty(Directory.EnumerateFileSystemEntries(rig.App.Paths.Captures)); // the day's folder went too
        Assert.False(rig.Pictures.HasPictures);
    });

    [Fact]
    public void Only_the_data_folder_is_written() => Sta.Run(() =>
    {
        var appFiles = Directory.EnumerateFiles(AppContext.BaseDirectory, "*", SearchOption.AllDirectories).ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var rig = new StashRig(On);
        var root = Path.GetDirectoryName(rig.App.Paths.DataDir)!;
        var before = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToHashSet(StringComparer.OrdinalIgnoreCase);

        rig.Tap();
        rig.Tap();
        var picture = rig.Pictures.Pictures[0];
        picture.TabChoices[0].ChooseCommand.Execute(null);
        picture.Note = "labelled";
        rig.Pictures.DeleteCommand.Execute(rig.Pictures.Pictures[1]);

        var added = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(f => !before.Contains(f)).ToList();
        Assert.Equal(2, added.Count);
        Assert.All(added, f => Assert.StartsWith(rig.App.Paths.Captures + Path.DirectorySeparatorChar, f, StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith(rig.App.Paths.DataDir, rig.App.Paths.Captures, StringComparison.Ordinal);
        Assert.Equal(2, rig.Files().Count);
        Assert.DoesNotContain(rig.Files(), f => f.EndsWith(".tmp", StringComparison.Ordinal));
        Assert.DoesNotContain(Directory.EnumerateFiles(AppContext.BaseDirectory, "*", SearchOption.AllDirectories), f => !appFiles.Contains(f));
    });

    [Fact]
    public void Switching_collecting_on_readies_the_capture_and_off_releases_it() => Sta.Run(() =>
    {
        using var rig = new StashRig();
        var settings = rig.App.Shell.SettingsPage;
        Assert.Contains("It reads nothing now", settings.ReadsExplanation, StringComparison.Ordinal);
        Assert.Equal("coming later", settings.StashKeyNote);
        Assert.False(rig.Pictures.ShowCard);

        rig.Pictures.IsCollecting = true;
        Sta.Until(() => rig.Source.Prepares == 1, "the capture to be readied");

        Assert.True(rig.App.Settings.CollectStashPictures);
        Assert.Contains("\"Collect stash pictures\" is on", settings.ReadsExplanation, StringComparison.Ordinal);
        Assert.Equal("takes stash pictures (see Stash pictures)", settings.StashKeyNote);
        Assert.True(rig.Pictures.ShowCard);
        Assert.Equal(0, rig.Source.Calls); // readying captures nothing

        rig.Pictures.IsCollecting = false;
        Sta.Until(() => rig.Source.Releases == 1, "the capture to be released");
        Assert.False(rig.App.Settings.CollectStashPictures);
        rig.Tap();
        Assert.Equal(0, rig.Source.Calls);
    });

    [Fact]
    public void A_stash_key_on_a_side_button_is_observed_only_while_collecting() => Sta.Run(() =>
    {
        using var rig = new HudRig(s => s.StashKey = Hotkey.Parse("Mouse4")!);
        Assert.False(rig.Keys.ObserveMouseButtons);

        rig.Shell.StashPictures.IsCollecting = true;
        Assert.True(rig.Keys.ObserveMouseButtons);

        rig.Shell.StashPictures.IsCollecting = false;
        Assert.False(rig.Keys.ObserveMouseButtons);
    });

    [Fact]
    public void Pictures_without_readable_notes_are_still_listed() => Sta.Run(() =>
    {
        using var app = new TestApp();
        var day = Path.Combine(app.Paths.Captures, "2026-11-27");
        Directory.CreateDirectory(day);
        var saved = StashTestPictures.Save(app, new DateTimeOffset(2026, 11, 27, 14, 30, 5, TimeSpan.Zero));
        File.WriteAllText(saved.NotesPath, "{ not json");

        app.Shell.StashPictures.Reload();

        var p = Assert.Single(app.Shell.StashPictures.Pictures);
        Assert.Null(p.TabType);
        Assert.Equal("No notes were found for this picture.", p.Facts);
    });
}

/// <summary>Pictures written straight through the store, as the collector would have saved them.</summary>
internal static class StashTestPictures
{
    public static StashPicture Save(TestApp app, DateTimeOffset at)
    {
        var pixels = new byte[64 * 36 * 4];
        Array.Fill(pixels, (byte)120);
        using var frame = new Frame(64, 36, 64 * 4, FramePixelFormat.Bgra8, pixels, Stopwatch.GetTimestamp());
        using var copy = PictureCopy.Of(frame);
        var notes = StashPictureNotes.For(copy, new CaptureFacts("fake capture", FakeStashSource.Window, "B8G8R8A8UIntNormalized", 9.5), at, "9.9.9-test", 14);
        return app.Shell.StashPictures.Store.Save(copy, notes);
    }
}
