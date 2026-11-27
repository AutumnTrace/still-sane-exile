using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using StillSaneExile.Domain.Pricing;

namespace StillSaneExile.Desktop.Views;

public enum ValueLabelSize
{
    /// <summary>Plain text in the surrounding font: only the currency words become pictures.</summary>
    Text,
    Small,
    Medium,
    Large,
}

/// <summary>A price drawn like a loot-filter label: a solid tag whose colours say how much it is worth
/// (<see cref="ValueTier"/>), with each currency as its game picture instead of "div", "ex" or "c". With no
/// tier (a status such as "No matches", or no rate to place the price) it draws the text plain. Screen
/// readers hear the currencies by name: "2.1–2.8 Divine".</summary>
public sealed partial class ValueLabel : Border
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(ValueLabel), new FrameworkPropertyMetadata("", (d, _) => ((ValueLabel)d).Build()));

    public static readonly DependencyProperty TierProperty = DependencyProperty.Register(
        nameof(Tier), typeof(ValueTier?), typeof(ValueLabel), new FrameworkPropertyMetadata(null, (d, _) => ((ValueLabel)d).Build()));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(ValueLabelSize), typeof(ValueLabel), new FrameworkPropertyMetadata(ValueLabelSize.Medium, (d, _) => ((ValueLabel)d).Build()));

    private readonly TextBlock _text = new() { TextWrapping = TextWrapping.Wrap };

    public ValueLabel()
    {
        Child = _text;
        SnapsToDevicePixels = true;
        HorizontalAlignment = HorizontalAlignment.Left;
        Build();

        // The theme's colours and the inherited font size are known once it is in a window.
        Loaded += (_, _) => Build();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public ValueTier? Tier
    {
        get => (ValueTier?)GetValue(TierProperty);
        set => SetValue(TierProperty, value);
    }

    public ValueLabelSize Size
    {
        get => (ValueLabelSize)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>The text cut where a currency word follows a number: "95 ex – 1.2 div" gives "95", ex,
    /// " – 1.2", div. Other text ("No matches", "check rolls") stays as it is.</summary>
    public static IReadOnlyList<(string Text, string? Currency)> Split(string text)
    {
        var parts = new List<(string, string?)>();
        var at = 0;
        foreach (Match m in Money().Matches(text))
        {
            parts.Add((text[at..m.Index], null));
            parts.Add(("", CurrencyIcon.FromShort(m.Groups[1].Value)));
            at = m.Index + m.Length;
        }

        if (at < text.Length)
        {
            parts.Add((text[at..], null));
        }

        return parts.Where(p => p.Item1.Length > 0 || p.Item2 is not null).ToList();
    }

    /// <summary>What a screen reader says: the currency words spelled out.</summary>
    public static string Spoken(string text) => string.Concat(Split(text).Select(p => p.Currency is { } c ? $" {Currencies.Word(c)}" : p.Text));

    [GeneratedRegex(@"(?<=\d)\s(div|ex|c)(?=$|[\s·,.;)])")]
    private static partial Regex Money();

    private void Build()
    {
        var size = Size;
        var tier = Tier;
        var plain = size == ValueLabelSize.Text;
        var (fontSize, padding, border, icon) = size switch
        {
            ValueLabelSize.Large => (24.0, new Thickness(12, 5, 12, 5), 2.0, 22.0),
            ValueLabelSize.Medium => (16.0, new Thickness(8, 3, 8, 3), 1.5, 16.0),
            ValueLabelSize.Small => (13.0, new Thickness(5, 1, 5, 1), 1.0, 13.0),
            _ => (0.0, new Thickness(0), 0.0, 0.0),
        };

        _text.Inlines.Clear();
        if (plain)
        {
            _text.ClearValue(TextBlock.FontFamilyProperty);
            _text.ClearValue(TextBlock.FontSizeProperty);
            _text.ClearValue(TextBlock.FontWeightProperty);
            _text.ClearValue(TextBlock.ForegroundProperty);
            Background = null;
            BorderBrush = null;
            BorderThickness = new Thickness(0);
            Padding = new Thickness(0);
        }
        else
        {
            _text.FontFamily = (FontFamily)(TryFindResource("CapsFont") ?? new FontFamily("Segoe UI"));
            _text.FontSize = fontSize;
            _text.FontWeight = FontWeights.ExtraBold;
            var (fill, ink, edge) = Colours(tier);
            Background = fill;
            BorderBrush = edge;
            _text.Foreground = ink;
            BorderThickness = tier is null ? new Thickness(0) : new Thickness(border);
            Padding = tier is null ? new Thickness(0) : padding;
        }

        foreach (var (text, currency) in Split(Text ?? ""))
        {
            if (currency is null)
            {
                _text.Inlines.Add(new Run(text));
                continue;
            }

            // In running text the picture follows the text's size; in a label, the label's.
            var side = plain ? Math.Round(_text.FontSize * 1.2) : icon;
            _text.Inlines.Add(new InlineUIContainer(new CurrencyIcon { Currency = currency, Width = side, Height = side, Margin = new Thickness(plain ? 2 : 3, 0, 0, 0) })
            {
                BaselineAlignment = BaselineAlignment.Center,
            });
        }

        AutomationProperties.SetName(this, Spoken(Text ?? ""));
    }

    private (Brush? Fill, Brush Ink, Brush? Edge) Colours(ValueTier? tier)
    {
        if (tier is not { } t)
        {
            return (null, Find("Text"), null);
        }

        var key = $"Tier{t}";
        return (Find(key + "Fill"), Find(key + "Ink"), Find(key + "Edge"));
    }

    private Brush Find(string key) => TryFindResource(key) as Brush ?? Brushes.White;
}
