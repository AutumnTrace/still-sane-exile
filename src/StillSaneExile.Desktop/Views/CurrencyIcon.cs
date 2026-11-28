using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StillSaneExile.Desktop.Views;

/// <summary>A currency's own picture from the game: the Divine, Exalted and Chaos Orbs next to prices, and the
/// orbs, essences and omens of the craft simulator. The pictures ship with the app (Assets/Currency, fetched
/// once by scripts/update-currency-icons.ps1): drawing one never makes a request. Decorative: the price or
/// the button next to it carries the name for screen readers.</summary>
public sealed class CurrencyIcon : Image
{
    public static readonly DependencyProperty CurrencyProperty = DependencyProperty.Register(
        nameof(Currency), typeof(string), typeof(CurrencyIcon), new FrameworkPropertyMetadata(null, (d, _) => ((CurrencyIcon)d).Update()));

    private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    // 16 px unless told otherwise, as a default rather than a value set here: a value set in the constructor
    // would outrank a size given inside a template, and every picture in a list would stay 16 px.
    static CurrencyIcon()
    {
        WidthProperty.OverrideMetadata(typeof(CurrencyIcon), new FrameworkPropertyMetadata(16.0));
        HeightProperty.OverrideMetadata(typeof(CurrencyIcon), new FrameworkPropertyMetadata(16.0));
    }

    public CurrencyIcon()
    {
        Stretch = Stretch.Uniform;
        Focusable = false;
        SnapsToDevicePixels = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    /// <summary>poe.ninja's id of a picture the app ships ("divine", "transmute", "omen-of-whittling"); anything
    /// else shows nothing.</summary>
    public string? Currency
    {
        get => (string?)GetValue(CurrencyProperty);
        set => SetValue(CurrencyProperty, value);
    }

    /// <summary>The currency id for a short name in a price ("div", "ex", "c"), or null.</summary>
    public static string? FromShort(string word) => word switch
    {
        "div" => "divine",
        "ex" => "exalted",
        "c" => "chaos",
        _ => null,
    };

    /// <summary>The picture for a currency id, or null when there is none.</summary>
    public static ImageSource? Picture(string? id)
    {
        if (id is null || FileFor(id) is not { } file)
        {
            return null;
        }

        lock (Cache)
        {
            if (!Cache.TryGetValue(file, out var source))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri($"pack://application:,,,/StillSaneExile;component/Assets/Currency/{file}.png", UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                source = bitmap;
                Cache[file] = source;
            }

            return source;
        }
    }

    /// <summary>The pictures in Assets/Currency, named after poe.ninja's ids (the script fetches the same list).</summary>
    private static readonly HashSet<string> Shipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "divine", "exalted", "chaos", "transmute", "aug", "regal", "alch", "annul", "fracturing-orb", "vaal",
        "essence-of-abrasion", "essence-of-haste",
        "omen-of-sinistral-exaltation", "omen-of-dextral-exaltation", "omen-of-greater-exaltation", "omen-of-whittling",
        "omen-of-sinistral-erasure", "omen-of-dextral-erasure", "omen-of-sinistral-annulment", "omen-of-dextral-annulment",
    };

    private static string? FileFor(string id) => Shipped.Contains(id) ? id.ToLowerInvariant() : null;

    private void Update()
    {
        Source = Picture(Currency);
        Visibility = Source is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
