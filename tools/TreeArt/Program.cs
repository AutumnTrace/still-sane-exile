using System.Globalization;
using System.Text;
using System.Text.Json;
using SkiaSharp;

// Usage: TreeArt <folder with GGG's assets/*.json and *.webp> <output folder> <source note>
// Writes one PNG per sheet SSE draws with, and atlas.json: every frame's sheet and rectangle in the PNG,
// and how many tree units one PNG pixel covers (GGG's sheets are drawn at scale 0.5: one pixel, two units).
if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: TreeArt <assets folder> <output folder> <source note>");
    return 2;
}

var input = args[0];
var output = args[1];
Directory.CreateDirectory(output);

// The sheets the guide draws, how far each is shrunk, and the format. The node icons are opaque squares
// (99.7% of their pixels), so JPEG keeps them whole at a sixth of the PNG size; everything with
// transparency stays PNG. The centre circle (4000 tree units) and the mastery patterns (about 490) are
// soft and never seen at full size on the page, so half their pixels are plenty.
(string Name, double Shrink, string Format)[] sheets =
[
    ("skills", 1, "jpg"),
    ("skills-disabled", 1, "jpg"),
    ("frame", 1, "png"),
    ("line", 1, "png"),
    ("background", 1, "png"),
    ("group-background", 0.5, "png"),
    ("mastery-effect-active", 0.5, "png"),
    ("mastery-effect-disabled", 0.5, "png"),
];

var frames = new SortedDictionary<string, (string Sheet, int X, int Y, int W, int H)>(StringComparer.Ordinal);
var units = new SortedDictionary<string, double>(StringComparer.Ordinal);
var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
long total = 0;
foreach (var (name, shrink, format) in sheets)
{
    using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(input, name + ".json")));
    var meta = doc.RootElement.GetProperty("meta");
    var scale = double.Parse(meta.GetProperty("scale").GetString()!, CultureInfo.InvariantCulture);

    using var source = SKBitmap.Decode(Path.Combine(input, name + ".webp"))
        ?? throw new InvalidDataException($"{name}.webp could not be decoded.");
    var width = (int)Math.Round(source.Width * shrink);
    var height = (int)Math.Round(source.Height * shrink);
    using var sized = shrink == 1 ? source.Copy() : source.Resize(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul), new SKSamplingOptions(SKCubicResampler.Mitchell));
    using var image = SKImage.FromBitmap(sized);
    using var encoded = format == "jpg" ? Opaque(sized).Encode(SKEncodedImageFormat.Jpeg, 95) : image.Encode(SKEncodedImageFormat.Png, 100);
    var file = Path.Combine(output, $"{name}.{format}");
    using (var stream = File.Create(file))
    {
        encoded.SaveTo(stream);
    }

    total += new FileInfo(file).Length;
    units[name] = 1 / (scale * shrink);
    files[name] = Path.GetFileName(file);
    foreach (var f in doc.RootElement.GetProperty("frames").EnumerateObject())
    {
        var r = f.Value.GetProperty("frame");
        int At(string p) => (int)Math.Round(r.GetProperty(p).GetDouble() * shrink);
        frames[f.Name] = (name, At("x"), At("y"), At("w"), At("h"));
    }

    Console.WriteLine($"{name}: {source.Width}x{source.Height} -> {width}x{height}, {new FileInfo(file).Length / 1024} KB");
}

var relaxed = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
var json = new StringBuilder();
json.Append("{\r\n");
json.Append("  \"about\": ").Append(JsonSerializer.Serialize("Where each picture of the passive tree sits in the sheets beside this file: sheet, x, y, width, height in pixels. `units` is how many tree units one pixel covers. Made by tools/TreeArt from Grinding Gear Games' passive tree export; regenerate with scripts/update-passive-tree.ps1.", relaxed)).Append(",\r\n");
json.Append("  \"source\": ").Append(JsonSerializer.Serialize(args[2], relaxed)).Append(",\r\n");
json.Append("  \"sheets\": {\r\n");
json.Append(string.Join(",\r\n", units.Select(u => $"    {JsonSerializer.Serialize(u.Key, relaxed)}: {{ \"file\": {JsonSerializer.Serialize(files[u.Key], relaxed)}, \"units\": {u.Value.ToString("0.###", CultureInfo.InvariantCulture)} }}")));
json.Append("\r\n  },\r\n");
json.Append("  \"frames\": {\r\n");
json.Append(string.Join(",\r\n", frames.Select(f => $"    {JsonSerializer.Serialize(f.Key, relaxed)}: [{JsonSerializer.Serialize(f.Value.Sheet, relaxed)}, {f.Value.X}, {f.Value.Y}, {f.Value.W}, {f.Value.H}]")));
json.Append("\r\n  }\r\n}\r\n");
File.WriteAllText(Path.Combine(output, "atlas.json"), json.ToString(), new UTF8Encoding(false));
total += new FileInfo(Path.Combine(output, "atlas.json")).Length;
Console.WriteLine($"{frames.Count} frames, {total / 1024} KB in all.");
return 0;

// JPEG has no transparency: the few see-through pixels are laid on black, as the tree's own ground is.
static SKImage Opaque(SKBitmap bitmap)
{
    using var surface = SKSurface.Create(new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
    surface.Canvas.Clear(SKColors.Black);
    using var image = SKImage.FromBitmap(bitmap);
    surface.Canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
    return surface.Snapshot();
}
