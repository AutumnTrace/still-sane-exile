using System.Diagnostics;
using StillSaneExile.CaptureSpike.Capture;
using StillSaneExile.CaptureSpike.TestWindows;
using StillSaneExile.Domain.Capture;

namespace StillSaneExile.CaptureSpike;

/// <summary>The issue's instrumentation check on the real path: a key held for 10 seconds with this
/// machine's auto-repeat, fed into the real <see cref="CaptureGate"/> and <see cref="FrameReader"/>
/// in front of the real backend, against a test window this process owns. The key events are made
/// here, at the rate Windows would repeat them, because a synthetic press would be (rightly) ignored
/// by the gate; the live mode takes them from the keyboard hook instead.</summary>
internal static class Hold
{
    public const string Command = "stash-labels";

    public static async Task<int> RunAsync(Args args)
    {
        var seconds = args.Int("seconds", 10);
        var backend = args.Get("backend") ?? "wgc";
        using var window = Bench.OpenWindow(args.Get("window") ?? "d3d");
        using var source = Args.Source(backend, args.Format, args.Has("no-border"));
        var reader = new FrameReader(new CaptureGate(), Command, source);
        var (delay, rate) = Info.KeyRepeat();
        Console.WriteLine($"Holding a key for {seconds} s on {window.Description}: first repeat after {delay.TotalMilliseconds:0} ms, then {rate:0.#}/s. Backend: {source.Name}.");

        var reads = new List<Task<FrameRead<FrameStats>>>();
        var downs = 0;
        var clock = Stopwatch.StartNew();
        var next = TimeSpan.Zero;
        while (clock.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            var wait = next - clock.Elapsed;
            if (wait > TimeSpan.Zero)
            {
                Thread.Sleep(wait);
            }

            downs++;
            var d = reader.KeyDown(injected: false, allowedForeground: true);
            if (d.Action == GateAction.Read)
            {
                reads.Add(reader.ReadAsync(d, window.Handle, FrameStats.Of));
            }

            next += downs == 1 ? delay : TimeSpan.FromSeconds(1 / rate);
        }

        var up = reader.KeyUp(injected: false);
        var results = await Task.WhenAll(reads);
        Thread.Sleep(300); // a FrameArrived after close would be counted by now

        var g = reader.GateCounters;
        var r = reader.Counters;
        var s = source.Counters;
        Console.WriteLine($"Key-downs delivered: {downs} (1 press + {downs - 1} auto-repeat) over {clock.Elapsed.TotalSeconds:0.0} s, then 1 release ({up.Action}).");
        Console.WriteLine($"Gate:   reads {g.Reads}, auto-repeat ignored {g.AutoRepeatIgnored}, synthetic ignored {g.InjectedIgnored}, late results dropped {g.StaleResultsDropped}, game inputs sent {g.GameActionsSent}");
        Console.WriteLine($"Reader: {r}");
        Console.WriteLine($"Source: {s}");
        foreach (var res in results)
        {
            Console.WriteLine($"Result: {res.Outcome}, generation {res.Generation}, {(res.Value is { } v ? $"{v.Width}×{v.Height} {v.Format}, average {v.AverageHex}, checksum {v.Checksum}" : res.Error)}; {source.LastTiming}");
        }

        var pass = g.Reads == 1 && r.CaptureCalls == 1 && r.Frames == 1 && r.FramesInMemory == 0
            && s.Acquisitions == 1 && s.SessionsOpened == 1 && s.SessionsClosed == 1 && s.EventsAfterClose == 0 && g.AutoRepeatIgnored == downs - 1;
        Console.WriteLine(pass ? "PASS: one press, one frame." : "FAIL: more or fewer than one frame for one press.");
        return pass ? 0 : 1;
    }
}
