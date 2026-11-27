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
    private PeekWindow? _peek;
    private PriceCheckWindow? _priceCheck;
    private AppPaths? _paths;
    private UpdateService? _updates;
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
            SnapshotRenderer.Render(shell, main, snapshots);
            Shutdown();
            return;
        }

        _peek = new PeekWindow();
        _priceCheck = new PriceCheckWindow();
        _hud = new HudController(shell, new InputObserver(), new ForegroundWatcher(), clipboard, _peek, _priceCheck, main);
        shell.Hud = _hud;

        main.Closed += (_, _) => Shutdown();
        MainWindow = main;

        // After an update: the notes wait in the main window, never on the HUD.
        shell.WhatsNew.CheckOnStart();
        main.Show();
        _hud.Start();
        _updates.Start(settings.AutoUpdate);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hud?.Dispose();
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
