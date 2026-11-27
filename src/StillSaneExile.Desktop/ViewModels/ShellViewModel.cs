using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Domain.Pricing;

namespace StillSaneExile.Desktop.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    private readonly Stack<object> _back = new();

    public ShellViewModel(AppPaths paths, Store store, Settings settings, ClipboardReader clipboard)
    {
        Paths = paths;
        Store = store;
        Settings = settings;
        Session = Session.Create(store, settings, paths);
        Inspect = new InspectViewModel(this, clipboard);
        Craft = PlaceholderViewModel.Craft();
        Wealth = PlaceholderViewModel.Wealth();
        Guide = PlaceholderViewModel.Guide();
        SettingsPage = new SettingsViewModel(this);
        _currentPage = settings.Onboarded ? Inspect : SettingsPage;
    }

    public AppPaths Paths { get; }
    public Store Store { get; }
    public Settings Settings { get; }
    public HudController? Hud { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDemo), nameof(League), nameof(DisplayCurrency), nameof(ProfileBadge), nameof(PackStatus), nameof(PriceStatus))]
    public partial Session Session { get; private set; }

    public InspectViewModel Inspect { get; }
    public PlaceholderViewModel Craft { get; }
    public PlaceholderViewModel Wealth { get; }
    public PlaceholderViewModel Guide { get; }
    public SettingsViewModel SettingsPage { get; }

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
        InspectViewModel => "Inspect",
        PlaceholderViewModel p => p.Title,
        SettingsViewModel => "Settings",
        _ => "",
    };

    public bool CanGoBack => _back.Count > 0;
    public bool IsDemo => Session.IsDemo;
    public string League => Session.League;
    public string DisplayCurrency => Currencies.Long(Session.DisplayCurrency);
    public string ProfileBadge => Session.IsDemo ? "DEMO · synthetic data" : "LIVE";
    public string PackStatus => Session.PackStatus;
    public string PriceStatus => Session.PriceStatus;
    public IReadOnlyList<string> CurrencyChoices => Currencies.Display;

    [ObservableProperty]
    public partial bool CurrencyMenuOpen { get; set; }

    [ObservableProperty]
    public partial string Diagnostics { get; set; } = "";

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
        Craft.Context = item is null ? null : $"Item handed over from Inspect: {item.Item.DisplayName} (snapshot {item.CapturedAt}). Crafting plans will start from this exact snapshot.";
        Navigate(Craft);
    }

    /// <summary>Rebuilds the session from settings and swaps it in one assignment, then re-reads items against it.</summary>
    public void ReloadSession()
    {
        Session = Session.Create(Store, Settings, Paths);
        Inspect.Rebuild();
        SettingsPage.Load();
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
    private void SetCurrency(string currency)
    {
        Settings.SetDisplayCurrency(Session.Kind, currency);
        CurrencyMenuOpen = false;
        ReloadSession();
    }
}
