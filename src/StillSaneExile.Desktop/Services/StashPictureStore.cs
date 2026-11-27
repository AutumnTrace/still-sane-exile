using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Domain.Capture;

namespace StillSaneExile.Desktop.Services;

/// <summary>The kinds of screen a stash picture can show, as the player labels it. The values are what
/// the notes file stores; the labels are what the app shows.</summary>
public static class StashTabTypes
{
    public const string Currency = "currency";
    public const string Ordinary = "ordinary";
    public const string Quad = "quad";
    public const string Aldur = "aldur";
    public const string Other = "other";

    public static readonly IReadOnlyList<(string Value, string Label)> All =
    [
        (Currency, "Currency tab"),
        (Ordinary, "Ordinary"),
        (Quad, "Quad"),
        (Aldur, "Aldur panel"),
        (Other, "Other"),
    ];

    public static string? Label(string? value) => All.FirstOrDefault(t => t.Value == value).Label;
}

public sealed record PictureSize(int Width, int Height);

/// <param name="Mode">"windowed", "borderless fullscreen" or "borderless".</param>
public sealed record GameWindowNotes(int ClientWidth, int ClientHeight, string? Mode);

/// <param name="ScalingPercent">Windows display scaling of the monitor the game is on.</param>
/// <param name="SdrWhiteNits">Windows' SDR content brightness, with HDR on.</param>
public sealed record MonitorNotes(string? Name, int Width, int Height, int ScalingPercent, bool Hdr, double? SdrWhiteNits);

/// <param name="PixelFormat">What Windows handed over before it was saved as 8-bit sRGB.</param>
/// <param name="PressToFrameMs">From the key press to the picture in memory.</param>
/// <param name="CreateToFrameMs">From asking Windows for the capture to holding the frame.</param>
/// <param name="PeakScRgb">With HDR on: the brightest channel in scRGB (1.0 = 80 nits), before clamping.</param>
public sealed record CaptureNotes(string Backend, string PixelFormat, string SavedAs, double PressToFrameMs, double? CreateToFrameMs, double? PeakScRgb);

/// <summary>What the player fills in later, on My wealth.</summary>
/// <param name="TabType">One of <see cref="StashTabTypes"/>, or null until labelled.</param>
/// <param name="UiScale">The game's own UI scale setting, as the player typed it.</param>
/// <param name="HeldOut">Kept apart from the pictures used to tune the readers (#3's held-out set).</param>
public sealed record StashPictureLabels(string? TabType = null, string? CurrencySubtab = null, double? UiScale = null, string? Note = null, bool HeldOut = false);

/// <summary>The JSON file beside each picture: automatic notes taken with the picture, and the labels.</summary>
public sealed record StashPictureNotes(
    string Kind, int Format, string File, DateTimeOffset CapturedAt, string SseVersion,
    PictureSize Picture, GameWindowNotes? GameWindow, MonitorNotes? Monitor, CaptureNotes? Capture, StashPictureLabels Labels)
{
    public const string KindValue = "sse-stash-picture";

    public static StashPictureNotes For(PictureCopy copy, CaptureFacts? facts, DateTimeOffset at, string version, double pressToFrameMs)
    {
        var w = facts?.Window;
        var hdr = copy.SourceFormat == FramePixelFormat.RgbaHalf;
        return new StashPictureNotes(KindValue, 1, "", at, version,
            new PictureSize(copy.Width, copy.Height),
            w is null ? null : new GameWindowNotes(w.ClientWidth, w.ClientHeight, w.WindowMode),
            w is null ? null : new MonitorNotes(w.MonitorName, w.MonitorWidth, w.MonitorHeight, w.ScalingPercent, w.Hdr, w.Hdr ? Math.Round(w.SdrWhite * 80, 1) : null),
            new CaptureNotes(facts?.Backend ?? "unknown", facts?.PixelFormat ?? (hdr ? "R16G16B16A16Float" : "B8G8R8A8UIntNormalized"),
                hdr ? "8-bit sRGB PNG: divided by SDR white, clamped" : "8-bit sRGB PNG",
                Math.Round(pressToFrameMs, 1), facts is null ? null : Math.Round(facts.CreateToFrameMs, 1), hdr ? Math.Round(copy.PeakScRgb, 3) : null),
            new StashPictureLabels());
    }
}

/// <summary>One saved picture: its PNG, its notes file, and what the notes say.</summary>
public sealed record StashPicture(string PngPath, string NotesPath, StashPictureNotes Notes);

