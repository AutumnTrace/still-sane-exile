using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using StillSaneExile.Desktop.Infrastructure;

namespace StillSaneExile.Desktop.Views;

/// <summary>What the stash picture collector needs from its confirmation over the game, so it can be
/// tested with a fake.</summary>
public interface IStashToast
{
    /// <summary>Shows <paramref name="message"/> for a few seconds over <paramref name="over"/> (the game),
    /// without taking the focus.</summary>
    void Show(string message, nint over);

    void Hide();
}

/// <summary>A one-line note over the game ("Saved stash picture 7"): topmost, click-through, never
/// activated and never focusable, like the guide lines. It goes by itself after a few seconds, and
/// sooner when anything else comes to the front.</summary>
public partial class StashToastWindow : Window, IStashToast
{
    public static readonly TimeSpan ShowFor = TimeSpan.FromSeconds(2.5);

    private readonly DispatcherTimer _timer;

    public StashToastWindow()
    {
        InitializeComponent();
        _timer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = ShowFor };
        _timer.Tick += (_, _) => Hide();
    }

    public string Message
    {
        get => MessageText.Text;
        set => MessageText.Text = value;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        var style = ex | (nint)(NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, style);
    }

    public void Show(string message, nint over)
    {
        Message = message;
        _timer.Stop();
        Opacity = 0;
        Show();
        UpdateLayout();
        Place(over);
        Opacity = 1;
        _timer.Start();
    }

    public new void Hide()
    {
        _timer.Stop();
        base.Hide();
    }

    /// <summary>Top centre of the game, below where it draws its own top bar. Positions are physical pixels.</summary>
    private void Place(nint over)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (over == 0 || !NativeMethods.GetWindowRect(over, out var area) || area.Width <= 0 || NativeMethods.IsIconic(over))
        {
            var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var info = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            NativeMethods.GetMonitorInfo(monitor, ref info);
            area = info.rcWork;
        }

        var dpi = over != 0 ? NativeMethods.GetDpiForWindow(over) : NativeMethods.GetDpiForWindow(hwnd);
        var factor = dpi > 0 ? dpi / 96.0 : 1.0;
        var w = (int)Math.Ceiling(ActualWidth * factor);
        var x = area.Left + (area.Width - w) / 2;
        var y = area.Top + (int)(area.Height * 0.09);
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, x, y, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }
}
