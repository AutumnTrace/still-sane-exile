using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Domain.Capture;
using StillSaneExile.Domain.Items;
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
    }

    private Settings S => _shell.Settings;

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
        new(6, $"Press {OpenLastKey} to change it", "Tick or untick stats and type your own numbers — like 160 instead of 175."),
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
        $"{OpenLastKey}: reads nothing; opens the price check for your last item over the game (Esc closes it).\n" +
        $"{StashKey}: reserved for stash and Aldur labels (Milestone 2). Not active yet; it will read one frame of the game window per press, with your opt-in.";

    /// <summary>Onboarding's one line under "Quick price".</summary>
    public string PeekHowTo => _peek.IsNativeCopy
        ? "Hover an item and hold. Let go to hide."
        : "Shows the last item you copied with Ctrl+C. Let go to hide.";

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
    }

    partial void OnAnchorChanged(PeekAnchor value) => S.Anchor = value;

    partial void OnAutoUpdateChanged(bool value)
    {
        if (S.AutoUpdate != value)
        {
            S.AutoUpdate = value;
            Updates.Start(value);
        }
    }

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

    [RelayCommand]
    private void ChooseLive() => ShowLeagueInput = true;

    [RelayCommand]
    private void ConfirmLeague()
    {
        var league = LeagueDraft.Trim();
        if (league.Length == 0)
        {
            Message = "Type the league name exactly as the game shows it, then confirm.";
            return;
        }

        S.LiveLeague = league;
        S.Profile = ProfileKind.Live;
        ConfirmedLeague = league;
        LiveSelected = true;
        Message = $"Live profile on {league}. Demo prices are never shown here.";
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
