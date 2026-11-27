using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.ViewModels;
using StillSaneExile.Desktop.Views;
using StillSaneExile.Domain.Capture;

namespace StillSaneExile.Desktop.Services;

/// <summary><c>--render-snapshots &lt;dir&gt;</c>: renders the demo screens to PNG and exits. Used for
/// README screenshots and visual checks; run it with <c>--data &lt;empty dir&gt;</c> so it starts from a
/// clean demo profile. It installs no hooks and reads no clipboard. <c>--text-scale &lt;percent&gt;</c>
/// renders as if Windows' text size were set to it, and <c>--window &lt;width&gt;x&lt;height&gt;</c> sets the
/// main window's size, which also stands for the screen the scaled window must fit.</summary>
public static class SnapshotRenderer
{
    private static readonly Size UsualSize = new(1440, 1000);

    public static void Render(ShellViewModel shell, MainWindow main, string dir, double textScale = 1, Size? window = null)
    {
        Directory.CreateDirectory(dir);
        shell.Settings.Onboarded = false;
        shell.SettingsPage.Load();
        shell.Navigate(shell.SettingsPage);
        var size = window ?? new Size(UsualSize.Width * textScale, UsualSize.Height * textScale);
        main.UseTextScale(TextScale.Fixed(textScale), screen: size);
        main.Width = size.Width;
        main.Height = size.Height;
        main.Left = -20000;
        main.Top = 0;
        main.ShowActivated = false;
        main.WindowStartupLocation = WindowStartupLocation.Manual;
        main.Show();

        Save(main, Path.Combine(dir, "onboarding.png"));

        shell.Settings.Onboarded = true;
        shell.SettingsPage.Load();
        var practice = shell.Inspect.PracticeItems;
        shell.GoInspectCommand.Execute(null);
        foreach (var p in practice.Reverse())
        {
            shell.Inspect.LoadPracticeCommand.Execute(p);
        }

        Shot(shell, main, dir, "rare-crossbow-advanced.txt", "inspect-advanced");
        Shot(shell, main, dir, "rare-crossbow-normal-hybrid.txt", "inspect-hybrid");
        Shot(shell, main, dir, "unique-ring.txt", "inspect-unique");
        Shot(shell, main, dir, "currency-exalted.txt", "inspect-currency");

        shell.Inspect.Selected = shell.Inspect.Recent.First(r => r.Item.CaptureId.Length > 0 && r.Title == "Doom Thirst");
        shell.OpenCraftWith(shell.Inspect.Selected);
        Save(main, Path.Combine(dir, "craft.png"));
        SeedGuide(shell.Guide);
        shell.GoGuideCommand.Execute(null);
        Save(main, Path.Combine(dir, "guide.png"));
        SeedPlanChange(shell.Guide.Plan);
        Save(main, Path.Combine(dir, "guide-plan-change.png"));

        // Wealth with demo holdings: the practice file applied (idempotent, so a re-run adds nothing),
        // one count typed in, then a preview of the practice file with mistakes.
        var wealth = shell.Wealth;
        shell.GoWealthCommand.Execute(null);
        wealth.PreviewPracticeCommand.Execute("example.csv");
        wealth.ApplyImportCommand.Execute(null);
        wealth.NewTab = "Currency";
        wealth.NewItemId = "vaal";
        wealth.NewQuantity = "6";
        wealth.SaveManualCommand.Execute(null);
        wealth.NewTab = "";
        wealth.Message = null;
        Save(main, Path.Combine(dir, "wealth.png"));
        wealth.PreviewPracticeCommand.Execute("needs-fixing.csv");
        Save(main, Path.Combine(dir, "wealth-import.png"));
        wealth.CancelImportCommand.Execute(null);
        wealth.Message = null;

        shell.GoSettingsCommand.Execute(null);
        Save(main, Path.Combine(dir, "settings.png"));
        RenderStashPictures(shell, main, dir);

        // "What's new" as the first start after an update shows it, over the price check. A development
        // build never decides to show it, so it is opened directly.
        shell.GoInspectCommand.Execute(null);
        shell.WhatsNew.Show(since: null, afterUpdate: true);
        Save(main, Path.Combine(dir, "whats-new.png"));
        shell.WhatsNew.DismissCommand.Execute(null);

        var peek = new PeekWindow { Left = -20000, Top = 0 };
        foreach (var (title, name) in new[] { ("Doom Thirst", "peek-advanced"), ("Storm Mark", "peek-hybrid"), ("Sanity's Last Loop", "peek-unique"), ("Exalted Orb", "peek-currency") })
        {
            var item = shell.Inspect.Recent.First(r => r.Title == title);
            peek.DataContext = PeekViewModel.From(item, true, PeekViewModel.ReleaseHintFor(shell.Settings.PeekMode), shell.Settings.OpenLastKey.Label);
            peek.Show();
            Save(peek, Path.Combine(dir, $"{name}.png"));
        }

        // Pinned (the player moved onto it while holding the key): close button, live stats. Never
        // pinned through the window here, so it stays click-through and never activates.
        var pinned = PeekViewModel.From(shell.Inspect.Recent.First(r => r.Title == "Doom Thirst"), true, PeekViewModel.ReleaseHintFor(shell.Settings.PeekMode), shell.Settings.OpenLastKey.Label);
        pinned.IsPinned = true;
        peek.DataContext = pinned;
        Save(peek, Path.Combine(dir, "peek-pinned.png"));
        peek.Close();

        // The pinned guide lines, unpositioned; the click-through window never activates.
        var guideHud = new GuideHudWindow { Left = -20000, Top = 0, DataContext = shell.Guide };
        guideHud.Show();
        Save(guideHud, Path.Combine(dir, "guide-hud.png"));
        guideHud.Close();

        // The in-game price check window (F3), unpositioned and never activated.
        var priceCheck = new PriceCheckWindow { Left = -20000, Top = 0, ShowActivated = false };
        priceCheck.SetContentScale(main.ContentScale);
        priceCheck.DataContext = shell.Inspect.Recent.First(r => r.Title == "Doom Thirst");
        priceCheck.Show();
        Save(priceCheck, Path.Combine(dir, "price-check-window.png"));
        priceCheck.Close();
        main.Close();
    }

