using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StillSaneExile.Desktop.Views;

namespace StillSaneExile.Desktop.Tests;

/// <summary>Keyboard and screen reader checks on the real views, styled by App.xaml: Tab reaches every
/// control in reading order and nothing else, each one shows that it has focus, and each one has a name
/// a screen reader can say.</summary>
public class AccessibilityTests
{
    private const string Crossbow = "rare-crossbow-advanced.txt";

    [Fact]
    public void The_price_check_page_works_from_the_keyboard() => UiThread.Run(() =>
    {
        using var app = new TestApp();
        var inspect = app.Shell.Inspect;
        foreach (var p in inspect.PracticeItems.Reverse())
        {
            inspect.LoadPracticeCommand.Execute(p);
        }

        inspect.Selected = inspect.Recent.First(r => r.Title == "Doom Thirst");
        inspect.ToggleAddFormCommand.Execute(null);

        var stops = Check(new InspectView { DataContext = inspect });

        Assert.Equal("Paste an item", stops[0]);
        Assert.Contains("Doom Thirst, Rare crossbow, checked at", string.Join("\n", stops));
        Assert.Contains("Lower the minimum for Physical DPS 355.3", stops);
        Assert.Contains("Minimum for Physical DPS 355.3", stops);
        Assert.Contains("Raise the minimum for Physical DPS 355.3", stops);
        Assert.Contains("Asking price", stops);
        Assert.Contains("Divine Orb", stops); // the asking price's currency, shown as "div"
        Assert.True(stops.IndexOf("Reset to my item") < stops.IndexOf("Open this search on the trade site"));
        Assert.True(stops.IndexOf("Open this search on the trade site") < stops.IndexOf("Asking price"));
    });

    [Fact]
    public void The_empty_price_check_page_has_no_stop_without_a_control() => UiThread.Run(() =>
    {
        using var app = new TestApp();

        var stops = Check(new InspectView { DataContext = app.Shell.Inspect });

        Assert.Contains("Paste an item", stops);
    });

    [Fact]
    public void The_price_check_over_the_game_works_from_the_keyboard() => UiThread.Run(() =>
    {
        using var app = new TestApp();
        var item = app.Shell.Inspect.AddFromText(app.ItemText(Crossbow), "test")!;
        var window = new PriceCheckWindow { DataContext = item, Left = -20000, Top = 0 };

        var stops = Check(window);

        Assert.Equal("Close the price check", stops[0]);
        Assert.Contains("Copy search", stops);
    });

    [Fact]
    public void Settings_work_from_the_keyboard() => UiThread.Run(() =>
    {
        using var app = new TestApp();

        var stops = Check(new SettingsView { DataContext = app.Shell.SettingsPage });

        Assert.Contains("Live league name", stops);
        Assert.Contains("Change the quick price key", stops);
        Assert.Contains("Quick price card scale", stops);
        Assert.Contains("Days until prices you write down expire", stops);
        Assert.True(stops.IndexOf("Practice with demo data") < stops.IndexOf("Divine Orb"));
    });

    [Fact]
    public void Onboarding_works_from_the_keyboard() => UiThread.Run(() =>
    {
        using var app = new TestApp(s => s.Onboarded = false);
        app.Shell.SettingsPage.Load();

        var stops = Check(new SettingsView { DataContext = app.Shell.SettingsPage });

        Assert.Equal("Practice with demo data", stops[0]);
        Assert.Equal("Skip for now", stops[^1]);
    });

    [Fact]
    public void My_wealth_works_from_the_keyboard() => UiThread.Run(() =>
    {
        using var app = new TestApp();
        var wealth = app.Shell.Wealth;
        wealth.PreviewPracticeCommand.Execute("example.csv");
        wealth.ApplyImportCommand.Execute(null);

        var stops = Check(new WealthView { DataContext = wealth });

        Assert.Contains(stops, s => s.Contains(", tab Currency, as of ", StringComparison.Ordinal));
        Assert.Contains("Tab", stops);

        wealth.PreviewPracticeCommand.Execute("needs-fixing.csv");
        var preview = Check(new WealthView { DataContext = wealth });

        Assert.Contains("Cancel", preview);
    });

    [Fact]
    public void The_main_window_header_reads_left_to_right() => UiThread.Run(() =>
    {
        using var app = new TestApp();
        app.Shell.Navigate(app.Shell.Wealth);
        app.Shell.WhatsNew.Show(since: null, afterUpdate: true);
        var window = new MainWindow { DataContext = app.Shell, Left = -20000, Top = 0, Width = 1440, Height = 1000 };

        var stops = Check(window);

        Assert.Equal(new[] { "Back", "Price check", "Crafting later", "My wealth" }, stops[..4]);
        var league = stops.FindIndex(s => s.StartsWith("League: ", StringComparison.Ordinal));
        var currency = stops.FindIndex(s => s.StartsWith("Show prices in: ", StringComparison.Ordinal));
        var settings = stops.IndexOf("Settings");
        Assert.True(league < currency && currency < settings, string.Join(" | ", stops));
        Assert.True(settings < stops.IndexOf("Got it"));
    });

