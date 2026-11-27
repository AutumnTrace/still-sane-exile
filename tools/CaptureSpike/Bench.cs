using System.Diagnostics;
using System.Globalization;
using StillSaneExile.CaptureSpike.Capture;
using StillSaneExile.CaptureSpike.TestWindows;
using StillSaneExile.Domain.Capture;

namespace StillSaneExile.CaptureSpike;

/// <summary>Every backend against test windows this process owns: N single captures spaced like key
/// presses, each checked for size, content and the marker's frame number, then one capture while the
/// window's thread is busy. Captures nothing but its own windows.</summary>
internal static class Bench
{
    public static int Run(Args args)
    {
        var runs = args.Int("runs", 50);
        var interval = TimeSpan.FromMilliseconds(args.Int("interval", 100));
        var windows = args.List("windows", "d3d", "blt", "wpf", "borderless");
        var backends = args.List("backends", "wgc", "duplication", "duplication2", "printwindow");
        CaptureFormat[] formats = args.Has("format") ? [args.Format] : [CaptureFormat.Bgra8, CaptureFormat.RgbaHalf];
        var dump = args.DumpFolder;
        var rows = new List<Row>();

        Console.WriteLine($"{runs} captures per scenario, {interval.TotalMilliseconds:0} ms apart. Test windows are owned by this process; nothing else is captured.");
        foreach (var kind in windows)
        {
            using var window = OpenWindow(kind);
            Console.WriteLine($"== {window.Description}");
            foreach (var backend in backends)
            {
                foreach (var format in backend.StartsWith("print", StringComparison.OrdinalIgnoreCase) ? [CaptureFormat.Bgra8] : formats)
                {
                    using var source = Args.Source(backend, format, args.Has("no-border"));
                    if (source is WgcFrameSource { BorderAccess: { } access })
                    {
                        Console.WriteLine($"   Borderless capture requested: {access}");
                    }

                    var row = Measure(window, source, format, runs, interval, dump);
                    rows.Add(row);
                    Console.WriteLine("   " + row.Line());
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("| Window | Backend | Format | Frames OK | Marker at (8,8) | Content drawn before the press, ms (median, min..max) | Create→frame p50 / p95 / max ms | Whole call p50 / p95 ms | Capture live p50 ms | First call ms | OS acquisitions / calls | Messages the window handled per capture | Window thread busy 1 s |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var r in rows)
        {
            Console.WriteLine(r.Markdown());
        }

        return 0;
    }

    public static ITestWindow OpenWindow(string kind) => kind.ToLowerInvariant() switch
    {
        "d3d" => D3DTestWindow.Open(D3DWindowMode.Windowed),
        "blt" => D3DTestWindow.Open(D3DWindowMode.WindowedBlt),
        "borderless" => D3DTestWindow.Open(D3DWindowMode.Borderless),
        "wpf" => WpfTestWindow.Open(),
        _ => throw new ArgumentException($"Unknown window {kind}: use d3d, blt, wpf or borderless."),
    };

    private static Row Measure(ITestWindow window, ISpikeSource source, CaptureFormat format, int runs, TimeSpan interval, string? dump)
    {
        var hwnd = window.Handle;
        var printBefore = window.PrintMessages;
        var messagesBefore = window.MessageCounts();
        var acquire = new List<double>();
        var total = new List<double>();
        var live = new List<double>();
        var ages = new List<double>();
        int ok = 0, marker = 0, blank = 0;
        string? firstProblem = null;

        // The first call pays for creating the D3D device; it is reported apart.
        var cold = Once(window, source, dump, null, out _);
        var coldMs = source.LastTiming?.TotalMs ?? 0;
        Thread.Sleep(interval);

        for (var i = 0; i < runs; i++)
        {
            var check = Once(window, source, dump: null, firstProblem, out var problem);
            firstProblem ??= problem;
            if (check.Frame)
            {
                ok++;
                blank += check.Blank ? 1 : 0;
                if (check.AgeMs is { } a)
                {
                    marker++;
                    ages.Add(a);
                }
            }

            if (source.LastTiming is { } t && check.Frame)
            {
                acquire.Add(t.AcquireMs);
                total.Add(t.TotalMs);
                live.Add(t.LiveMs);
            }

            Thread.Sleep(interval);
        }

        var printDuringRuns = window.PrintMessages - printBefore;
        var messagesAfter = window.MessageCounts();
        var messages = string.Join(", ", messagesAfter
            .Select(m => (m.Key, Count: m.Value - messagesBefore.GetValueOrDefault(m.Key)))
            .Where(m => m.Count >= runs)
            .Select(m => $"0x{m.Key:X4}×{m.Count}"));

        // A busy game: the window's thread does not run for a second.
        window.Stall(TimeSpan.FromSeconds(1));
        Thread.Sleep(50);
        var busyStart = Stopwatch.GetTimestamp();
        string busy;
        using (var frame = source.CaptureOnce(hwnd))
        {
            var ms = Ms.Since(busyStart);
            busy = frame is null ? $"no frame after {ms:0} ms"
                : Marker.Read(frame) is { } seen ? $"frame after {ms:0} ms, drawn {Ms.Between(window.RenderedAt(seen), busyStart):0} ms before the press"
                : $"frame after {ms:0} ms, no marker";
        }

        // Let the stall end, and give a FrameArrived after a session closed time to be counted.
        Thread.Sleep(1500);
        return new Row(window.Description, source.Name, source.LastFormat, format, runs, ok, blank, marker, ages, acquire, total, live, coldMs, cold.Frame, source.Counters, printDuringRuns, messages, (source as DuplicationFrameSource)?.LastFrameInfo, busy, firstProblem);
    }

    /// <param name="AgeMs">How long before the press the captured content was drawn (negative: after it).</param>
    private readonly record struct Check(bool Frame, bool Blank, double? AgeMs);

    private static Check Once(ITestWindow window, ISpikeSource source, string? dump, string? known, out string? problem)
    {
        problem = null;
        var press = Stopwatch.GetTimestamp();
        using var frame = source.CaptureOnce(window.Handle);
        if (frame is null)
        {
            problem = source.LastTiming?.Note ?? "no frame";
            return new Check(false, false, null);
        }

        var stats = FrameStats.Of(frame);
        if (frame.Width != window.ClientWidth || frame.Height != window.ClientHeight)
        {
            problem = $"frame {frame.Width}×{frame.Height}, client {window.ClientWidth}×{window.ClientHeight}";
        }

        var seen = Marker.Read(frame);
        if (seen is null && known is null)
        {
            var p = FrameStats.PixelAt(frame, Marker.X + 2, Marker.Y + 2);
            problem ??= stats.IsBlank ? "frame is black" : $"no marker at (8,8): pixel #{p.R:X2}{p.G:X2}{p.B:X2}, average {stats.AverageHex}";
        }

        if (dump is not null)
        {
            Dump.Png(frame, dump, $"{Safe(window.Description)}-{Safe(source.Name)}-{Safe(source.LastFormat)}");
        }

        double? age = seen is { } s ? Ms.Between(window.RenderedAt(s), press) : null;
        return new Check(true, stats.IsBlank, age);
    }

    private static string Safe(string s) => string.Concat(s.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    private static double Pct(List<double> values, double q)
    {
        if (values.Count == 0)
        {
            return double.NaN;
        }

        var sorted = values.Order().ToList();
        return sorted[Math.Clamp((int)Math.Ceiling(q * sorted.Count) - 1, 0, sorted.Count - 1)];
    }

    private sealed record Row(string Window, string Backend, string FormatGot, CaptureFormat FormatAsked, int Runs, int Ok, int Blank, int Marker, List<double> Ages, List<double> Acquire, List<double> Total, List<double> Live, double ColdMs, bool ColdOk, SourceCounters Counters, int PrintMessages, string Messages, string? DuplicationInfo, string Busy, string? Problem)
    {
        private static string F(double v) => double.IsNaN(v) ? "-" : v.ToString("0.0", CultureInfo.InvariantCulture);

        private string Frames => Ages.Count == 0 ? "-" : $"{F(Pct(Ages, 0.5))} ({F(Ages.Min())}..{F(Ages.Max())})";

        public string Line() =>
            $"{Backend} [{FormatGot}]: {Ok}/{Runs} frames ({Blank} black), marker {Marker}/{Runs}, content age at press {Frames} ms, create→frame p50 {F(Pct(Acquire, 0.5))} p95 {F(Pct(Acquire, 0.95))} max {F(Acquire.Count > 0 ? Acquire.Max() : double.NaN)} ms, whole call p50 {F(Pct(Total, 0.5))} p95 {F(Pct(Total, 0.95))} ms, first call {F(ColdMs)} ms; {Counters}; WM_PRINT {PrintMessages}; messages the window handled at least once per capture: {(Messages.Length == 0 ? "none" : Messages)}{(DuplicationInfo is null ? "" : "; " + DuplicationInfo)}; busy window: {Busy}{(Problem is null ? "" : "; first problem: " + Problem)}";

        public string Markdown() =>
            $"| {Window} | {Backend} | {FormatGot} | {Ok}/{Runs}{(Blank > 0 ? $" ({Blank} black)" : "")} | {Marker}/{Runs} | {Frames} | {F(Pct(Acquire, 0.5))} / {F(Pct(Acquire, 0.95))} / {F(Acquire.Count > 0 ? Acquire.Max() : double.NaN)} | {F(Pct(Total, 0.5))} / {F(Pct(Total, 0.95))} | {F(Pct(Live, 0.5))} | {F(ColdMs)}{(ColdOk ? "" : " (failed)")} | {Counters.Acquisitions} / {Counters.Captures} | {(Messages.Length == 0 ? "none" : Messages)} | {Busy} |";
    }
}
