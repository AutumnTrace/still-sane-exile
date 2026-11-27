using StillSaneExile.CaptureSpike;

// Developer tool for issue #4. See docs/CAPTURE-SPIKE.md.
//   CaptureSpike info                      OS, GPU, monitors and their HDR state, keyboard repeat
//   CaptureSpike bench [--runs 50]         every backend against test windows this process owns
//   CaptureSpike hold [--seconds 10]       a held key with auto-repeat through the real gate and WGC
//   CaptureSpike live [--key RightAlt]     one frame of the game per physical press (for the owner)
// Common options: --backend wgc|duplication|printwindow, --format auto|bgra8|fp16, --dump <folder>.
// Nothing is written to disk unless --dump is given.
var args2 = Args.Parse(args);
try
{
    return args2.Command switch
    {
        "info" => Info.Run(),
        "bench" => Bench.Run(args2),
        "hold" => await Hold.RunAsync(args2),
        "live" => Live.Run(args2),
        _ => Usage(),
    };
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return Usage();
}

static int Usage()
{
    Console.WriteLine("""
        CaptureSpike: one frame of one window per key press (issue #4).

          info                       OS, GPU, monitors with their HDR state, keyboard repeat rate
          bench [--runs 50] [--windows d3d,blt,wpf,borderless] [--backends wgc,duplication,printwindow]
                                     latency and single-acquisition checks against this tool's own test windows
          hold [--seconds 10]        hold a key for 10 s with auto-repeat: real gate, real WGC, own test window
          live [--key RightAlt] [--backend wgc] [--format auto] [--dump <folder>] [--own-windows]
                                     one frame of Path of Exile 2 per physical press of the key, until Ctrl+C

        Options: --format auto|bgra8|fp16 (auto = 16-bit float on an HDR monitor), --dump <folder> saves PNGs.
        Nothing is written to disk without --dump.
        """);
    return 2;
}
