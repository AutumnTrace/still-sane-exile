using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace StillSaneExile.Desktop.Services;

/// <summary>Windows' text size (Settings, Accessibility, Text size: 100 to 225 %). WPF does not apply it
/// by itself, because SSE sets its font sizes explicitly, so SSE's windows scale their whole content by
/// it, as far as the window still fits its screen (<see cref="Fit"/>). Display scaling (DPI) applies on
/// top, as before. The quick price card keeps its own Scale setting and ignores this.</summary>
public sealed class TextScale : IDisposable
{
    public const string RegistryKey = @"Software\Microsoft\Accessibility";
    public const string RegistryValue = "TextScaleFactor";
    public const int MinPercent = 100;
    public const int MaxPercent = 225;

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly bool _follows;

    /// <summary>Reads the setting now and again whenever Windows reports a changed preference. Create it
    /// on the UI thread.</summary>
    public TextScale()
        : this(Read(), follows: true)
    {
    }

    private TextScale(double factor, bool follows)
    {
        Factor = factor;
        _follows = follows;
        if (follows)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }
    }

    /// <summary>Raised on the UI thread when the factor changes.</summary>
    public event EventHandler? Changed;

    /// <summary>1.0 to 2.25: the text size Windows asks for, before <see cref="Fit"/>.</summary>
    public double Factor { get; private set; }

    /// <summary>A fixed factor that ignores the Windows setting (snapshots and tests).</summary>
    public static TextScale Fixed(double factor) => new(Math.Clamp(factor, MinPercent / 100.0, MaxPercent / 100.0), follows: false);

    /// <summary>The registry value as a factor. Windows writes a DWORD percentage from 100 to 225, and
    /// none at all until the slider was first moved; anything missing or not a number reads as 100 %,
    /// and a number outside the range is clamped into it.</summary>
    public static double FromRegistry(object? value) => value switch
    {
        int percent => Math.Clamp(percent, MinPercent, MaxPercent) / 100.0,
        _ => 1.0,
    };

    /// <summary>The largest factor, up to <paramref name="factor"/>, at which a window whose content needs
    /// at least <paramref name="minimum"/> (device-independent pixels, unscaled) still fits
    /// <paramref name="available"/>. Never below 1: at 100 % the window behaves as it always has.</summary>
    public static double Fit(double factor, Size available, Size minimum)
    {
        if (factor <= 1 || minimum.Width <= 0 || minimum.Height <= 0)
        {
            return 1;
        }

        var room = Math.Min(available.Width / minimum.Width, available.Height / minimum.Height);
        return double.IsFinite(room) ? Math.Clamp(Math.Floor(room * 100) / 100, 1, factor) : factor;
    }

    public static double Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey);
            return FromRegistry(key?.GetValue(RegistryValue));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return 1.0;
        }
    }

    public void Dispose()
    {
        if (_follows)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }
    }

    /// <summary>Windows announces a new text size as a changed preference (category Accessibility or
    /// General, depending on the version), so any change re-reads the one value.</summary>
    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e) => _dispatcher.BeginInvoke(() =>
    {
        var factor = Read();
        if (factor != Factor)
        {
            Factor = factor;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    });
}
