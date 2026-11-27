using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Domain.Capture;
using StillSaneExile.Domain.Items;
using StillSaneExile.Domain.Market;
using StillSaneExile.Domain.Pricing;

namespace StillSaneExile.Desktop.ViewModels;

public sealed record GuideStep(int Number, string Title, string Text)
{
    /// <summary>What a screen reader says for the step (WPF reads a list row through ToString).</summary>
    public override string ToString() => $"{Number}. {Title}. {Text}";
}

/// <summary>Settings and first-run onboarding (same page, different heading).</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;
    private Hotkey _peek;
    private Hotkey _openLast;
    private Hotkey _stash;

    public SettingsViewModel(ShellViewModel shell)
    {
        _shell = shell;
        Load();
        shell.StashPictures.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StashPicturesViewModel.IsCollecting))
            {
                OnPropertyChanged(nameof(ReadsExplanation));
                OnPropertyChanged(nameof(StashKeyNote));
            }
        };
    }

    private Settings S => _shell.Settings;

    /// <summary>The "Stash reading (coming soon): help build it" card.</summary>
    public StashPicturesViewModel Pictures => _shell.StashPictures;

    /// <summary>Beside the stash key in the Keys card.</summary>
    public string StashKeyNote => Pictures.IsCollecting ? "takes stash pictures (see below)" : "coming later";

    [ObservableProperty]
    public partial bool IsOnboarding { get; set; }

    [ObservableProperty]
    public partial bool LiveSelected { get; set; }

    [ObservableProperty]
    public partial string LeagueDraft { get; set; } = "";

    [ObservableProperty]
    public partial string? ConfirmedLeague { get; set; }

    [ObservableProperty]
    public partial string PeekKey { get; set; } = "";

    [ObservableProperty]
    public partial string OpenLastKey { get; set; } = "";

    [ObservableProperty]
    public partial string StashKey { get; set; } = "";

    [ObservableProperty]
    public partial string? Recording { get; set; }

    [ObservableProperty]
    public partial bool ToggleMode { get; set; }

    [ObservableProperty]
    public partial PeekAnchor Anchor { get; set; }

    [ObservableProperty]
    public partial double PeekScale { get; set; }

    [ObservableProperty]
    public partial string ExpiryDays { get; set; } = "3";

    [ObservableProperty]
    public partial bool AutoUpdate { get; set; }

    /// <summary>"Live prices from poe.ninja". On by default; only ever fetches for a confirmed live league.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LiveStatus), nameof(LiveHold), nameof(LiveProblem))]
    public partial bool LivePricesOn { get; set; }

    /// <summary>poe.ninja's economy leagues, offered as choices next to the typed name.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadLeagues))]
    public partial IReadOnlyList<string> LeagueChoices { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadLeagues))]
    public partial bool LoadingLeagues { get; set; }

    /// <summary>Where the league choices came from, or why there are none and what to do instead.</summary>
    [ObservableProperty]
    public partial string? LeagueListStatus { get; set; }

    /// <summary>The "Get the league list from poe.ninja" button shows until a list has loaded.</summary>
    public bool CanLoadLeagues => LeagueChoices.Count == 0 && !LoadingLeagues;

    /// <summary>Last refresh and next refresh, in clock times.</summary>
    public string LiveStatus
    {
        get
        {
            if (!LivePricesOn)
            {
                return "Off: SSE asks poe.ninja for nothing.";
            }

            var session = _shell.Session;
            if (session is null || session.IsDemo)
            {
                return "Starts once you confirm a live league above.";
            }

            var prices = _shell.Prices;
            var now = session.Now;
            var book = session.Book;
            var last = prices?.LastRefresh ?? (book.Quotes.Count > 0 ? book.CreatedAt : null);
            var count = prices is { LastCount: > 0 } ? prices.LastCount : book.Quotes.Count;
            var text = last is { } l
                ? $"Last refresh {Session.LocalTime(l, now)}: {count.ToString("N0", CultureInfo.InvariantCulture)} prices for {session.League}."
                : $"No prices for {session.League} yet.";
            if (prices?.IsRefreshing == true)
            {
                text += $" Refreshing now: {prices.RequestsPerRefresh} requests, one at a time.";
            }
            else if (prices?.NextRefresh is { } next)
            {
                text += $" Next refresh about {Session.LocalTime(next, now)}.";
            }

            return text;
        }
    }

    /// <summary>Why poe.ninja is not being asked right now (cooling down, circuit open...), or null.</summary>
    public string? LiveHold => LivePricesOn && _shell.Session is { IsDemo: false } ? _shell.Prices?.Hold : null;

    /// <summary>What went wrong with the last refresh, or null.</summary>
    public string? LiveProblem => LivePricesOn && _shell.Session is { IsDemo: false } ? _shell.Prices?.Problem : null;

    public UpdateService Updates => _shell.Updates;

    public WhatsNewViewModel WhatsNew => _shell.WhatsNew;

    [ObservableProperty]
    public partial string TestLog { get; set; } = "Press any key or side mouse button while this page is open to see press, hold and release.";

    [ObservableProperty]
    public partial string? Message { get; set; }

    public IReadOnlyList<string> CurrencyChoices => Currencies.Display;

    public string DataFolder => _shell.Paths.DataDir + (_shell.Paths.Portable ? " (portable)" : "");

    /// <summary>Onboarding: "A live league" was picked, so the league name box is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProfileChoice))]
    public partial bool ShowLeagueInput { get; set; }

    /// <summary>"live" or "demo", for the onboarding choice buttons.</summary>
    public string ProfileChoice => LiveSelected || ShowLeagueInput ? "live" : "demo";

    partial void OnLiveSelectedChanged(bool value) => OnPropertyChanged(nameof(ProfileChoice));

    /// <summary>The legend under the sample card; numbers match the pins on it.</summary>
    public IReadOnlyList<GuideStep> GuideSteps =>
    [
        new(1, "Item name", "Coloured the same as in game: white, blue, yellow or orange."),
        new(2, "What it’s worth", "What items like yours sell for, and in plain words how sure that is."),
        new(3, "How many are for sale", "More for sale means a better price guess."),
        new(4, "Stats in the search", "How many of your stats were used to find similar items."),
        new(5, "Ticked = searched", "Ticked stats show the number searched for. Empty boxes were left out."),
        ToggleMode
            ? new(6, "Press again to hide", "The card stays until you press the key again.")
            : new(6, "Move onto it to keep it", "Still holding the key, move the mouse onto the card: it stays open, and you can tick or untick stats on it. Let go without moving and it hides."),
        new(7, $"Press {OpenLastKey} to change it", "Every filter, and your own numbers — like 160 instead of 175."),
    ];

    private PeekViewModel? _samplePeek;

    /// <summary>The demo crossbow as a real peek card, for "How to read a price check".</summary>
    public PeekViewModel SamplePeek => _samplePeek ??= BuildSamplePeek();

    public IReadOnlyList<string> KeyWarnings
    {
        get
        {
            var list = new List<string>();
            var keys = new[] { ("Quick price", _peek), ("Change the search", _openLast), ("Stash prices", _stash) };
            foreach (var g in keys.GroupBy(k => k.Item2).Where(g => g.Count() > 1))
            {
                list.Add($"{string.Join(" and ", g.Select(x => x.Item1))} share {g.Key.Label}. Give each its own key or button.");
            }

            foreach (var (name, key) in keys)
            {
                if (GameKeys.DefaultAction(key) is { } action)
                {
                    list.Add($"{name} ({key.Label}) may clash with the game's default {action} key.");
                }
            }

            if (StashKey.Equals("RightAlt", StringComparison.OrdinalIgnoreCase) && _shell.Hud?.AltGrDetected == true)
            {
                list.Add("Your layout uses Right Alt as AltGr. Pick another stash key so typing is not affected.");
            }

            return list;
        }
    }

    /// <summary>Exactly what each binding reads. Only the game's own copy key makes the game copy the
    /// hovered item; any other quick price binding reads whatever the clipboard already holds.</summary>
    public string ReadsExplanation =>
        (PeekKey.Equals("Ctrl+C", StringComparison.OrdinalIgnoreCase)
            ? "Ctrl+C is the game's own copy key, so one press copies and peeks (Ctrl+Alt+C too, for the advanced copy). The game still copies; the app only listens.\n"
            : "") +
        (_peek.IsNativeCopy
            ? $"{PeekKey}: reads the clipboard once, right after the game copies the hovered item. If the game copied nothing, nothing is read.\n"
            : $"{PeekKey}: reads the clipboard once per press, straight away. It is not the game's copy key, so the game copies nothing: it shows the last item you copied with Ctrl+C (or Ctrl+Alt+C), which may not be the one under your cursor.\n") +
        (ToggleMode
            ? ""
            : "While you hold it, the app checks where the mouse cursor is (its position, not the screen) to notice you moving onto the card. Ticking stats on the card reads nothing.\n") +
        $"{OpenLastKey}: reads nothing; opens the price check for your last item over the game (Esc closes it).\n" +
        (Pictures.IsCollecting
            ? $"{StashKey}: \"Collect stash pictures\" is on, so each press with the game in front takes one picture of the game window and saves it in your data folder. Holding the key takes no more. Stash and Aldur labels come later (Milestone 2)."
            : $"{StashKey}: reserved for stash and Aldur labels (Milestone 2). It reads nothing now; with \"Collect stash pictures\" on (below), each press takes one picture of the game window.");

    /// <summary>Onboarding's one line under "Quick price".</summary>
    public string PeekHowTo => (_peek.IsNativeCopy, ToggleMode) switch
    {
        (true, false) => "Hover an item and hold. Move onto the card to keep it open and change the search; let go without moving and it hides.",
        (true, true) => "Hover an item and press. Press again to hide.",
        (false, false) => "Shows the last item you copied with Ctrl+C. Move onto the card to keep it open; let go without moving and it hides.",
        (false, true) => "Shows the last item you copied with Ctrl+C. Press again to hide.",
    };

    [MemberNotNull(nameof(_peek), nameof(_openLast), nameof(_stash))]
    public void Load()
    {
        LiveSelected = S.Profile == ProfileKind.Live && !string.IsNullOrWhiteSpace(S.LiveLeague);
        ConfirmedLeague = string.IsNullOrWhiteSpace(S.LiveLeague) ? null : S.LiveLeague;
        LeagueDraft = ConfirmedLeague ?? "";
        _peek = S.PeekKey;
        _openLast = S.OpenLastKey;
        _stash = S.StashKey;
        PeekKey = _peek.Label;
        OpenLastKey = _openLast.Label;
        StashKey = _stash.Label;
        ToggleMode = S.PeekMode == HoldMode.Toggle;
        Anchor = S.Anchor;
        PeekScale = S.PeekScale;
        ExpiryDays = S.ComparableExpiryDays.ToString(CultureInfo.InvariantCulture);
        AutoUpdate = S.AutoUpdate;
        LivePricesOn = S.LivePrices;
        OnPricesChanged();
        IsOnboarding = !S.Onboarded;
        _samplePeek = null; // keys or currency may have changed
        OnPropertyChanged(nameof(CurrentCurrency));
        OnPropertyChanged(nameof(ReadsExplanation));
        OnPropertyChanged(nameof(PeekHowTo));
        OnPropertyChanged(nameof(KeyWarnings));
        OnPropertyChanged(nameof(GuideSteps));
        OnPropertyChanged(nameof(SamplePeek));
    }

    public string CurrentCurrency => _shell.Session?.DisplayCurrency ?? "";

    partial void OnToggleModeChanged(bool value)
    {
        S.PeekMode = value ? HoldMode.Toggle : HoldMode.Hold;
        _shell.Hud?.ApplySettings();
        _samplePeek = null; // its footer says how to hide it
        OnPropertyChanged(nameof(ReadsExplanation));
        OnPropertyChanged(nameof(PeekHowTo));
        OnPropertyChanged(nameof(GuideSteps));
        OnPropertyChanged(nameof(SamplePeek));
    }

    /// <summary>Settings → Quick price card: "hold" (hide on release, unless the player moved onto the
    /// card) or "toggle" (press again to hide).</summary>
    [RelayCommand]
    private void SetPeekMode(string mode) => ToggleMode = mode == "toggle";

    partial void OnAnchorChanged(PeekAnchor value) => S.Anchor = value;

    partial void OnAutoUpdateChanged(bool value)
    {
        if (S.AutoUpdate != value)
        {
            S.AutoUpdate = value;
            Updates.Start(value);
        }
    }

    partial void OnLivePricesOnChanged(bool value)
    {
        if (S.LivePrices == value)
        {
            return;
        }

        S.LivePrices = value;
        if (value)
        {
            _shell.Prices?.Retry(); // the player's own retry after poe.ninja refused access
        }

        _shell.ReloadSession();
    }

    private LivePrices? _watched;

    /// <summary>Follows the refresh's progress for the Live prices card.</summary>
    public void UsePrices(LivePrices? prices)
    {
        if (_watched is not null)
        {
            _watched.PropertyChanged -= OnPricesPropertyChanged;
        }

        _watched = prices;
        if (prices is not null)
        {
            prices.PropertyChanged += OnPricesPropertyChanged;
        }

        OnPricesChanged();
    }

    private void OnPricesPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => OnPricesChanged();

    public void OnPricesChanged()
    {
        OnPropertyChanged(nameof(LiveStatus));
        OnPropertyChanged(nameof(LiveHold));
        OnPropertyChanged(nameof(LiveProblem));
    }

    /// <summary>The Settings page came to the front: a player already on live prices gets poe.ninja's
    /// leagues to pick from without asking. Anyone else asks with the button.</summary>
    public void OnShown()
    {
        if (LeagueChoices.Count == 0 && !LoadingLeagues && _shell.Prices is not null && _shell.Session is { IsDemo: false, LivePricesOn: true })
        {
            LoadLeaguesCommand.Execute(null);
        }
    }

    /// <summary>One request for poe.ninja's league list. Offline, the typed name still works.</summary>
    [RelayCommand]
    private async Task LoadLeagues()
    {
        if (_shell.Prices is not { } prices)
        {
            LeagueListStatus = "Type the league name exactly as the game shows it.";
            return;
        }

        LoadingLeagues = true;
        LeagueListStatus = "Asking poe.ninja for its leagues…";
        try
        {
            var leagues = await prices.LeaguesAsync();
            LeagueChoices = [.. leagues.Select(l => l.Name)];
            LeagueListStatus = leagues.Count > 0
                ? "Leagues poe.ninja has prices for. Pick one or type a name, then confirm."
                : "poe.ninja listed no leagues. Type the league name exactly as the game shows it.";
        }
        catch (Exception e) when (e is HttpRequestException or ProviderHoldException or OperationCanceledException or System.Text.Json.JsonException)
        {
            LeagueListStatus = "Couldn't get the league list from poe.ninja. Type the league name exactly as the game shows it.";
        }
        finally
        {
            LoadingLeagues = false;
        }
    }

    /// <summary>A league from poe.ninja's list goes into the name box; it is used only after "Confirm league".</summary>
    [RelayCommand]
    private void PickLeague(string league) => LeagueDraft = league;

    [RelayCommand]
    private Task CheckForUpdates() => Updates.CheckAsync(userInitiated: true);

    [RelayCommand]
    private void RestartToUpdate() => Updates.RestartNow();

    partial void OnPeekScaleChanged(double value) => S.PeekScale = value;

    partial void OnExpiryDaysChanged(string value)
    {
        if (int.TryParse(value, out var d) && d is >= 1 and <= 60)
        {
            S.ComparableExpiryDays = d;
        }
    }

    [RelayCommand]
    private void UseDemo()
    {
        LiveSelected = false;
        ShowLeagueInput = false;
        S.Profile = ProfileKind.Demo;
        _shell.ReloadSession();
    }

    /// <summary>Onboarding's "A live league": shows the league box and asks poe.ninja for its leagues.</summary>
    [RelayCommand]
    private void ChooseLive()
    {
        ShowLeagueInput = true;
        if (LeagueChoices.Count == 0 && !LoadingLeagues)
        {
            LoadLeaguesCommand.Execute(null);
        }
    }

    [RelayCommand]
    private void ConfirmLeague()
    {
        var league = LeagueDraft.Trim();
        if (league.Length == 0)
        {
            Message = "Type the league name exactly as the game shows it, then confirm.";
            return;
        }

        // poe.ninja's own spelling when only the case differs ("standard" → "Standard").
        league = LeagueChoices.FirstOrDefault(l => l.Equals(league, StringComparison.OrdinalIgnoreCase)) ?? league;
        var listed = LeagueChoices.Count == 0 || LeagueChoices.Contains(league, StringComparer.Ordinal);
        S.LiveLeague = league;
        S.Profile = ProfileKind.Live;
        ConfirmedLeague = league;
        LiveSelected = true;
        Message = !listed
            ? $"Live profile on {league}. poe.ninja lists no league by that name, so prices may not load: check the spelling."
            : S.LivePrices
                ? $"Live profile on {league}. Its prices come from poe.ninja and refresh every hour. Demo prices are never shown here."
                : $"Live profile on {league}. Live prices are off, so it has no prices until you switch them on. Demo prices are never shown here.";
        _shell.ReloadSession();
    }

    [RelayCommand]
    private void SetCurrency(string currency)
    {
        S.SetDisplayCurrency(_shell.Session.Kind, currency);
        _shell.ReloadSession();
    }

    [RelayCommand]
    private void SetAnchor(string anchor)
    {
        if (Enum.TryParse<PeekAnchor>(anchor, out var a))
        {
            Anchor = a;
        }
    }

    [RelayCommand]
    private void StartRecording(string target)
    {
        Recording = target;
        Message = "Press the new key combination, or a side mouse button, now. Esc cancels.";
    }

    /// <summary>Called by the view while recording. Returns true when the key was consumed.</summary>
    public bool Record(Key key, ModifierKeys mods)
    {
        if (Recording is null)
        {
            return false;
        }

        if (key == Key.Escape)
        {
            Recording = null;
            Message = null;
            return true;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.System)
        {
            return true; // wait for the main key
        }

        Save(new Hotkey(KeyInterop.VirtualKeyFromKey(key), mods.HasFlag(ModifierKeys.Control), mods.HasFlag(ModifierKeys.Shift), mods.HasFlag(ModifierKeys.Alt) && key != Key.RightAlt));
        return true;
    }

    /// <summary>Called by the view for mouse presses while recording: a side button becomes the
    /// binding. Left, right and middle click never do, because the game uses them for skills and SSE's
    /// own buttons need the left one. Returns true when the press was consumed.</summary>
    public bool RecordMouse(MouseButton button, ModifierKeys mods)
    {
        if (Recording is null)
        {
            return false;
        }

        switch (button)
        {
            case MouseButton.XButton1 or MouseButton.XButton2:
                var vk = button == MouseButton.XButton1 ? Hotkey.VkXButton1 : Hotkey.VkXButton2;
                Save(new Hotkey(vk, mods.HasFlag(ModifierKeys.Control), mods.HasFlag(ModifierKeys.Shift), mods.HasFlag(ModifierKeys.Alt)));
                return true;
            case MouseButton.Right or MouseButton.Middle:
                Message = "Left, right and middle click are skill buttons in the game, so they can't be bound. Press a key or a side mouse button (Mouse 4 or 5). Esc cancels.";
                return true;
            default:
                return false; // a left click still works the page, e.g. another "Change" button
        }
    }

    private void Save(Hotkey binding)
    {
        switch (Recording)
        {
            case "peek":
                S.PeekKey = binding;
                break;
            case "openLast":
                S.OpenLastKey = binding;
                break;
            case "stash":
                S.StashKey = binding;
                break;
        }

        Recording = null;
        Message = $"Saved {binding.Label}.";
        Load();
        OnPropertyChanged(nameof(KeyWarnings));
        OnPropertyChanged(nameof(ReadsExplanation));
        _shell.StashPictures.ApplySettings();
        _shell.Hud?.ApplySettings();
    }

    [RelayCommand]
    private void ResetDemoData()
    {
        _shell.Store.ResetDemo();
        Message = "Demo prices and holdings you added were removed. The seeded demo data stays.";
        _shell.ReloadSession();
    }

    /// <summary>"Done — try a price check": in the demo, opens the practice crossbow so there is
    /// something to try straight away.</summary>
    [RelayCommand]
    private void FinishOnboarding()
    {
        SkipOnboarding();
        var inspect = _shell.Inspect;
        if (_shell.Session.IsDemo && inspect.Recent.Count == 0)
        {
            inspect.LoadPracticeCommand.Execute(inspect.PracticeItems[0]);
        }
    }

    [RelayCommand]
    private void SkipOnboarding()
    {
        S.Onboarded = true;
        IsOnboarding = false;
        Recording = null;
        Message = null;
        _shell.GoInspectCommand.Execute(null);
    }

    private PeekViewModel BuildSamplePeek()
    {
        var session = Session.Demo(_shell.Store, S, _shell.Paths);
        var text = File.ReadAllText(Path.Combine(_shell.Paths.Fixtures, "items", "rare-crossbow-advanced.txt"));
        var parsed = ItemTextParser.Parse(text, session.Now, "onboarding-sample");
        if (!parsed.Ok)
        {
            return PeekViewModel.Message(parsed.Error ?? "The sample item could not be read.", true, PeekViewModel.ReleaseHintFor(S.PeekMode));
        }

        var item = new ItemViewModel(parsed.Item!, session);
        return PeekViewModel.From(item, true, PeekViewModel.ReleaseHintFor(S.PeekMode), S.OpenLastKey.Label, pins: true);
    }

    public void OnTestKey(KeyEvent e, string name, TimeSpan? held)
    {
        var what = e.Down ? (held is null ? "press" : "auto-repeat (ignored)") : $"release after {held?.TotalMilliseconds:0} ms";
        var injected = e.Injected ? " · synthetic input (ignored)" : "";
        var altgr = _shell.Hud?.AltGrDetected == true ? " · AltGr layout detected" : "";
        TestLog = $"{name}: {what}{injected}{altgr}";
        OnPropertyChanged(nameof(KeyWarnings));
    }
}
