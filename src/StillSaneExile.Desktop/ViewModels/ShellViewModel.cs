using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Domain.Pricing;

namespace StillSaneExile.Desktop.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    public ShellViewModel(AppPaths paths, Store store, Settings settings, IClipboardSource clipboard, UpdateService updates)
    {
        Paths = paths;
        Store = store;
        Settings = settings;
        Updates = updates;
        Session = Session.Create(store, settings, paths);
        TradeSite = new TradeSiteSwitch(this);
        Inspect = new InspectViewModel(this, clipboard);
        Craft = PlaceholderViewModel.Craft();
        StashPictures = new StashPicturesViewModel(this);
        Wealth = PlaceholderViewModel.Wealth();
        Guide = new GuideViewModel(this);
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

    private LivePrices? _prices;

    /// <summary>The hourly poe.ninja refresh. Null in tests and while rendering snapshots, so neither ever
    /// fetches. Setting it starts tracking the session's league.</summary>
    public LivePrices? Prices
    {
        get => _prices;
        set
        {
            if (ReferenceEquals(_prices, value))
            {
                return;
            }

            if (_prices is not null)
            {
                _prices.BookReady -= OnLiveBook;
            }

            _prices = value;
            if (value is not null)
            {
                value.BookReady += OnLiveBook;
            }

            SettingsPage.UsePrices(value);
            TrackPrices();
        }
    }

    /// <summary>Searches the trade site when the player clicks Search. Null in tests and while rendering
    /// snapshots, so neither ever reaches the site; the HUD holds no reference to it.</summary>
    public LiveListings? Listings { get; set; }

    /// <summary>What each price check searches through: <see cref="Listings"/> while "Search the trade site
    /// from SSE" is on, otherwise nothing.</summary>
    public ITradeSite TradeSite { get; }

    /// <summary>The league to keep prices for: the session's, when it is live and live prices are on.</summary>
    private string? LiveLeague => !Session.IsDemo && Session.LivePricesOn ? Session.League : null;

    private void TrackPrices() => Prices?.Track(LiveLeague, Session.Book);

    private void OnLiveBook(PriceBook book) => ApplyLiveBook(book);

    /// <summary>Swaps in a book that just arrived, in one assignment, if it is still for this session's
    /// league; a late book for a league the player has left returns false and changes nothing. Unlike
    /// <see cref="ReloadSession"/> it leaves Settings alone, so a refresh never wipes what is being typed there.</summary>
    public bool ApplyLiveBook(PriceBook book)
    {
        if (LiveLeague is not { } league || !string.Equals(book.League, league, StringComparison.Ordinal)
            || !string.Equals(Settings.LiveLeague, league, StringComparison.Ordinal))
        {
            return false;
        }

        Session = Session.Create(Store, Settings, Paths, book);
        Inspect.Rebuild();
        SettingsPage.OnPricesChanged();
        return true;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDemo), nameof(League), nameof(LeagueLabel), nameof(DisplayCurrency), nameof(CurrencyWord), nameof(PackStatus), nameof(PriceStatus), nameof(FooterStatus), nameof(FooterDetail))]
    public partial Session Session { get; private set; }

    public InspectViewModel Inspect { get; }
    public PlaceholderViewModel Craft { get; }

    /// <summary>"Collect stash pictures": the switch and the pictures to label, in Settings.</summary>
    public StashPicturesViewModel StashPictures { get; }

    /// <summary>My wealth is switched off for now: a page that says so.</summary>
    public PlaceholderViewModel Wealth { get; }
    public GuideViewModel Guide { get; }
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
                OnPropertyChanged(nameof(PageName));
            }
        }
    }

    public string PageName => CurrentPage switch
    {
        InspectViewModel => "Price check",
        PlaceholderViewModel p => p.Title,
        GuideViewModel => "Campaign guide",
        SettingsViewModel => "Settings",
        _ => "",
    };

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
    public partial string Diagnostics { get; set; } = "Reads 0 · game inputs sent 0 · market requests from keys 0";

    public void Navigate(object page)
    {
        if (ReferenceEquals(page, CurrentPage))
        {
            return;
        }

        CurrentPage = page;
        if (page == SettingsPage)
        {
            SettingsPage.OnShown();
        }
    }

    /// <summary>Settings, open on one of its sections (<see cref="SettingsViewModel.Sections"/>).</summary>
    public void OpenSettings(string section)
    {
        SettingsPage.ShowSectionCommand.Execute(section);
        Navigate(SettingsPage);
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
        TrackPrices();
        Inspect.Rebuild();
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
    private void OpenLeagueSetting() => OpenSettings("general");

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

    /// <summary>Reads the switch and the client at the moment of each Search, so a price check never keeps
    /// a client the player has switched off.</summary>
    private sealed class TradeSiteSwitch(ShellViewModel shell) : ITradeSite
    {
        public bool IsOn => shell.Listings is not null && shell.Settings.TradeSearch;

        public Task<Domain.Trade.TradePage> SearchAsync(string league, string queryJson, bool buyNowOnly, CancellationToken cancellationToken = default) =>
            IsOn ? shell.Listings!.SearchAsync(league, queryJson, buyNowOnly, cancellationToken)
                : Task.FromException<Domain.Trade.TradePage>(new InvalidOperationException("Searching the trade site from SSE is off."));
    }
}
