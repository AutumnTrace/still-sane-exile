using System.IO;
using System.Reflection;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Velopack;
using Velopack.Sources;

namespace StillSaneExile.Desktop.Services;

/// <summary>Keeps an installed copy up to date from GitHub Releases. Checks at startup and every few
/// hours, downloads in the background, and installs when the app closes, so it never restarts in
/// the middle of a game unless the player presses "Restart now". The only network request this app
/// makes is this check (to github.com), and it can be turned off.</summary>
public sealed partial class UpdateService : ObservableObject
{
    public const string RepoUrl = "https://github.com/AutumnTrace/still-sane-exile";

    /// <summary>For testing an update locally: a folder with packages made by <c>vpk pack</c>.</summary>
    public const string SourceOverrideVariable = "SSE_UPDATE_SOURCE";

    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly UpdateManager _manager;
    private readonly DispatcherTimer _timer = new() { Interval = Interval };
    private UpdateInfo? _ready;
    private bool _busy;

    public UpdateService()
    {
        var overrideSource = Environment.GetEnvironmentVariable(SourceOverrideVariable);
        IUpdateSource source = !string.IsNullOrWhiteSpace(overrideSource) && Directory.Exists(overrideSource)
            ? new SimpleFileSource(new DirectoryInfo(overrideSource))
            : new GithubSource(RepoUrl, accessToken: null, prerelease: false);
        _manager = new UpdateManager(source);
        _timer.Tick += async (_, _) => await CheckAsync(userInitiated: false);

        CurrentVersion = _manager.CurrentVersion?.ToString() ?? AssemblyVersion();
        Status = !_manager.IsInstalled
            ? "Development build: updates apply to installed copies only."
            : _manager.UpdatePendingRestart is { } pending
                ? $"Update {pending.Version} is downloaded and installs when you close SSE."
                : $"Version {CurrentVersion}.";
        ReadyVersion = _manager.UpdatePendingRestart?.Version.ToString();
    }

    public bool IsInstalled => _manager.IsInstalled;

    public bool IsPortable => _manager.IsInstalled && _manager.IsPortable;

    /// <summary>Where a portable copy keeps its files (survives updates). Null when not portable.</summary>
    public string? PortableRoot => IsPortable ? Velopack.Locators.VelopackLocator.Current.RootAppDir : null;

    public string CurrentVersion { get; }

    [ObservableProperty]
    public partial string Status { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    public partial string? ReadyVersion { get; private set; }

    public bool IsReady => ReadyVersion is not null;

    public void Start(bool automatic)
    {
        if (!IsInstalled || !automatic)
        {
            _timer.Stop();
            return;
        }

        _timer.Start();
        _ = Dispatcher.CurrentDispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            await CheckAsync(userInitiated: false);
        });
    }

    public async Task CheckAsync(bool userInitiated)
    {
        if (!IsInstalled)
        {
            Status = "Development build: updates apply to installed copies only.";
            return;
        }

        if (_busy || (IsReady && !userInitiated))
        {
            return;
        }

        _busy = true;
        try
        {
            Status = "Checking for updates…";
            var info = await _manager.CheckForUpdatesAsync();
            if (info is null)
            {
                Status = $"Version {CurrentVersion} is the latest.";
                return;
            }

            var version = info.TargetFullRelease.Version.ToString();
            Status = $"Downloading {version}…";
            await _manager.DownloadUpdatesAsync(info, p => Status = $"Downloading {version}… {p}%");
            _ready = info;
            ReadyVersion = version;
            Status = $"Update {version} is ready and installs when you close SSE.";
        }
        catch (Exception ex)
        {
            Status = $"Could not check for updates: {ex.Message}";
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Called on a normal exit: hands the downloaded update to Velopack's updater, which
    /// installs it after this process ends. Silent, no restart.</summary>
    public void ApplyOnExit()
    {
        if (!IsInstalled || !IsReady)
        {
            return;
        }

        try
        {
            _manager.WaitExitThenApplyUpdates(_ready?.TargetFullRelease ?? _manager.UpdatePendingRestart, silent: true, restart: false);
        }
        catch (Exception)
        {
            // Velopack also applies a pending update on the next start.
        }
    }

    public void RestartNow()
    {
        if (IsInstalled && IsReady)
        {
            _manager.ApplyUpdatesAndRestart(_ready?.TargetFullRelease ?? _manager.UpdatePendingRestart);
        }
    }

    private static string AssemblyVersion()
    {
        var info = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var plus = info.IndexOf('+', StringComparison.Ordinal);
        return plus > 0 ? info[..plus] : info;
    }
}
