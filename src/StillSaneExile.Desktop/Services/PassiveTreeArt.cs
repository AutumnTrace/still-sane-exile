using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Domain.Guide;

namespace StillSaneExile.Desktop.Services;

/// <summary>One picture from the passive tree's sheets, with its size in tree units.</summary>
public sealed record TreeSprite(BitmapSource Image, double Width, double Height);

/// <summary>The passive tree's pictures, from <c>data/tree/art/</c>: GGG's export sheets as JPEG and PNG,
/// which WPF reads on every Windows. They ship with the app; nothing is downloaded. Loaded once, the first
/// time a tree is drawn; a missing or broken file means no art, and the tree is drawn with plain shapes.</summary>
public sealed class PassiveTreeArt
{
    private static readonly Lock Gate = new();
    private static (string Folder, PassiveTreeArt? Art)? _loaded;

    private readonly IReadOnlyDictionary<string, BitmapSource> _sheets;
    private readonly Dictionary<string, TreeSprite?> _sprites = new(StringComparer.Ordinal);

    private PassiveTreeArt(TreeAtlas atlas, IReadOnlyDictionary<string, BitmapSource> sheets)
    {
        Atlas = atlas;
        _sheets = sheets;
    }

    public TreeAtlas Atlas { get; }

    public static PassiveTreeArt? Load(AppPaths paths)
    {
        lock (Gate)
        {
            if (_loaded is { } l && l.Folder == paths.PassiveTreeArt)
            {
                return l.Art;
            }

            var art = Read(paths.PassiveTreeArt);
            _loaded = (paths.PassiveTreeArt, art);
            return art;
        }
    }

    /// <summary>A picture by its name in the export ("frame:PSSkillFrame"), or null when the sheets do not
    /// have it. Cut out once and kept.</summary>
    public TreeSprite? Get(string name)
    {
        if (_sprites.TryGetValue(name, out var known))
        {
            return known;
        }

        TreeSprite? sprite = null;
        if (Atlas.Find(name) is { } found && _sheets.TryGetValue(found.Frame.Sheet, out var sheet)
            && found.Frame.X + found.Frame.Width <= sheet.PixelWidth && found.Frame.Y + found.Frame.Height <= sheet.PixelHeight)
        {
            var f = found.Frame;
            var image = new CroppedBitmap(sheet, new Int32Rect(f.X, f.Y, f.Width, f.Height));
            image.Freeze();
            sprite = new TreeSprite(image, f.Width * found.Sheet.Units, f.Height * found.Sheet.Units);
        }

        _sprites[name] = sprite;
        return sprite;
    }

    internal static PassiveTreeArt? Read(string folder)
    {
        try
        {
            var atlas = TreeAtlas.Parse(File.ReadAllText(Path.Combine(folder, "atlas.json")));
            var sheets = new Dictionary<string, BitmapSource>(StringComparer.Ordinal);
            foreach (var (name, sheet) in atlas.Sheets)
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                image.UriSource = new Uri(Path.GetFullPath(Path.Combine(folder, sheet.File)));
                image.EndInit();
                image.Freeze();
                sheets[name] = image;
            }

            return new PassiveTreeArt(atlas, sheets);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or NotSupportedException or UriFormatException or InvalidOperationException)
        {
            return null;
        }
    }
}
