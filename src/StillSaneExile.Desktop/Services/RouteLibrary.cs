using System.IO;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Domain.Guide;

namespace StillSaneExile.Desktop.Services;

/// <summary>Loads the route pack that ships in <c>data/routes</c>. Every load goes through the
/// domain loader's checks. A pack that fails them is not used: the last good copy stays in use (in
/// memory, and across restarts from the copy saved in the data folder the last time it loaded), and
/// <see cref="Problem"/> says why. Reads local files only; nothing is downloaded.</summary>
public sealed class RouteLibrary(AppPaths paths)
{
    public const string DefaultFile = "act1-opening.json";

    public RoutePack? Pack { get; private set; }

    /// <summary>Why the shipped pack was not used, or null when it loaded.</summary>
    public string? Problem { get; private set; }

    public void Load()
    {
        var cached = Path.Combine(paths.RouteCache, DefaultFile);
        var inUse = Pack ?? (File.Exists(cached) ? RoutePackLoader.Load(File.ReadAllText(cached)).Pack : null);

        var shipped = Path.Combine(paths.RoutePacks, DefaultFile);
        if (!File.Exists(shipped))
        {
            (Pack, Problem) = (inUse, $"The route pack {DefaultFile} is missing from the app folder.");
            return;
        }

        var json = File.ReadAllText(shipped);
        var result = RoutePackLoader.Replace(inUse, json);
        (Pack, Problem) = (result.Pack, result.Error);
        if (result.Error is null)
        {
            try
            {
                Directory.CreateDirectory(paths.RouteCache);
                File.WriteAllText(cached, json);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The cache only protects against a later broken pack; the route still works without it.
            }
        }
    }
}
