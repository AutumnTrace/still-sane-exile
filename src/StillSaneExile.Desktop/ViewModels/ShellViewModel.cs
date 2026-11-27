using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Domain.Pricing;

namespace StillSaneExile.Desktop.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    private readonly Stack<object> _back = new();

    public ShellViewModel(AppPaths paths, Store store, Settings settings, IClipboardSource clipboard, UpdateService updates)
    {
        Paths = paths;
        Store = store;
        Settings = settings;
        Updates = updates;
        Session = Session.Create(store, settings, paths);
        Inspect = new InspectViewModel(this, clipboard);
        Craft = PlaceholderViewModel.Craft();
        Wealth = new WealthViewModel(this);
        Guide = PlaceholderViewModel.Guide();
        WhatsNew = new WhatsNewViewModel(settings, updates);
        SettingsPage = new SettingsViewModel(this);
        _currentPage = settings.Onboarded ? Inspect : SettingsPage;
        WhatsNew.PropertyChanged += OnWhatsNewInputChanged;
        SettingsPage.PropertyChanged += OnWhatsNewInputChanged;
    }

    public AppPaths Paths { get; }
    public Store Store { get; }
    public Settings Settings { get; }
    public UpdateService Updates { get; }
    public HudController? Hud { get; set; }
    public string VersionLabel => $"v{Updates.CurrentVersion}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDemo), nameof(League), nameof(LeagueLabel), nameof(DisplayCurrency), nameof(CurrencyWord), nameof(PackStatus), nameof(PriceStatus), nameof(FooterStatus), nameof(FooterDetail))]
    public partial Session Session { get; private set; }

    public InspectViewModel Inspect { get; }
    public PlaceholderViewModel Craft { get; }
    public WealthViewModel Wealth { get; }
    public PlaceholderViewModel Guide { get; }
    public SettingsViewModel SettingsPage { get; }
    public WhatsNewViewModel WhatsNew { get; }

    /// <summary>The "What's new" card sits above every page once opened, but never over onboarding: it
    /// waits until onboarding is done, and only then counts as seen.</summary>
    public bool ShowWhatsNew => WhatsNew.IsOpen && !SettingsPage.IsOnboarding;

    private object _currentPage;

    public object CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (SetProperty(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(PageName));
            }
        }
    }

    public string PageName => CurrentPage switch
    {
        InspectViewModel => "Price check",
        WealthViewModel => "My wealth",
        PlaceholderViewModel p => p.Title,
        SettingsViewModel => "Settings",
        _ => "",
    };

    public bool CanGoBack => _back.Count > 0;
    public bool IsDemo => Session.IsDemo;
    public string League => Session.League;

    /// <summary>The league as the header shows it: "Demo", or the live league's name.</summary>
    public string LeagueLabel => Session.IsDemo ? "Demo" : Session.League;

    public string DisplayCurrency => Currencies.Long(Session.DisplayCurrency);
    public string CurrencyWord => Currencies.Word(Session.DisplayCurrency);
    public string PackStatus => Session.PackStatus;
    public string PriceStatus => Session.PriceStatus;
    public string FooterStatus => Session.FooterStatus;
    public string FooterDetail => $"{PackStatus} · {PriceStatus}";
    public IReadOnlyList<string> CurrencyChoices => Currencies.Display;

    [ObservableProperty]
    public partial bool CurrencyMenuOpen { get; set; }

    /// <summary>Always 0: the app never sends input to the game. Shown so the promise is visible.</summary>
    [ObservableProperty]
    public partial int KeysSent { get; set; }

    [ObservableProperty]
    public partial int ItemsRead { get; set; }

    /// <summary>Every capture counter, for the status bar's tooltip.</summary>
    [ObservableProperty]
    public partial string Diagnostics { get; set; } = "Reads 0 · game inputs sent 0 · market requests 0";

    public void Navigate(object page)
    {
        if (ReferenceEquals(page, CurrentPage))
        {
            return;
        }

        _back.Push(CurrentPage);
        CurrentPage = page;
    }

    public void OpenCraftWith(ItemViewModel? item)
    {
        Craft.Context = item is null ? null : $"Item handed over from the price check: {item.Item.DisplayName} (snapshot {item.CapturedAt}). Crafting plans will start from this exact snapshot.";
        Navigate(Craft);
    }

    /// <summary>Rebuilds the session from settings and swaps it in one assignment, then re-reads items and
    /// holdings against it.</summary>
    public void ReloadSession()
    {
        Session = Session.Create(Store, Settings, Paths);
        Inspect.Rebuild();
        Wealth.Reload();
        SettingsPage.Load();
    }

    private void OnWhatsNewInputChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WhatsNewViewModel.IsOpen) or nameof(SettingsViewModel.IsOnboarding))
        {
            OnPropertyChanged(nameof(ShowWhatsNew));
            if (ShowWhatsNew)
            {
                WhatsNew.MarkShown();
            }
        }
    }

    [RelayCommand]
    private void GoInspect() => Navigate(Inspect);

    [RelayCommand]
    private void GoCraft() => Navigate(Craft);

    [RelayCommand]
    private void GoWealth() => Navigate(Wealth);

    [RelayCommand]
    private void GoGuide() => Navigate(Guide);

    [RelayCommand]
    private void GoSettings() => Navigate(SettingsPage);

    [RelayCommand]
    private void OpenLeagueSetting() => Navigate(SettingsPage);

    [RelayCommand]
    private void Back()
    {
        if (_back.Count > 0)
        {
            CurrentPage = _back.Pop();
        }
    }

    [RelayCommand]
    private void ToggleCurrencyMenu() => CurrencyMenuOpen = !CurrencyMenuOpen;

    [RelayCommand]
    private void RestartToUpdate() => Updates.RestartNow();

    [RelayCommand]
    private void SetCurrency(string currency)
    {
        Settings.SetDisplayCurrency(Session.Kind, currency);
        CurrencyMenuOpen = false;
        ReloadSession();
    }
}