    /// <summary>"Collect stash pictures" switched on with three made-up pictures: its Settings card, the
    /// review list and the card on My wealth, and the note over the game. The made-up pictures are deleted
    /// and the switch put back afterwards; pictures already in the data folder are left alone.</summary>
    private static void RenderStashPictures(ShellViewModel shell, MainWindow main, string dir)
    {
        var pictures = shell.StashPictures;
        var wasCollecting = pictures.IsCollecting;
        var made = new List<string>();
        pictures.IsCollecting = true;
        try
        {
            var today = DateTimeOffset.Now.Date;
            var facts = new CaptureFacts("Windows.Graphics.Capture", new GameWindowFacts(1920, 1080, "borderless fullscreen", null, 1920, 1080, 100, false, 1.0), "B8G8R8A8UIntNormalized", 9.8);
            foreach (var (time, title, quad) in new[] { (new TimeSpan(14, 30, 5), "Currency", false), (new TimeSpan(14, 31, 12), "Quad stash", true), (new TimeSpan(14, 32, 40), "Stash", false) })
            {
                using var frame = MadeUpStash(title, quad);
                using var copy = PictureCopy.Of(frame);
                made.Add(pictures.Store.Save(copy, StashPictureNotes.For(copy, facts, new DateTimeOffset(today + time), shell.Updates.CurrentVersion, 13.4)).PngPath);
            }

            pictures.Reload();
            var byFile = pictures.Pictures.Where(p => made.Contains(p.Picture.PngPath)).OrderBy(p => p.Picture.PngPath, StringComparer.Ordinal).ToList();
            byFile[0].TabType = StashTabTypes.Currency;
            byFile[0].Subtab = "General";
            byFile[0].UiScale = "100";
            byFile[1].TabType = StashTabTypes.Quad;
            byFile[1].UiScale = "100";
            byFile[1].Note = "Tooltip over a stack";
            byFile[1].HeldOut = true;
            foreach (var p in pictures.Pictures)
            {
                p.LoadThumbnailNow();
            }

            shell.GoSettingsCommand.Execute(null);
            Flush(main);
            Find<FrameworkElement>(main, "StashPicturesCard")?.BringIntoView();
            Save(main, Path.Combine(dir, "settings-stash-pictures.png"));

            shell.GoWealthCommand.Execute(null);
            Save(main, Path.Combine(dir, "wealth-stash-card.png"));
            pictures.ReviewCommand.Execute(null);
            Save(main, Path.Combine(dir, "wealth-stash-pictures.png"));
            pictures.DoneReviewingCommand.Execute(null);

            // The note over the game, unpositioned; the click-through window never activates.
            var toast = new StashToastWindow { Left = -20000, Top = 0, Message = $"Saved stash picture {pictures.Count}" };
            ((Window)toast).Show();
            Save(toast, Path.Combine(dir, "stash-toast.png"));
            toast.Close();
        }
        finally
        {
            foreach (var p in pictures.Pictures.Where(p => made.Contains(p.Picture.PngPath)).ToList())
            {
                pictures.DeleteCommand.Execute(p);
            }

            pictures.Message = null;
            pictures.IsCollecting = wasCollecting;
        }
    }

