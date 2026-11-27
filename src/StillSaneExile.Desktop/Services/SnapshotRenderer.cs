using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StillSaneExile.Desktop.ViewModels;
using StillSaneExile.Desktop.Views;

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
