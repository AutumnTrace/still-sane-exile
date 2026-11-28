using System.Text.Json;

namespace StillSaneExile.Domain.Guide;

/// <summary>One sprite sheet: its file beside the atlas and how many tree units one of its pixels covers.</summary>
public sealed record AtlasSheet(string File, double Units);

/// <summary>Where one picture sits in a sheet, in pixels.</summary>
public sealed record AtlasFrame(string Sheet, int X, int Y, int Width, int Height);

/// <summary>The passive tree's pictures: <c>data/tree/art/atlas.json</c>, written by <c>tools/TreeArt</c> from
/// the sprite sheets of Grinding Gear Games' passive tree export. Frame names are the export's own
/// ("frame:PSSkillFrame", "normalActive:Art/2DArt/SkillIcons/…", "line:Orbit3Normal").</summary>
public sealed class TreeAtlas
{
    private TreeAtlas(IReadOnlyDictionary<string, AtlasSheet> sheets, IReadOnlyDictionary<string, AtlasFrame> frames)
    {
        Sheets = sheets;
        Frames = frames;
    }

    public IReadOnlyDictionary<string, AtlasSheet> Sheets { get; }

    public IReadOnlyDictionary<string, AtlasFrame> Frames { get; }

    /// <summary>A frame and its sheet, when both are known.</summary>
    public (AtlasFrame Frame, AtlasSheet Sheet)? Find(string name) =>
        Frames.TryGetValue(name, out var f) && Sheets.TryGetValue(f.Sheet, out var s) ? (f, s) : null;

    /// <exception cref="FormatException">The file is not an atlas SSE can draw from.</exception>
    public static TreeAtlas Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var sheets = new Dictionary<string, AtlasSheet>(StringComparer.Ordinal);
            foreach (var s in root.GetProperty("sheets").EnumerateObject())
            {
                var file = s.Value.GetProperty("file").GetString() ?? "";

                // Only a plain file name beside the atlas: a path could point anywhere on the disk.
                if (file.Length == 0 || file != Path.GetFileName(file) || file.StartsWith('.'))
                {
                    throw new FormatException($"The atlas names a sheet file SSE will not open: '{file}'.");
                }

                var units = s.Value.GetProperty("units").GetDouble();
                if (!(units > 0))
                {
                    throw new FormatException($"The atlas gives sheet '{s.Name}' no size.");
                }

                sheets[s.Name] = new AtlasSheet(file, units);
            }

            var frames = new Dictionary<string, AtlasFrame>(StringComparer.Ordinal);
            foreach (var f in root.GetProperty("frames").EnumerateObject())
            {
                var v = f.Value.EnumerateArray().ToList();
                var frame = new AtlasFrame(v[0].GetString() ?? "", v[1].GetInt32(), v[2].GetInt32(), v[3].GetInt32(), v[4].GetInt32());
                if (sheets.ContainsKey(frame.Sheet) && frame.Width > 0 && frame.Height > 0 && frame.X >= 0 && frame.Y >= 0)
                {
                    frames[f.Name] = frame;
                }
            }

            return frames.Count == 0 ? throw new FormatException("The atlas has no pictures.") : new TreeAtlas(sheets, frames);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            throw new FormatException("The passive tree's atlas could not be read.", e);
        }
    }
}
