using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using StillSaneExile.Domain.Items;

namespace StillSaneExile.Desktop.Views;

/// <summary>Visible when the value is "truthy": true, non-null, non-empty string or non-empty list.
/// ConverterParameter="invert" flips it.</summary>
public sealed class VisibleWhenConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var truthy = value switch
        {
            null => false,
            bool b => b,
            string s => s.Length > 0,
            ICollection c => c.Count > 0,
            IEnumerable e => e.GetEnumerator().MoveNext(),
            _ => true,
        };
        if (parameter is "invert")
        {
            truthy = !truthy;
        }

        return truthy ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Returns "active" when the value's text equals the parameter. Used by nav buttons and pill toggles.</summary>
public sealed class ActiveWhenEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase) ? "active" : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>"active" when all bound values are equal as text (a pill whose value equals the current choice).</summary>
public sealed class PillActiveConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length >= 2 && string.Equals(values[0]?.ToString(), values[1]?.ToString(), StringComparison.OrdinalIgnoreCase) ? "active" : "";

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Currency id → its name: "Divine Orb", or with ConverterParameter "short" → "div", "word" → "Divine".</summary>
public sealed class ShortCurrencyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => parameter switch
    {
        "short" => Domain.Pricing.Currencies.Short(value?.ToString() ?? ""),
        "word" => Domain.Pricing.Currencies.Word(value?.ToString() ?? ""),
        _ => Domain.Pricing.Currencies.Long(value?.ToString() ?? ""),
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Item rarity → the name colour the game uses. Rarity colours appear on item names only.</summary>
public sealed class RarityBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            ItemRarity.Magic => "RarityMagic",
            ItemRarity.Rare => "RarityRare",
            ItemRarity.Unique => "RarityUnique",
            ItemRarity.Currency => "RarityCurrency",
            ItemRarity.Gem => "RarityGem",
            _ => "Text",
        };
        return Application.Current.Resources[key];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
