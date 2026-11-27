using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Desktop.Views;
using StillSaneExile.Domain.Capture;

namespace StillSaneExile.Desktop.Tests;

/// <summary>Stands in for Windows.Graphics.Capture: a small solid frame per call, counted. With
/// <see cref="HoldUntilReleased"/>, the frame is handed over only when the test calls <see cref="Release"/>,
/// like a capture still on its way.</summary>
internal sealed class FakeStashSource : IStashFrameSource
{
    public static readonly GameWindowFacts Window = new(64, 36, "borderless fullscreen", "Test monitor", 64, 36, 125, false, 1.0);

    private readonly SemaphoreSlim _go = new(0);
    private readonly ConditionalWeakTable<Frame, CaptureFacts> _facts = [];
    private int _calls, _prepares, _releases;

    public bool HoldUntilReleased { get; set; }

    public bool Black { get; set; }

    public bool ReturnNothing { get; set; }

    public int Calls => Volatile.Read(ref _calls);

    public int Prepares => Volatile.Read(ref _prepares);

    public int Releases => Volatile.Read(ref _releases);

    public List<nint> Windows { get; } = [];

    public List<Frame> Handed { get; } = [];

    public SemaphoreSlim Entered { get; } = new(0);

    public string Name => "fake capture";

    public string? Unsupported => null;

    public string? LastProblem { get; private set; }

    public void Prepare() => Interlocked.Increment(ref _prepares);

    public void Release() => Interlocked.Increment(ref _releases);

    public void Dispose()
    {
    }

    public void Let() => _go.Release(100);

    public CaptureFacts? FactsFor(Frame frame) => _facts.TryGetValue(frame, out var f) ? f : null;

    public Frame? CaptureOnce(nint window)
    {
        Interlocked.Increment(ref _calls);
        lock (Windows)
        {
            Windows.Add(window);
        }

        Entered.Release();
        if (HoldUntilReleased && !_go.Wait(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException("The test never let the fake capture finish.");
        }

        if (ReturnNothing)
        {
            LastProblem = "Windows gave no picture of the game within 500 ms";
            return null;
        }

        var (w, h) = (Window.ClientWidth, Window.ClientHeight);
        var pixels = new byte[w * h * 4];
        if (!Black)
        {
            for (var i = 0; i < w * h; i++)
            {
                (pixels[i * 4], pixels[i * 4 + 1], pixels[i * 4 + 2], pixels[i * 4 + 3]) = ((byte)(i % 200), 90, 160, 255);
            }
        }

        var frame = new Frame(w, h, w * 4, FramePixelFormat.Bgra8, pixels, Stopwatch.GetTimestamp());
        _facts.Add(frame, new CaptureFacts(Name, Window, "B8G8R8A8UIntNormalized", 9.5));
        lock (Handed)
        {
            Handed.Add(frame);
        }

        return frame;
    }
}

internal sealed class FakeToast : IStashToast
{
    public List<string> Shown { get; } = [];

    public nint ShownOver { get; private set; }

    public int Hides { get; private set; }

    public void Show(string message, nint over)
    {
        Shown.Add(message);
        ShownOver = over;
    }

    public void Hide() => Hides++;
}

/// <summary>A started <see cref="StashCollector"/> wired to fakes and a temp data folder, on the current STA thread.</summary>
internal sealed class StashRig : IDisposable
{
    public StashRig(Action<Settings>? configure = null, IStashFrameSource? source = null)
    {
        App = new TestApp(configure);
        try
        {
            Frames = source ?? Source;
            Collector = new StashCollector(App.Shell.StashPictures, "9.9.9-test", Keys, Foreground, Frames, Toast, Dispatcher.CurrentDispatcher);
            Collector.Start();
            Sta.Drain();
        }
        catch
        {
            App.Dispose();
            throw;
        }
    }

    public TestApp App { get; }
    public FakeKeys Keys { get; } = new();
    public FakeForeground Foreground { get; } = new();
    public FakeStashSource Source { get; } = new();
    public IStashFrameSource Frames { get; }
    public FakeToast Toast { get; } = new();
    public StashCollector Collector { get; }
    public ViewModels.StashPicturesViewModel Pictures => App.Shell.StashPictures;
    public Hotkey Key => Pictures.Key;

    public void Press(bool injected = false) => Keys.Press(Key, injected);

    public void Release(bool injected = false) => Keys.Release(Key, injected);

    /// <summary>A quick tap of the stash key, then waits until its picture is saved (or dropped).</summary>
    public void Tap()
    {
        Press();
        Release();
        Settle();
    }

    /// <summary>Runs the queued capture and waits for it and its save to finish.</summary>
    public void Settle()
    {
        Sta.Drain();
        Sta.Until(() => !Collector.IsBusy, "the stash picture to be taken and saved");
        Sta.Drain();
    }

    /// <summary>Every file under the pictures folder, relative to it.</summary>
    public List<string> Files() =>
        Directory.Exists(App.Paths.Captures)
            ? Directory.EnumerateFiles(App.Paths.Captures, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(App.Paths.Captures, f)).Order(StringComparer.Ordinal).ToList()
            : [];

    public void Dispose()
    {
        Collector.Dispose();
        App.Dispose();
    }
}
