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
    private AppPaths? _paths;
    private UpdateService? _updates;

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
        _hud = new HudController(shell, new KeyboardObserver(), new ForegroundWatcher(), clipboard, _peek, main);
        shell.Hud = _hud;

        main.Closed += (_, _) => Shutdown();
        MainWindow = main;
        main.Show();
        _hud.Start();
        _updates.Start(settings.AutoUpdate);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hud?.Dispose();
        _peek?.Close();
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

        _peek?.HideCard();
        MessageBox.Show(e.Exception.Message, "Still Sane, Exile?", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
