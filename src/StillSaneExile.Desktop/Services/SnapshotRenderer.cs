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
/// clean demo profile. It installs no hooks and reads no clipboard.</summary>
public static class SnapshotRenderer
{
    public static void Render(ShellViewModel shell, MainWindow main, string dir)
    {
        Directory.CreateDirectory(dir);
        shell.Settings.Onboarded = false;
        shell.SettingsPage.Load();
        shell.Navigate(shell.SettingsPage);
        main.Width = 1440;
        main.Height = 1000;
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
        shell.GoGuideCommand.Execute(null);
        Save(main, Path.Combine(dir, "guide.png"));
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
            peek.DataContext = PeekViewModel.From(item, true, PeekViewModel.ReleaseHintFor(shell.Settings.PeekMode), shell.Settings.OpenLastKey.ToString());
            peek.Show();
            Save(peek, Path.Combine(dir, $"{name}.png"));
        }

        peek.Close();

        // The in-game price check window (F3), unpositioned and never activated.
        var priceCheck = new PriceCheckWindow { Left = -20000, Top = 0, ShowActivated = false };
        priceCheck.DataContext = shell.Inspect.Recent.First(r => r.Title == "Doom Thirst");
        priceCheck.Show();
        Save(priceCheck, Path.Combine(dir, "price-check-window.png"));
        priceCheck.Close();
        main.Close();
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

    private static void Save(Window window, string path)
    {
        Flush(window);
        var content = (FrameworkElement)window.Content;
        var w = (int)Math.Ceiling(content.ActualWidth);
        var h = (int)Math.Ceiling(content.ActualHeight);
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