    [Fact]
    public void The_currency_menu_works_from_the_keyboard() => UiThread.Run(() =>
    {
        using var app = new TestApp();
        var window = new MainWindow { DataContext = app.Shell, Left = -20000, Top = 0, Width = 1440, Height = 1000 };
        window.Show();
        try
        {
            window.Activate();
            Flush(window);
            var opener = (Button)window.FindName("CurrencyButton");
            opener.Focus();

            Press(Key.Enter);

            Assert.True(app.Shell.CurrencyMenuOpen);
            var current = Assert.IsType<Button>(Keyboard.FocusedElement);
            Assert.Equal("Divine Orb", SpokenName(current));
            Assert.Equal("Selected", Peer(current).GetItemStatus());

            Press(Key.Down);

            Assert.Equal("Chaos Orb", SpokenName((DependencyObject)Keyboard.FocusedElement));

            Press(Key.Escape);

            Assert.False(app.Shell.CurrencyMenuOpen);
            Assert.Same(opener, Keyboard.FocusedElement);

            Press(Key.Enter);
            Press(Key.Up);
            Press(Key.Enter);

            Assert.False(app.Shell.CurrencyMenuOpen);
            Assert.Equal("exalted", app.Shell.Session.DisplayCurrency);
            Assert.Same(opener, Keyboard.FocusedElement);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void The_chosen_one_of_a_few_is_announced_as_selected() => UiThread.Run(() =>
    {
        using var app = new TestApp();
        var window = Show(new SettingsView { DataContext = app.Shell.SettingsPage });
        try
        {
            var currencies = Descendants<Button>(window).Where(b => b.Content is "Exalted Orb" or "Divine Orb" or "Chaos Orb").ToList();

            Assert.Equal(3, currencies.Count);
            var selected = Assert.Single(currencies, b => Equals(b.Tag, "active"));
            Assert.Equal("Selected", Peer(selected).GetItemStatus());
            Assert.All(currencies.Where(b => b != selected), b => Assert.Equal("", Peer(b).GetItemStatus()));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>Walks the Tab order of <paramref name="view"/> in a window of the app's size and checks
    /// every stop; returns the stops' spoken names in order.</summary>
    private static List<string> Check(FrameworkElement view) => Check(Show(view));

    private static List<string> Check(Window window)
    {
        if (!window.IsVisible)
        {
            window.Show();
        }

        try
        {
            window.Activate();
            Flush(window);
            AssertNoTypeNames(window);
            var stops = TabOrder(window);
            Assert.NotEmpty(stops);
            var names = new List<string>();
            foreach (var stop in stops)
            {
                var name = SpokenName(stop);
                Assert.True(Sayable(name), $"{Describe(stop)} has no name a screen reader can say: '{name}'");
                Assert.DoesNotContain("StillSaneExile.", name, StringComparison.Ordinal);
                Assert.True(stop is ButtonBase or TextBoxBase or Slider or ListBoxItem or Hyperlink, $"{Describe(stop)} is a Tab stop but not a control");
                if (stop is UIElement element)
                {
                    AssertFocusShows(window, element, name);
                }
                else
                {
                    Assert.Same(Application.Current.Resources["FocusRing"], ((FrameworkContentElement)stop).FocusVisualStyle);
                }

                if (stop is Button { Tag: "active" } chosen)
                {
                    Assert.Equal("Selected", Peer(chosen).GetItemStatus());
                }

                names.Add(name);
            }

            var buttons = stops.OfType<Button>().Select(SpokenName).ToList();
            Assert.True(buttons.Count == buttons.Distinct().Count(), "Buttons that say the same: " + string.Join(", ", buttons.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key)));
            return names;
        }
        finally
        {
            window.Close();
        }
    }

    private static Window Show(FrameworkElement view)
    {
        var window = new Window
        {
            Content = view,
            Width = 1440,
            Height = 1000,
            Left = -20000,
            Top = 0,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Background = (Brush)Application.Current.Resources["Surface"],
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
        };
        window.Show();
        return window;
    }

    /// <summary>Everything a screen reader can reach, controls or not, reads as words: WPF names a row
    /// of a list after its data object, which by default is the type's name.</summary>
    private static void AssertNoTypeNames(Window window)
    {
        var pending = new Stack<AutomationPeer>([Peer(window)]);
        while (pending.TryPop(out var peer))
        {
            var name = peer.GetName();
            Assert.False(name.Contains("StillSaneExile.", StringComparison.Ordinal) || name.Contains(" { ", StringComparison.Ordinal), $"A {peer.GetAutomationControlType()} reads as a type name: '{name}'");
            foreach (var child in peer.GetChildren() ?? [])
            {
                pending.Push(child);
            }
        }
    }

    /// <summary>The order Tab visits, from the first stop until it comes round again.</summary>
    private static List<DependencyObject> TabOrder(Window window)
    {
        var order = new List<DependencyObject>();
        window.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        for (var i = 0; i < 200; i++)
        {
            Flush(window);
            if (Keyboard.FocusedElement is not DependencyObject focused || order.Contains(focused))
            {
                break;
            }

            order.Add(focused);
            var next = new TraversalRequest(FocusNavigationDirection.Next);
            _ = focused switch
            {
                UIElement e => e.MoveFocus(next),
                ContentElement c => c.MoveFocus(next),
                _ => false,
            };
        }

        return order;
    }

    /// <summary>Renders the area around <paramref name="element"/> without and with keyboard focus: the
    /// focus must change pixels clearly. A change of 2.5:1 or more is required, enough to rule out a
    /// ring in nearly the background's colour; the faintest ring the app draws, a Text outline on the
    /// accent fill, changes its pixels by 2.9:1 and stands at 16:1 against the card around it.</summary>
    private static void AssertFocusShows(Window window, UIElement element, string name)
    {
        element.Focus();
        Flush(window);
        Keyboard.ClearFocus();
        Flush(window);
        var bounds = element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));
        bounds.Inflate(3, 3);
        bounds.Intersect(new Rect(0, 0, Math.Floor(window.ActualWidth), Math.Floor(window.ActualHeight)));
        var area = new Int32Rect((int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height);
        var before = Render(window, area);
        element.Focus();
        Flush(window);
        var after = Render(window, area);

        var clear = 0;
        for (var i = 0; i < before.Length; i += 4)
        {
            if (Contrast(before, after, i) >= 2.5)
            {
                clear++;
            }
        }

        // To look at every focus state: set SSE_FOCUS_DUMP to a folder; each stop is saved unfocused (-0)
        // and focused (-1).
        if (Environment.GetEnvironmentVariable("SSE_FOCUS_DUMP") is { Length: > 0 } dump)
        {
            var file = string.Concat(name.Where(char.IsLetterOrDigit).Take(40));
            Save(before, area, System.IO.Path.Combine(dump, file + "-0.png"));
            Save(after, area, System.IO.Path.Combine(dump, file + "-1.png"));
        }

        Assert.True(clear >= 20, $"Focus on '{name}' ({Describe(element)}) is hard to see: {clear} pixels change clearly");
    }

    /// <summary>The window as drawn, cut to <paramref name="area"/>.</summary>
    private static byte[] Render(Window window, Int32Rect area)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var pixels = new byte[area.Width * area.Height * 4];
        bitmap.CopyPixels(area, pixels, area.Width * 4, 0);
        return pixels;
    }

    private static void Save(byte[] pixels, Int32Rect area, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(area.Width, area.Height, 96, 96, PixelFormats.Pbgra32, null, pixels, area.Width * 4)));
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using var file = System.IO.File.Create(path);
        encoder.Save(file);
    }

    /// <summary>WCAG contrast between the same pixel in two renders (BGRA, opaque).</summary>
    private static double Contrast(byte[] a, byte[] b, int i)
    {
        var la = Luminance(a[i + 2], a[i + 1], a[i]);
        var lb = Luminance(b[i + 2], b[i + 1], b[i]);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(byte r, byte g, byte b) => 0.2126 * Linear(r) + 0.7152 * Linear(g) + 0.0722 * Linear(b);

    private static double Linear(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    /// <summary>Words, not a symbol drawn as an icon ("‹ Back", "+", "×") or an abbreviation ("ex"). A
    /// stat may start with its sign ("+18% to Fire Resistance").</summary>
    private static bool Sayable(string name) =>
        name.Count(char.IsLetter) >= 3
        && (char.IsLetterOrDigit(name[0]) || (name[0] is '+' or '-' && name.Length > 1 && char.IsDigit(name[1])));

    private static string SpokenName(DependencyObject o) => o switch
    {
        UIElement e => Peer(e).GetName(),
        ContentElement c => ContentElementAutomationPeer.CreatePeerForElement(c)?.GetName() ?? "",
        _ => "",
    };

    private static AutomationPeer Peer(UIElement element) =>
        UIElementAutomationPeer.CreatePeerForElement(element) ?? throw new InvalidOperationException($"{Describe(element)} has no automation peer");

    private static string Describe(DependencyObject o) =>
        $"{o.GetType().Name} '{(o as ContentControl)?.Content}' ({AutomationProperties.GetName(o)})";

    private static IEnumerable<T> Descendants<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var inner in Descendants<T>(child))
            {
                yield return inner;
            }
        }
    }

    /// <summary>A key press and release, through WPF's input pipeline, to whatever has keyboard focus.</summary>
    private static void Press(Key key)
    {
        foreach (var routed in new[] { Keyboard.PreviewKeyDownEvent, Keyboard.PreviewKeyUpEvent })
        {
            var target = (DependencyObject)Keyboard.FocusedElement;
            var source = PresentationSource.FromDependencyObject(target) ?? throw new InvalidOperationException("Nothing has keyboard focus");
            InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = routed });
            Sta.Drain();
        }
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Sta.Drain();
        window.UpdateLayout();
    }
}
