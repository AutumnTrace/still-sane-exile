using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StillSaneExile.Desktop.Views;

/// <summary>A currency's own picture from the game (Divine, Exalted or Chaos Orb), so a price reads the way
/// the game shows money. The pictures ship with the app (Assets/Currency, fetched once by
/// scripts/update-currency-icons.ps1): drawing one never makes a request. Decorative: the price next to it
/// carries the name for screen readers.</summary>
public sealed class CurrencyIcon : Image
{
    public static readonly DependencyProperty CurrencyProperty = DependencyProperty.Register(
        nameof(Currency), typeof(string), typeof(CurrencyIcon), new FrameworkPropertyMetadata(null, (d, _) => ((CurrencyIcon)d).Update()));

    private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    public CurrencyIcon()
    {
        Stretch = Stretch.Uniform;
        Width = Height = 16;
        Focusable = false;
        SnapsToDevicePixels = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    /// <summary>"divine", "exalted" or "chaos"; anything else shows nothing.</summary>
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

    private static string? FileFor(string id) => id.ToLowerInvariant() switch
    {
        "divine" => "divine",
        "exalted" => "exalted",
        "chaos" => "chaos",
        _ => null,
    };

    private void Update()
    {
        Source = Picture(Currency);
        Visibility = Source is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
