using StillSaneExile.CaptureSpike.Capture;

namespace StillSaneExile.CaptureSpike;

internal sealed class Args
{
    private readonly Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    public string Command { get; private set; } = "";

    public static Args Parse(string[] argv)
    {
        var a = new Args();
        for (var i = 0; i < argv.Length; i++)
        {
            var s = argv[i];
            if (s.StartsWith("--", StringComparison.Ordinal))
            {
                if (i + 1 < argv.Length && !argv[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    a._options[s[2..]] = argv[++i];
                }
                else
                {
                    a._flags.Add(s[2..]);
                }
            }
            else if (a.Command.Length == 0)
            {
                a.Command = s.ToLowerInvariant();
            }
        }

        return a;
    }

    public string? Get(string name) => _options.TryGetValue(name, out var v) ? v : null;

    public bool Has(string name) => _flags.Contains(name) || _options.ContainsKey(name);

    public int Int(string name, int fallback) => Get(name) is { } v && int.TryParse(v, out var n) ? n : fallback;

    public string[] List(string name, params string[] fallback) =>
        Get(name) is { } v ? v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : fallback;

    public CaptureFormat Format => Get("format")?.ToLowerInvariant() switch
    {
        null or "auto" => CaptureFormat.Auto,
        "bgra8" or "8" => CaptureFormat.Bgra8,
        "fp16" or "16" or "half" => CaptureFormat.RgbaHalf,
        var f => throw new ArgumentException($"Unknown --format {f}: use auto, bgra8 or fp16."),
    };

    public string? DumpFolder => Get("dump");

    public static ISpikeSource Source(string backend, CaptureFormat format, bool hideBorder = false) => backend.ToLowerInvariant() switch
    {
        "wgc" => new WgcFrameSource(new WgcOptions(format, HideBorder: hideBorder)),
        "duplication" or "dda" => new DuplicationFrameSource(format),
        "duplication2" => new DuplicationFrameSource(format, untilImage: true),
        "printwindow" or "pw" => new PrintWindowFrameSource(),
        _ => throw new ArgumentException($"Unknown backend {backend}: use wgc, duplication or printwindow."),
    };
}
