using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using CommunityToolkit.Mvvm.Input;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.ViewModels;

namespace StillSaneExile.Desktop.Views;

/// <summary>The price check over the game, opened with F3 after a peek. Unlike the peek card it takes
/// focus, because it has boxes to tick and numbers to type. Esc, the close button, F3 again or clicking
/// back into the game closes it, and focus goes back to the game.</summary>
public partial class PriceCheckWindow : Window
{
    private nint _gameWindow;
    private bool _hiding;

    public PriceCheckWindow()
    {
        InitializeComponent();
        View.CloseCommand = new RelayCommand(HideWindow);
    }

    public void ShowFor(ItemViewModel item, nint gameWindow)
    {
        _gameWindow = gameWindow;
        DataContext = item;
        Opacity = 0;
        Show();
        Place();
        Opacity = 1;
        Activate();
        View.Focus();
    }

    public void HideWindow()
    {
        if (_hiding || !IsVisible)
        {
            return;
        }

        _hiding = true;
        var active = IsActive;
        Hide();
        DataContext = null;
        if (active && _gameWindow != 0 && NativeMethods.IsWindow(_gameWindow))
        {
            NativeMethods.SetForegroundWindow(_gameWindow);
        }

        _hiding = false;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            HideWindow();
        }
    }

    /// <summary>Clicking back into the game (or anywhere else) closes it, so it never covers the game unseen.</summary>
    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (!_hiding && IsVisible)
        {
            _hiding = true;
            Hide();
            DataContext = null;
            _hiding = false;
        }
    }

    /// <summary>Left side of the game window (the inventory is on the right), as tall as the game allows.
    /// Positions are physical pixels.</summary>
    private void Place()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (_gameWindow == 0 || !NativeMethods.GetWindowRect(_gameWindow, out var area) || area.Width <= 0 || NativeMethods.IsIconic(_gameWindow))
        {
            var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var info = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            NativeMethods.GetMonitorInfo(monitor, ref info);
            area = info.rcWork;
        }

        var dpi = _gameWindow != 0 ? NativeMethods.GetDpiForWindow(_gameWindow) : NativeMethods.GetDpiForWindow(hwnd);
        var factor = dpi > 0 ? dpi / 96.0 : 1.0;
        var margin = (int)(area.Height * 0.02);
        Scroller.MaxHeight = Math.Max(240, (area.Height - 2 * margin) / factor - 32);
        UpdateLayout();
        var x = area.Left + (int)(area.Width * 0.03);
        var y = area.Top + margin;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, x, y, 0, 0, NativeMethods.SWP_NOSIZE);
    }
}