/// <summary>Stash pictures on disk, under the data folder's <c>captures\&lt;date&gt;\&lt;time&gt;.png</c>, each
/// with <c>&lt;time&gt;.json</c> beside it. Nothing here goes anywhere but that folder: no upload, no copy.</summary>
public sealed class StashPictureStore(string root)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string Root { get; } = Path.GetFullPath(root);

    /// <summary>Every picture, newest first. A picture whose notes are missing or unreadable is still listed.</summary>
    public IReadOnlyList<StashPicture> Load()
    {
        if (!Directory.Exists(Root))
        {
            return [];
        }

        var list = new List<StashPicture>();
        foreach (var day in Directory.EnumerateDirectories(Root))
        {
            if (!DateOnly.TryParseExact(Path.GetFileName(day), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                continue;
            }

            foreach (var png in Directory.EnumerateFiles(day, "*.png"))
            {
                var notesPath = Path.ChangeExtension(png, ".json");
                list.Add(new StashPicture(png, notesPath, ReadNotes(png, notesPath)));
            }
        }

        return list.OrderByDescending(p => p.Notes.CapturedAt).ThenByDescending(p => p.PngPath, StringComparer.Ordinal).ToList();
    }

    /// <summary>Writes the picture as a PNG and its notes beside it, named after <see cref="StashPictureNotes.CapturedAt"/>
    /// in local time. Never overwrites: a second picture in the same second gets "-2". Blocks while the
    /// PNG is encoded: call it off the UI thread.</summary>
    public StashPicture Save(PictureCopy copy, StashPictureNotes notes)
    {
        var local = notes.CapturedAt.ToLocalTime();
        var day = Path.Combine(Root, local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(day);
        var stem = local.ToString("HH-mm-ss", CultureInfo.InvariantCulture);
        string png, json;
        var n = 1;
        do
        {
            var name = n == 1 ? stem : $"{stem}-{n}";
            png = Path.Combine(day, name + ".png");
            json = Path.Combine(day, name + ".json");
            n++;
        }
        while (File.Exists(png) || File.Exists(json));

        var bitmap = BitmapSource.Create(copy.Width, copy.Height, 96, 96, PixelFormats.Bgr32, null, copy.Pixels, copy.Stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = new FileStream(png, FileMode.CreateNew, FileAccess.Write))
        {
            try
            {
                encoder.Save(file);
            }
            catch
            {
                file.Close();
                TryDelete(png); // half a PNG is no picture
                throw;
            }
        }

        var saved = notes with { File = Path.GetFileName(png) };
        try
        {
            WriteNotes(json, saved);
        }
        catch
        {
            TryDelete(png); // a picture without its notes would be listed with none
            throw;
        }

        return new StashPicture(png, json, saved);
    }

    /// <summary>Rewrites the notes file with new labels; the automatic notes stay as they were.</summary>
    public StashPicture SaveLabels(StashPicture picture, StashPictureLabels labels)
    {
        EnsureInside(picture.NotesPath);
        var notes = picture.Notes with { Labels = labels };
        WriteNotes(picture.NotesPath, notes);
        return picture with { Notes = notes };
    }

    /// <summary>Deletes the PNG and its notes, then the day's folder if that left it empty.</summary>
    public void Delete(StashPicture picture)
    {
        EnsureInside(picture.PngPath);
        EnsureInside(picture.NotesPath);
        File.Delete(picture.PngPath);
        File.Delete(picture.NotesPath);
        var day = Path.GetDirectoryName(picture.PngPath);
        if (day is not null && Directory.Exists(day) && !Directory.EnumerateFileSystemEntries(day).Any())
        {
            Directory.Delete(day);
        }
    }

    public static string ToJson(StashPictureNotes notes) => JsonSerializer.Serialize(notes, Json);

    private static StashPictureNotes ReadNotes(string png, string notesPath)
    {
        try
        {
            if (File.Exists(notesPath) && JsonSerializer.Deserialize<StashPictureNotes>(File.ReadAllText(notesPath), Json) is { Kind: StashPictureNotes.KindValue } notes)
            {
                return notes with { Labels = notes.Labels ?? new StashPictureLabels() };
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
        }

        return new StashPictureNotes(StashPictureNotes.KindValue, 1, Path.GetFileName(png), new DateTimeOffset(File.GetCreationTime(png)), "", new PictureSize(0, 0), null, null, null, new StashPictureLabels());
    }

    /// <summary>Writes to a temporary file first, so a crash mid-write never leaves half a notes file.</summary>
    private static void WriteNotes(string path, StashPictureNotes notes)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, ToJson(notes));
        File.Move(temp, path, overwrite: true);
    }

    private void EnsureInside(string path)
    {
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{full} is not in the pictures folder.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
