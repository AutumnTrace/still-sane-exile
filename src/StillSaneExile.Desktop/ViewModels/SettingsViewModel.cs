using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Domain.Capture;
using StillSaneExile.Domain.Pricing;

namespace StillSaneExile.Desktop.ViewModels;

/// <summary>Settings and first-run onboarding (same page, different heading).</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;

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

    [ObservableProperty]
    public partial string TestLog { get; set; } = "Press any key while this page is open to see press, hold and release.";

    [ObservableProperty]
    public partial string? Message { get; set; }

    public IReadOnlyList<string> CurrencyChoices => Currencies.Display;

    public string DataFolder => _shell.Paths.DataDir + (_shell.Paths.Portable ? " (portable)" : "");

    public IReadOnlyList<string> KeyWarnings
    {
        get
        {
            var list = new List<string>();
            var keys = new[] { ("Item peek", PeekKey), ("Open last item", OpenLastKey), ("Stash labels", StashKey) };
            foreach (var g in keys.GroupBy(k => k.Item2, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            {
                list.Add($"{string.Join(" and ", g.Select(x => x.Item1))} share {g.Key}. Give each its own key.");
            }

            if (StashKey.Equals("RightAlt", StringComparison.OrdinalIgnoreCase) && _shell.Hud?.AltGrDetected == true)
            {
                list.Add("Your layout uses Right Alt as AltGr. Pick another stash key so typing is not affected.");
            }

            return list;
        }
    }

    public string ReadsExplanation =>
        (PeekKey.Equals("Ctrl+C", StringComparison.OrdinalIgnoreCase)
            ? "Ctrl+C is the game's own copy key, so one press copies and peeks (Ctrl+Alt+C too, for the advanced copy). The game still copies; the app only listens.\n"
            : "") +
        $"{PeekKey}: reads the clipboard once, right after the game copies the hovered item. If the game copied nothing, nothing is read.\n" +
        $"{OpenLastKey}: reads nothing; brings this window forward with the last item.\n" +
        $"{StashKey}: reserved for stash and Aldur labels (Milestone 2). Not active yet; it will read one frame of the game window per press, with your opt-in.";

    public void Load()
    {
        LiveSelected = S.Profile == ProfileKind.Live && !string.IsNullOrWhiteSpace(S.LiveLeague);
        ConfirmedLeague = string.IsNullOrWhiteSpace(S.LiveLeague) ? null : S.LiveLeague;
        LeagueDraft = ConfirmedLeague ?? "";
        PeekKey = S.PeekKey.ToString();
        OpenLastKey = S.OpenLastKey.ToString();
        StashKey = S.StashKey.ToString();
        ToggleMode = S.PeekMode == HoldMode.Toggle;
        Anchor = S.Anchor;
        PeekScale = S.PeekScale;
        ExpiryDays = S.ComparableExpiryDays.ToString(CultureInfo.InvariantCulture);
        AutoUpdate = S.AutoUpdate;
        IsOnboarding = !S.Onboarded;
        OnPropertyChanged(nameof(CurrentCurrency));
        OnPropertyChanged(nameof(ReadsExplanation));
        OnPropertyChanged(nameof(KeyWarnings));
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
        S.Profile = ProfileKind.Demo;
        _shell.ReloadSession();
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
        Message = "Press the new key combination now. Esc cancels.";
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

        var binding = new Hotkey(KeyInterop.VirtualKeyFromKey(key), mods.HasFlag(ModifierKeys.Control), mods.HasFlag(ModifierKeys.Shift), mods.HasFlag(ModifierKeys.Alt) && key != Key.RightAlt);
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
        Message = $"Saved {binding}.";
        Load();
        OnPropertyChanged(nameof(KeyWarnings));
        OnPropertyChanged(nameof(ReadsExplanation));
        _shell.Hud?.ApplySettings();
        return true;
    }

    [RelayCommand]
    private void ResetDemoData()
    {
        _shell.Store.ResetDemo();
        Message = "Demo comparables you added were removed. The seeded demo data stays.";
        _shell.ReloadSession();
    }

    [RelayCommand]
    private void FinishOnboarding()
    {
        S.Onboarded = true;
        IsOnboarding = false;
        _shell.GoInspectCommand.Execute(null);
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
