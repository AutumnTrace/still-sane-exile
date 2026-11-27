using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using StillSaneExile.CaptureSpike.Interop;

namespace StillSaneExile.CaptureSpike.TestWindows;

/// <summary>A WPF window (what SSE itself is) redrawing <see cref="Marker"/> on every WPF render pass.
/// WPF draws with Direct3D 9 into a DWM redirection surface, not a flip-model swap chain.</summary>
public sealed class WpfTestWindow : ITestWindow
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private Dispatcher? _dispatcher;
    private readonly long[] _renderedAt = new long[4096];
    private readonly System.Collections.Concurrent.ConcurrentDictionary<uint, int> _messages = new();
    private long _frames;
    private int _printMessages;
    private Exception? _failure;

    private WpfTestWindow()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "WPF test window" };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    public nint Handle { get; private set; }

    /// <summary>Render passes so far; the marker shows this number.</summary>
    public long Presented => Interlocked.Read(ref _frames);

    public int PrintMessages => Volatile.Read(ref _printMessages);

    public double Scale { get; private set; } = 1.0;

    public long RenderedAt(int marker) => Volatile.Read(ref _renderedAt[marker & 4095]);

    public int ClientWidth { get; private set; }

    public int ClientHeight { get; private set; }

    public string Description => $"WPF window {ClientWidth}×{ClientHeight}";

    public static WpfTestWindow Open()
    {
        var w = new WpfTestWindow();
        w._thread.Start();
        if (!w._ready.Wait(TimeSpan.FromSeconds(15)) || w._failure is not null)
        {
            throw new InvalidOperationException("The WPF test window could not start: " + w._failure?.Message, w._failure);
        }

        var wait = Stopwatch.StartNew();
        while (w.Presented < 30 && wait.Elapsed < TimeSpan.FromSeconds(5))
        {
            Thread.Sleep(10);
        }

        return w;
    }

    public void Stall(TimeSpan duration) => _dispatcher?.BeginInvoke(() => Thread.Sleep(duration));

    public void Dispose()
    {
        _dispatcher?.InvokeShutdown();
        _thread.Join(TimeSpan.FromSeconds(5));
        _ready.Dispose();
    }

    private void Run()
    {
        try
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            var canvas = new Canvas { Width = 960, Height = 540 };
            var background = new SolidColorBrush();
            canvas.Background = background;
            var marker = new Rectangle();
            canvas.Children.Add(marker);
            var window = new Window
            {
                Title = "SSE capture spike: WPF test window",
                Content = canvas,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                Topmost = true,
                ShowActivated = false,
                Left = 120,
                Top = 120,
            };
            var clock = Stopwatch.StartNew();
            window.Loaded += (_, _) =>
            {
                var dpi = VisualTreeHelper.GetDpi(window);
                Scale = dpi.DpiScaleX;
                // Place the marker in physical pixels, like the D3D window's.
                Canvas.SetLeft(marker, Marker.X / Scale);
                Canvas.SetTop(marker, Marker.Y / Scale);
                marker.Width = Marker.Size / Scale;
                marker.Height = Marker.Size / Scale;
                Handle = new WindowInteropHelper(window).Handle;
                HwndSource.FromHwnd(Handle).AddHook(Hook);
                Win32.GetClientRect(Handle, out var client);
                ClientWidth = client.Width;
                ClientHeight = client.Height;
                _ready.Set();
            };
            CompositionTarget.Rendering += (_, _) =>
            {
                var n = Interlocked.Increment(ref _frames);
                Volatile.Write(ref _renderedAt[n & 4095], Stopwatch.GetTimestamp());
                var (r, g, b) = Marker.Encode(n);
                marker.Fill = new SolidColorBrush(Color.FromRgb(r, g, b));
                var (br, bg, bb) = Marker.Background(clock.Elapsed.TotalSeconds);
                background.Color = Color.FromScRgb(1, Srgb(br), Srgb(bg), Srgb(bb));
            };
            window.Show();
            Dispatcher.Run();
            window.Close();
        }
        catch (Exception ex)
        {
            _failure = ex;
            _ready.Set();
        }
    }

    // Marker.Background is in display (sRGB) values; ScRgb takes linear ones.
    private static float Srgb(float v) => (float)StillSaneExile.Domain.Capture.FrameStats.DecodeSrgb((byte)Math.Round(v * 255));

    public IReadOnlyDictionary<uint, int> MessageCounts() => new Dictionary<uint, int>(_messages);

    private nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        _messages.AddOrUpdate((uint)msg, 1, (_, n) => n + 1);
        if (msg is (int)Win32.WM_PRINT or (int)Win32.WM_PRINTCLIENT)
        {
            Interlocked.Increment(ref _printMessages);
        }

        return 0;
    }
}