    /// <summary>A made-up stash for the snapshots, drawn at 1920×1080: a tab of square cells with a few
    /// items and stack counts, the rest of the screen dark. Not a picture of the game.</summary>
    private static Frame MadeUpStash(string title, bool quad)
    {
        const int W = 1920, H = 1080;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x24, 0x1C, 0x16), Color.FromRgb(0x0C, 0x0A, 0x09), 90), null, new Rect(0, 0, W, H));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x14, 0x11, 0x0E)), new Pen(new SolidColorBrush(Color.FromRgb(0x6B, 0x55, 0x33)), 3), new Rect(40, 110, 880, 900));
            var face = new Typeface("Segoe UI");
            var gold = new SolidColorBrush(Color.FromRgb(0xC8, 0xB0, 0x7A));
            dc.DrawText(new FormattedText(title, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 30, gold, 1.0), new Point(70, 125));
            var cells = quad ? 24 : 12;
            var size = 820.0 / cells;
            var line = new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x24, 0x1E)), 1);
            var colours = new[] { Color.FromRgb(0xC8, 0xBF, 0xA6), Color.FromRgb(0x9A, 0x9A, 0xFF), Color.FromRgb(0xD2, 0x80, 0x3F), Color.FromRgb(0x1B, 0xA2, 0x9B) };
            var random = new Random(title.Length * 7919);
            for (var y = 0; y < cells; y++)
            {
                for (var x = 0; x < cells; x++)
                {
                    var cell = new Rect(70 + x * size, 175 + y * size, size, size);
                    dc.DrawRectangle(null, line, cell);
                    if (random.NextDouble() < 0.28)
                    {
                        var c = colours[random.Next(colours.Length)];
                        var inner = cell;
                        inner.Inflate(-size * 0.18, -size * 0.18);
                        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0xE0, c.R, c.G, c.B)), null, inner, 4, 4);
                        if (!quad)
                        {
                            dc.DrawText(new FormattedText(random.Next(1, 1200).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 15, Brushes.White, 1.0), new Point(cell.X + 4, cell.Y + 2));
                        }
                    }
                }
            }

            dc.DrawText(new FormattedText("Made up for SSE's screenshots", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 22, gold, 1.0), new Point(1260, 1020));
        }

        var bitmap = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[W * H * 4];
        bitmap.CopyPixels(pixels, W * 4, 0);
        return new Frame(W, H, W * 4, FramePixelFormat.Bgra8, pixels, System.Diagnostics.Stopwatch.GetTimestamp());
    }

    private static T? Find<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && match.Name == name)
            {
                return match;
            }

            if (Find<T>(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Example build notes, written for the snapshots in a player's own words.</summary>
    private const string ExampleNotes = """
        Act 1
        cut Entangle
        upgrade Entangle at Mud Burrow
        cut Contagion (level 3 uncut)
        cut support Zenith I (priority: high)
        socket Zenith I into Entangle
        Act 2
        cut Essence Drain
        move Zenith I: Entangle → Essence Drain
        After ascendancy
        respec the passive tree
        """;

    /// <summary>A practice character partway into the route, the way a player gets there: the first
    /// boss ticked, the optional one skipped, now in the Mud Burrow, with a saved guide link and their
    /// own notes saved as a plan and the first cut ticked.</summary>
    private static void SeedGuide(GuideViewModel guide)
    {
        guide.NewCharacterName = "Practice exile";
        guide.AddCharacterCommand.Execute(null);
        if (!guide.HasCharacter)
        {
            return;
        }

        guide.ToggleDoneCommand.Execute(guide.Steps.FirstOrDefault());
        guide.NextCommand.Execute(null);
        guide.NextCommand.Execute(null);
        guide.SkipCommand.Execute(guide.Steps.FirstOrDefault());
        guide.NextCommand.Execute(null);

        var plan = guide.Plan;
        plan.LinkDraft = "https://mobalytics.gg/poe-2/builds";
        plan.SaveLinkCommand.Execute(null);
        plan.NotesDraft = ExampleNotes;
        plan.SavePlanCommand.Execute(null);
        plan.ToggleStepCommand.Execute(plan.Checklist.FirstOrDefault());
        plan.Message = null;
    }

    /// <summary>The same notes edited into a second revision that waits for the player's say-so.</summary>
    private static void SeedPlanChange(BuildPlanViewModel plan)
    {
        plan.EditNotesCommand.Execute(null);
        plan.NotesDraft = ExampleNotes
            .Replace("(level 3 uncut)", "(level 5 uncut)", StringComparison.Ordinal)
            .Replace("respec the passive tree", "upgrade Essence Drain", StringComparison.Ordinal);
        plan.SavePlanCommand.Execute(null);
    }

    private static void Shot(ShellViewModel shell, MainWindow main, string dir, string file, string name)
    {
        var title = file switch
        {
            "rare-crossbow-advanced.txt" => "Doom Thirst",
            "rare-crossbow-normal-hybrid.txt" => "Storm Mark",
            "unique-ring.txt" => "Sanity's Last Loop",
            _ => "Exalted Orb",
        };
        shell.Inspect.Selected = shell.Inspect.Recent.First(r => r.Title == title);
        Save(main, Path.Combine(dir, $"{name}.png"));
    }

    /// <summary>"150" → 1.5; nothing → 1.</summary>
    public static double ParseScale(string? percent) =>
        int.TryParse(percent, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p / 100.0 : 1;

    /// <summary>"1920x1032" → that size; nothing or anything else → none.</summary>
    public static Size? ParseSize(string? size) =>
        size?.Split('x') is [var w, var h]
        && double.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
        && double.TryParse(h, NumberStyles.Float, CultureInfo.InvariantCulture, out var height)
            ? new Size(width, height)
            : null;

    private static void Save(Window window, string path)
    {
        Flush(window);
        var content = (FrameworkElement)window.Content;

        // A text size scales the content with a layout transform; ActualWidth is the size before it.
        var bounds = content.LayoutTransform.TransformBounds(new Rect(content.RenderSize));
        var w = (int)Math.Ceiling(bounds.Width);
        var h = (int)Math.Ceiling(bounds.Height);
        var bmp = new RenderTargetBitmap(Math.Max(w, 1), Math.Max(h, 1), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var background = window.Background is SolidColorBrush { Color.A: > 0 } ? window.Background : (Brush)Application.Current.Resources["Surface"];
            dc.DrawRectangle(background, null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, w, h));
        }

        bmp.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, () => { });
        window.UpdateLayout();
    }
}
