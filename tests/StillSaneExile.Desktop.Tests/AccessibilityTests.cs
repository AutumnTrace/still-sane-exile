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

    /// <summary>A character a few stops into the route, as a player gets there: one step ticked, one
    /// skipped, and the synthetic practice build's code pasted with its first gem step ticked.</summary>
    private static ViewModels.GuideViewModel SeedGuide(TestApp app)
    {
        var guide = app.Shell.Guide;
        Assert.True(guide.HasPack, guide.PackProblem);
        guide.NewCharacterName = "Practice exile";
        guide.AddCharacterCommand.Execute(null);
        guide.ToggleDoneCommand.Execute(guide.Steps.FirstOrDefault());
        guide.NextCommand.Execute(null);
        guide.NextCommand.Execute(null);
        guide.SkipCommand.Execute(guide.Steps.FirstOrDefault());
        guide.NextCommand.Execute(null);
        guide.Plan.CodeDraft = GuideBuildTests.PracticeCode(app);
        guide.Plan.ReadCodeCommand.Execute(null);
        Assert.True(guide.Plan.HasBuild, guide.Plan.Message);
        guide.Plan.ToggleStepCommand.Execute(guide.Plan.Checklist.FirstOrDefault());
        return guide;
    }

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
        Assert.True(stops.IndexOf("Reset to my item") < stops.IndexOf("Search"));
        Assert.True(stops.IndexOf("Search") < stops.IndexOf("Open this search on the trade site"));
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

        // The window's content as the window shows it: scrolling, close button included. Not the window
        // itself: it hides as soon as anything else on the desktop takes the foreground, as it should over
        // the game.
        var view = new PriceCheckView { DataContext = item, Width = 600, CloseCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }) };
        var stops = Check(new ScrollViewer { Content = view, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        Assert.Equal("Close the price check", stops[0]);
        Assert.Contains("Copy search", stops);
    });

    /// <summary>The crossbow's card, pinned: the player moved onto it while holding the key.</summary>
    private static ViewModels.PeekViewModel PinnedCard(TestApp app)
    {
        var item = app.Shell.Inspect.AddFromText(app.ItemText(Crossbow), "test")!;
        var card = ViewModels.PeekViewModel.From(item, true, ViewModels.PeekViewModel.ReleaseHintFor(Domain.Capture.HoldMode.Hold), "F3");
        card.IsPinned = true;
        return card;
    }

    [Fact]
    public void The_pinned_quick_price_card_works_from_the_keyboard() => UiThread.Run(() =>
    {
        using var app = new TestApp();
        var view = new PeekCard
        {
            DataContext = PinnedCard(app),
            CloseCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }),
            MoreCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }),
        };

        var stops = Check(view);

        Assert.Equal("Close the price card", stops[0]);
        Assert.Equal(new[] { "Physical DPS 355.3", "Lower the minimum for Physical DPS 355.3", "Raise the minimum for Physical DPS 355.3" }, stops[1..4]);
        Assert.Contains("+18% to Fire Resistance", stops);
        Assert.Equal("Search", stops[^2]);
        Assert.Equal("All filters", stops[^1]);
    });

    [Fact]
    public void The_held_quick_price_card_has_nothing_to_tab_to() => UiThread.Run(() =>
    {
        using var app = new TestApp();
        var card = PinnedCard(app);
        card.IsPinned = false;
        var window = Show(new PeekCard { DataContext = card });
        try
        {
            Flush(window);
            Assert.DoesNotContain(Descendants<ButtonBase>(window), b => b.IsVisible);
        }
        finally
        {
            window.Close();
        }
    });

    private const string Helmet = "rare-helmet-corrupted-advanced.txt";
    private const string CorruptedTag = "Search corrupted items only";

    [Fact]
    public void The_Corrupted_tag_of_the_price_check_works_from_the_keyboard_and_says_whether_it_is_on() => UiThread.Run(() =>
    {
        using var app = new TestApp();
        var item = app.Shell.Inspect.AddFromText(app.ItemText(Helmet), "test")!;
        PriceCheckView View() => new() { DataContext = item, Width = 600, CloseCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }) };

        var on = Check(new ScrollViewer { Content = View(), VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); // "Selected" while on

        Assert.Equal(["Close the price check", CorruptedTag], on[..2]);
        Assert.Contains("+74 to Accuracy Rating, corrupted", on);

        var window = Show(View());
        try
        {
            Flush(window);
            var tag = Descendants<Button>(window).Single(b => SpokenName(b) == CorruptedTag);
            Assert.Equal("Selected", Peer(tag).GetItemStatus());

            ((System.Windows.Automation.Provider.IInvokeProvider)Peer(tag).GetPattern(PatternInterface.Invoke)).Invoke();
            Flush(window);

            Assert.False(item.CorruptedOnly);
            Assert.Equal("", Peer(tag).GetItemStatus());
            Assert.True(item.IsStale);
        }
        finally
        {
            window.Close();
        }

        var off = Check(new ScrollViewer { Content = View(), VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Assert.Equal(CorruptedTag, off[1]);
    });

    [Fact]
    public void The_Corrupted_tag_is_clicked_on_the_pinned_card_and_only_shown_on_the_held_one() => UiThread.Run(() =>
    {
        using var app = new TestApp();
        var item = app.Shell.Inspect.AddFromText(app.ItemText(Helmet), "test")!;
        var card = ViewModels.PeekViewModel.From(item, true, ViewModels.PeekViewModel.ReleaseHintFor(Domain.Capture.HoldMode.Hold), "F3");
        card.IsPinned = true;

        var stops = Check(new PeekCard
        {
            DataContext = card,
            CloseCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }),
            MoreCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }),
        });

        Assert.Equal(["Close the price card", CorruptedTag], stops[..2]);
        Assert.Contains("+74 to Accuracy Rating, corrupted", stops);

        card.IsPinned = false;
        var held = new PeekCard { DataContext = card };
        var window = Show(held);
        try
        {
            Flush(window);
            Assert.DoesNotContain(Descendants<ButtonBase>(window), b => b.IsVisible);
            var shown = (ContentControl)held.FindName("CorruptedTag");
            Assert.True(shown.IsVisible);
            Assert.Equal("active", shown.Tag);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void Ticking_a_stat_on_the_pinned_card_marks_its_price_old_and_Search_updates_it() => UiThread.Run(() =>
    {
        using var app = new TestApp();
        var card = PinnedCard(app);
        var view = new PeekCard { DataContext = card };
        var window = Show(view);
        try
        {
            Flush(window);
            var count = (Run)view.FindName("CountText");
            var price = (ValueLabel)view.FindName("BigText");
            var line = (ValueLabel)view.FindName("PeekLineText");
            var before = (count.Text, price.Text);
            Assert.Equal(1, price.Opacity);
            var dps = Descendants<CheckBox>(window).Single(c => SpokenName(c) == "Physical DPS 355.3");
            Assert.True(dps.IsChecked);

            ((System.Windows.Automation.Provider.IToggleProvider)Peer(dps).GetPattern(PatternInterface.Toggle)).Toggle();
            Flush(window);

            Assert.False(dps.IsChecked);
            Assert.Equal(before, (count.Text, price.Text)); // ticking never searches
            Assert.Equal("Search to update", line.Text);
            Assert.True(price.Opacity < 1);
            Assert.Equal(1, card.OnCount);

            var search = Descendants<Button>(window).Single(b => SpokenName(b) == "Search");
            ((System.Windows.Automation.Provider.IInvokeProvider)Peer(search).GetPattern(PatternInterface.Invoke)).Invoke();
            Flush(window);

            Assert.NotEqual(before, (count.Text, price.Text));
            Assert.Equal(card.Answer!.Count?.ToString(System.Globalization.CultureInfo.InvariantCulture), count.Text);
            Assert.Equal(card.Answer.PeekNote, line.Text);
            Assert.Equal(1, price.Opacity);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void Esc_the_close_button_and_All_filters_on_the_pinned_card_reach_the_HUD() => UiThread.Run(() =>
    {
        using var app = new TestApp();
        var window = new PeekWindow { Left = -20000, Top = 0, DataContext = PinnedCard(app) };
        var closed = 0;
        var more = 0;
        window.CloseRequested += () => closed++;
        window.MoreRequested += () => more++;
        window.Show();
        try
        {
            window.SetPinned(true);
            EnsureActive(window);
            Flush(window);

            var close = Descendants<Button>(window).Single(b => SpokenName(b) == "Close the price card");
            ((System.Windows.Automation.Provider.IInvokeProvider)Peer(close).GetPattern(PatternInterface.Invoke)).Invoke();
            Sta.Drain();
            Assert.Equal(1, closed);

            var all = Descendants<Button>(window).Single(b => SpokenName(b) == "All filters");
            ((System.Windows.Automation.Provider.IInvokeProvider)Peer(all).GetPattern(PatternInterface.Invoke)).Invoke();
            Sta.Drain();
            Assert.Equal(1, more);

            Descendants<CheckBox>(window).First().Focus();
            Press(Key.Escape);
            Assert.Equal(2, closed);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void Settings_work_from_the_keyboard_one_section_at_a_time() => UiThread.Run(() =>
    {
        using var app = new TestApp();
        var settings = app.Shell.SettingsPage;
        var rail = ViewModels.SettingsViewModel.Sections.Select(s => s.Label).ToArray();

        List<string> Section(string key)
        {
            settings.ShowSectionCommand.Execute(key);
            var stops = Check(new SettingsView { DataContext = settings });
            Assert.Equal(rail, stops[..rail.Length]); // the rail first, then the section's own controls
            return stops[rail.Length..];
        }

        var general = Section("general");
        Assert.Contains("Live league name", general);
        Assert.True(general.IndexOf("Practice with demo data") < general.IndexOf("Divine Orb"));
        Assert.Contains("How leagues work", general);
        Assert.DoesNotContain("Change the quick price key", general);

        var keys = Section("keys");
        Assert.Contains("Change the quick price key", keys);
        Assert.Contains("What SSE listens to", keys);

        var card = Section("card");
        Assert.Contains("Hide when I let go, unless I move onto the card", card);
        Assert.Contains("Press again to hide (accessibility)", card);
        Assert.Contains("Quick price card scale", card);
        Assert.Contains("Less motion: no ring, slide or line, the card only fades", card);
        Assert.Contains("A soft click when the key is heard", card);
        Assert.Contains("Next to the game tooltip", card);
        Assert.True(card.IndexOf("Next to the game tooltip") < card.IndexOf("Screen edge"));
        Assert.True(card.IndexOf("Screen edge") < card.IndexOf("Fixed spot (drag once)"));

        var colours = Section("colours");
        Assert.Equal("Where white and red starts", colours[0]);
        Assert.True(colours.IndexOf("White and red in Divine Orbs") < colours.IndexOf("Where red starts")); // row by row
        Assert.Contains("Tan in Exalted Orbs", colours);
        Assert.DoesNotContain("Apply", colours); // nothing to apply until a step changes

        var market = Section("market");
        Assert.Contains("Search the trade site from SSE", market);
        Assert.Contains("Days until prices you write down expire", market);

        var pictures = Section("pictures");
        Assert.Contains("Collect stash pictures when I press RightAlt in the game", pictures);
        Assert.Contains("Open the pictures folder", pictures);

        var about = Section("about");
        Assert.Contains("Everything SSE keeps and sends", about);
    });

    [Fact]
    public void Details_open_and_close_from_the_keyboard() => UiThread.Run(() =>
    {
        using var app = new TestApp();
        var window = Show(new SettingsView { DataContext = app.Shell.SettingsPage });
        try
        {
            Flush(window);
            var details = Descendants<Expander>(window).First(e => e.IsVisible);
            var toggle = Descendants<System.Windows.Controls.Primitives.ToggleButton>(details).Single();
            Assert.False(details.IsExpanded);

            ((System.Windows.Automation.Provider.IToggleProvider)Peer(toggle).GetPattern(PatternInterface.Toggle)).Toggle();
            Flush(window);

            Assert.True(details.IsExpanded);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void Onboarding_works_from_the_keyboard() => UiThread.Run(() =>
    {
        using var app = new TestApp(s => s.Onboarded = false);
        app.Shell.SettingsPage.Load();

        var stops = Check(new SettingsView { DataContext = app.Shell.SettingsPage });

        Assert.Equal("Practice with demo data", stops[0]);
        Assert.Equal("Skip for now", stops[^1]);

        app.Shell.SettingsPage.ChooseLiveCommand.Execute(null);
        var live = Check(new SettingsView { DataContext = app.Shell.SettingsPage });

        Assert.True(live.IndexOf("League name") < live.IndexOf("Confirm league"));
    });

    [Fact]
    public void Stash_pictures_review_works_from_the_keyboard() => UiThread.Run(() =>
    {
        using var app = new TestApp(s => s.CollectStashPictures = true);
        StashTestPictures.Save(app, new DateTimeOffset(2026, 11, 27, 14, 30, 5, TimeSpan.Zero));
        StashTestPictures.Save(app, new DateTimeOffset(2026, 11, 27, 14, 31, 40, TimeSpan.Zero));
        var pictures = app.Shell.StashPictures;
        pictures.Reload();
        pictures.Pictures[0].TabType = Services.StashTabTypes.Currency;

        pictures.ReviewCommand.Execute(null);
        Assert.Same(app.Shell.SettingsPage, app.Shell.CurrentPage);
        Assert.Equal("pictures", app.Shell.SettingsPage.Section);
        var stops = Check(new SettingsView { DataContext = app.Shell.SettingsPage });

        Assert.Contains("Open the pictures folder", stops);
        Assert.Contains(stops, s => s.StartsWith("Currency tab: the picture from ", StringComparison.Ordinal));
        Assert.Contains(stops, s => s.StartsWith("Aldur panel: the picture from ", StringComparison.Ordinal));
        Assert.Single(stops, s => s.StartsWith("Currency subtab of the picture from ", StringComparison.Ordinal));
        Assert.Equal(2, stops.Count(s => s.StartsWith("In-game UI scale of the picture from ", StringComparison.Ordinal)));
        Assert.Equal(2, stops.Count(s => s.StartsWith("Held out: the picture from ", StringComparison.Ordinal)));
        Assert.Equal(2, stops.Count(s => s.StartsWith("Delete the picture from ", StringComparison.Ordinal)));
    });

    [Fact]
    public void The_craft_simulator_works_from_the_keyboard() => UiThread.Run(() =>
    {
        using var app = new TestApp();
        var craft = app.Shell.Craft;
        craft.Restart(new Random(3));
        craft.Groups.SelectMany(g => g.Orbs).First(o => o.Id == "transmute").ClickCommand.Execute(null); // in hand: the item is a button

        var stops = Check(new CraftView { DataContext = craft });

        Assert.Equal(["Lower the item level", "Item level", "Raise the item level", "Start over", "Regular orbs"], stops[..5]); // no click yet: nothing to undo
        Assert.True(stops.IndexOf("Orb of Transmutation") < stops.IndexOf("Orb of Augmentation"));
        Assert.Contains("Omen of Whittling", stops);
        Assert.Contains("Use Orb of Transmutation on the item", stops);
        Assert.True(stops.IndexOf("Omen of Dextral Annulment") < stops.IndexOf("Use Orb of Transmutation on the item")); // the bench, then the item
        Assert.Contains("T2+ for #% increased Physical Damage", stops);
        Assert.Contains("Stop wanting #% increased Attack Speed", stops);
    });

    [Fact]
    public void The_campaign_guide_works_from_the_keyboard() => UiThread.Run(() =>
    {
        using var app = new TestApp();
        var guide = SeedGuide(app);

        var stops = Check(new GuideView { DataContext = guide });

        Assert.Equal("Practice exile", stops[0]);
        Assert.Contains("New character name", stops);
        Assert.Contains(stops, s => s.StartsWith("Skip ", StringComparison.Ordinal));
        Assert.Contains(stops, s => s.StartsWith("Undo ", StringComparison.Ordinal));
        Assert.Contains("Previous stop", stops);
        Assert.Contains(stops, s => s.StartsWith("Next: ", StringComparison.Ordinal));
        Assert.Contains("One more ascendancy", stops);
        Assert.Contains("Cut Contagion", stops); // a gem step's tick box
        Assert.Contains("Show skill set Act 1", stops);
        Assert.Contains("Show skill set Endgame", stops);
        Assert.Contains("Path of Building code", stops);
        Assert.Contains("Read the build code", stops);
        Assert.True(stops.IndexOf("Previous stop") < stops.IndexOf("One more ascendancy"));
        Assert.True(stops.IndexOf("One more ascendancy") < stops.IndexOf("Show skill set Act 1"));
        Assert.True(stops.IndexOf("Show skill set Endgame") < stops.IndexOf("Path of Building code"));
        Assert.True(stops.IndexOf("Path of Building code") < stops.IndexOf("Read the build code"));
    });

    [Fact]
    public void The_campaign_guide_before_a_character_and_past_the_route_works_from_the_keyboard() => UiThread.Run(() =>
    {
        using var app = new TestApp();
        var guide = app.Shell.Guide;

        var empty = Check(new GuideView { DataContext = guide });

        Assert.Equal("New character name", empty[0]);

        SeedGuide(app);
        for (var i = 0; i < 50 && !guide.PastRoute; i++)
        {
            guide.NextCommand.Execute(null);
        }

        Assert.True(guide.PastRoute);
        guide.RemoveCharacterCommand.Execute(null); // asks first
        var past = Check(new GuideView { DataContext = guide });

        Assert.Contains("Earlier act", past);
        Assert.Contains("Later act", past);
        Assert.Contains("Remove Practice exile", past);
        Assert.Contains("Keep Practice exile", past);
    });

    [Fact]
    public void A_newer_build_code_waiting_as_a_revision_works_from_the_keyboard() => UiThread.Run(() =>
    {
        using var app = new TestApp();
        var plan = SeedGuide(app).Plan;
        plan.CodeDraft = GuideBuildTests.PracticeCode(app, xml => xml.Replace("title=\"Endgame\"", "title=\"After ascendancy\"", StringComparison.Ordinal));
        plan.ReadCodeCommand.Execute(null);

        Assert.True(plan.HasPending, plan.Message);
        var pending = Check(new GuideView { DataContext = app.Shell.Guide });

        Assert.Contains(pending, s => s.StartsWith("Use revision ", StringComparison.Ordinal));
        Assert.Contains(pending, s => s.StartsWith("Keep revision ", StringComparison.Ordinal));
    });

    [Fact]
    public void The_main_window_header_reads_left_to_right() => UiThread.Run(() =>
    {
        using var app = new TestApp();
        app.Shell.Navigate(app.Shell.Wealth);
        app.Shell.WhatsNew.Show(since: null, afterUpdate: true);
        var window = new MainWindow { DataContext = app.Shell, Left = -20000, Top = 0, Width = 1440, Height = 1000 };

        var stops = Check(window);

        Assert.Equal(new[] { "Price check", "Crafting", "My wealth later", "Campaign guide" }, stops[..4]);
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
            EnsureActive(window);
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
            var currencies = Descendants<Button>(window).Where(b => SpokenName(b) is "Exalted Orb" or "Divine Orb" or "Chaos Orb").ToList();

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
        Stage(window);
        if (!window.IsVisible)
        {
            window.Show();
        }

        try
        {
            EnsureActive(window);
            Flush(window);
            AssertNoTypeNames(window);
            var stops = TabOrder(window);
            Assert.NotEmpty(stops);
            AssertReadingOrder(window, stops);
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
        Stage(window);
        window.Show();
        return window;
    }

    private const string StageTag = "accessibility-test-stage";

    /// <summary>Lays the window's content out at the app's size, whatever the screen. Windows caps a window
    /// at the screen's size (GitHub's runners have 1024×768), which squeezes the layout and leaves Tab stops
    /// outside the window. The content sits on a fixed 1440×1000 surface with its own adorner layer, so focus
    /// rings still draw, and the focus check renders that surface rather than the window.</summary>
    private static void Stage(Window window)
    {
        if (window.Content is Canvas { Tag: StageTag })
        {
            return;
        }

        var content = (UIElement)window.Content;
        window.Content = null;
        window.Content = new Canvas
        {
            Tag = StageTag,
            Children =
            {
                new Border
                {
                    Width = 1440,
                    Height = 1000,
                    Background = window.Background,
                    Child = new AdornerDecorator { Child = content },
                },
            },
        };
    }

    /// <summary>What the focus check renders: the staged surface, or the window itself if it was never staged.</summary>
    private static FrameworkElement Surface(Window window) =>
        window.Content is Canvas { Tag: StageTag } stage ? (FrameworkElement)stage.Children[0] : window;

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

    /// <summary>Two stops in a row that sit on the same line are visited left to right: a box with its
    /// button docked to the right of it, declared first, would otherwise send Tab to the button first.</summary>
    private static void AssertReadingOrder(Window window, List<DependencyObject> stops)
    {
        foreach (var (before, after) in stops.Zip(stops.Skip(1)))
        {
            if (before is not UIElement a || after is not UIElement b)
            {
                continue;
            }

            // Both measured at one scroll position; a stop outside the view still has a place.
            var p = a.TransformToAncestor(window).TransformBounds(new Rect(a.RenderSize));
            var q = b.TransformToAncestor(window).TransformBounds(new Rect(b.RenderSize));
            var overlap = Math.Min(p.Bottom, q.Bottom) - Math.Max(p.Top, q.Top);
            var sameLine = overlap >= Math.Min(p.Height, q.Height) / 2;
            Assert.False(sameLine && q.Left < p.Left, $"Tab goes from '{SpokenName(a)}' back left to '{SpokenName(b)}' on the same line");
        }
    }

    /// <summary>The order Tab visits, from the first stop until it comes round again.</summary>
    private static List<DependencyObject> TabOrder(Window window)
    {
        var order = new List<DependencyObject>();
        Move(window, () => window.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)));
        for (var i = 0; i < 200; i++)
        {
            if (Keyboard.FocusedElement is not DependencyObject focused || order.Contains(focused))
            {
                break;
            }

            order.Add(focused);
            var next = new TraversalRequest(FocusNavigationDirection.Next);
            Move(window, () => _ = focused switch
            {
                UIElement e => e.MoveFocus(next),
                ContentElement c => c.MoveFocus(next),
                _ => false,
            });
        }

        return order;
    }

    /// <summary>Moves focus and checks the window kept the foreground meanwhile: another program on the
    /// desktop can take it at any moment, and keyboard focus goes with it. Then it tries again.</summary>
    private static void Move(Window window, Action move)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var from = Keyboard.FocusedElement;
            EnsureActive(window);
            if (from is not null && !ReferenceEquals(Keyboard.FocusedElement, from))
            {
                Keyboard.Focus(from);
            }

            move();
            Flush(window);
            if (window.IsActive)
            {
                return;
            }
        }
    }

    /// <summary>Renders the area around <paramref name="element"/> without and with keyboard focus: the
    /// focus must change pixels clearly. A change of 2.5:1 or more is required, enough to rule out a
    /// ring in nearly the background's colour; the faintest ring the app draws, a Text outline on the
    /// accent fill, changes its pixels by 2.9:1 and stands at 16:1 against the card around it.</summary>
    private static void AssertFocusShows(Window window, UIElement element, string name)
    {
        var surface = Surface(window);
        byte[] before, after;
        Int32Rect area;
        var attempt = 0;
        do
        {
            EnsureActive(window);
            element.Focus();
            Flush(window);
            Keyboard.ClearFocus();
            Flush(window);
            var bounds = element.TransformToAncestor(surface).TransformBounds(new Rect(element.RenderSize));
            bounds.Inflate(3, 3);
            bounds.Intersect(new Rect(0, 0, Math.Floor(surface.ActualWidth), Math.Floor(surface.ActualHeight)));
            Assert.False(bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1,
                $"'{name}' ({Describe(element)}) lies outside the {surface.ActualWidth}×{surface.ActualHeight} area the test renders");
            area = new Int32Rect((int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height);
            before = Render(surface, area);
            element.Focus();
            Flush(window);
            after = Render(surface, area);
        }
        while (!(window.IsActive && element.IsKeyboardFocused) && ++attempt < 3); // lost the foreground meanwhile

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

        Assert.True(clear >= 20, $"Focus on '{name}' ({Describe(element)}) is hard to see: {clear} pixels change clearly (window in front: {window.IsActive})");
    }

    /// <summary>The surface as drawn, cut to <paramref name="area"/>.</summary>
    private static byte[] Render(FrameworkElement surface, Int32Rect area)
    {
        var bitmap = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
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

    /// <summary>Keyboard focus needs the window in front. A window closed by the test before can hand the
    /// foreground elsewhere a moment after this one took it, so this asks again until it holds.</summary>
    private static void EnsureActive(Window window)
    {
        for (var i = 0; i < 20 && !window.IsActive; i++)
        {
            window.Activate();
            Sta.Drain();
            if (!window.IsActive)
            {
                Thread.Sleep(25);
            }
        }
    }
}
