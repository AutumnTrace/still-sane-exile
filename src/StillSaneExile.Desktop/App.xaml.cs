using System.IO;
using System.Windows;
using System.Windows.Threading;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Desktop.ViewModels;
using StillSaneExile.Desktop.Views;

namespace StillSaneExile.Desktop;

public partial class App : Application
{
    private HudController? _hud;
    private StashCollector? _stash;
    private WgcFrameSource? _frames;
    private StashToastWindow? _stashToast;
    private PeekWindow? _peek;
    private PriceCheckWindow? _priceCheck;
    private GuideHud? _guideHud;
    private AppPaths? _paths;
    private UpdateService? _updates;
    private LivePrices? _prices;
    private LiveListings? _listings;
    private TextScale? _textScale;
    private bool _renderingSnapshots;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;

        var args = e.Args;
        string? Arg(string name)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        var snapshots = Arg("--render-snapshots");
        _renderingSnapshots = snapshots is not null;
        _updates = new UpdateService();
        _paths = new AppPaths(Arg("--data"), _updates.PortableRoot);
        var store = new Store(_paths.Database);
        var settings = new Settings(store);
        var clipboard = new ClipboardReader();
        var shell = new ShellViewModel(_paths, store, settings, clipboard, _updates);
        var main = new MainWindow { DataContext = shell };

        if (snapshots is not null)
        {
            // --text-scale 150 renders as if Windows' text size were 150 %; --window 1920x1032 sets the
            // main window's size (and the screen it must fit), by default the usual size times the scale.
            SnapshotRenderer.Render(shell, main, snapshots, SnapshotRenderer.ParseScale(Arg("--text-scale")), SnapshotRenderer.ParseSize(Arg("--window")));
            Shutdown();
            return;
        }

        _textScale = new TextScale();
        main.UseTextScale(_textScale);
        _peek = new PeekWindow();
        _priceCheck = new PriceCheckWindow();
        _priceCheck.UseTextScale(_textScale);
        var keys = new InputObserver();
        var foreground = new ForegroundWatcher();
        _hud = new HudController(shell, keys, foreground, clipboard, _peek, _priceCheck, main, new CursorPoller(main.Dispatcher));
        shell.Hud = _hud;

        // The stash key's own path, on the same key and foreground events: off until "Collect stash
        // pictures" is switched on, and then one frame of the game window per physical press.
        _frames = new WgcFrameSource();
        _stashToast = new StashToastWindow();
        _stash = new StashCollector(shell.StashPictures, _updates.CurrentVersion, keys, foreground, _frames, _stashToast, Dispatcher);
        shell.StashPictures.UseSource(_frames);

        main.Closed += (_, _) => Shutdown();
        MainWindow = main;

        // After an update: the notes wait in the main window, never on the HUD.
        shell.WhatsNew.CheckOnStart();
        main.Show();
        _hud.Start();
        _stash.Start();
        _guideHud = new GuideHud(shell.Guide, _peek, main);
        _guideHud.Start();
        _updates.Start(settings.AutoUpdate);

        // Hourly poe.ninja prices for a confirmed live league with live prices on; it does nothing until
        // then. The trade site search runs only when the player clicks Search on a live item. Both share
        // one governor (one cooldown file). Never created for --render-snapshots (returned above), and the
        // HUD holds no reference to either.
        var governor = LivePrices.Governor(_paths);
        _prices = LivePrices.Create(_paths, _updates.CurrentVersion, Dispatcher, governor);
        _listings = LiveListings.Create(governor, _updates.CurrentVersion);
        shell.Listings = _listings;
        shell.Prices = _prices;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _stash?.Dispose();
        _frames?.Dispose();
        _stashToast?.Close();
        _hud?.Dispose();
        _guideHud?.Dispose();
        _prices?.Dispose();
        _listings?.Dispose();
        _textScale?.Dispose();
        _peek?.Close();
        _priceCheck?.Close();
        _updates?.ApplyOnExit();
        base.OnExit(e);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            if (_paths is not null)
            {
                File.AppendAllText(Path.Combine(_paths.DataDir, "errors.log"), $"{DateTimeOffset.Now:O} {e.Exception}\n\n");
            }
        }
        catch (IOException)
        {
        }

        if (_renderingSnapshots)
        {
            // Unattended (the CI smoke run): a message box would wait forever. Exit with an error instead;
            // the exception is in errors.log above.
            e.Handled = true;
            Shutdown(1);
            return;
        }

        _peek?.HideCard();
        _priceCheck?.HideWindow();
        MessageBox.Show(e.Exception.Message, "Still Sane, Exile?", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
