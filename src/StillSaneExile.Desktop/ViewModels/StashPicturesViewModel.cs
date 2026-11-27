using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.Services;

namespace StillSaneExile.Desktop.ViewModels;

/// <summary>"Collect stash pictures" (Settings) and "Stash pictures you collected" (My wealth): the switch,
/// the pictures on disk, and the labels the player adds to them for #3. Reading the stash comes later; the
/// pictures are what it will be built and checked against.</summary>
public sealed partial class StashPicturesViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;
    private IStashFrameSource? _source;

    public StashPicturesViewModel(ShellViewModel shell)
    {
        _shell = shell;
        Store = new StashPictureStore(shell.Paths.Captures);
        Key = shell.Settings.StashKey;
        IsCollecting = shell.Settings.CollectStashPictures;
        Pictures.CollectionChanged += (_, _) => OnCountChanged();
        Reload();
    }

    public StashPictureStore Store { get; }

    public ObservableCollection<StashPictureViewModel> Pictures { get; } = [];

    /// <summary>The stash key, cached: the key hook reads it on every key event.</summary>
    public Hotkey Key { get; private set; }

    public string KeyLabel => Key.Label;

    /// <summary>On: each physical press of the stash key with the game in front saves one picture.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCard), nameof(Summary), nameof(Problem), nameof(ShowSummaryCard))]
    public partial bool IsCollecting { get; set; }

    /// <summary>My wealth shows the pictures instead of the holdings.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSummaryCard))]
    public partial bool IsReviewing { get; set; }

    [ObservableProperty]
    public partial string? Message { get; set; }

    public string Folder => Store.Root;

    public int Count => Pictures.Count;

    public bool HasPictures => Count > 0;

    public int Labelled => Pictures.Count(p => p.TabType is not null);

    /// <summary>My wealth's card about the pictures: while collecting, or while any are left.</summary>
    public bool ShowCard => IsCollecting || HasPictures;

    /// <summary>The card in My wealth's right column; the review list takes its place while open.</summary>
    public bool ShowSummaryCard => ShowCard && !IsReviewing;

    public string Summary => Count == 0
        ? IsCollecting ? $"None yet. Open your stash in the game and press {KeyLabel}." : "No pictures yet."
        : $"{Count} {(Count == 1 ? "picture" : "pictures")}, {(Labelled == Count ? "all" : Labelled.ToString(CultureInfo.InvariantCulture))} with a tab type.";

    /// <summary>Why pictures can't be taken here, while collecting is on; null when they can.</summary>
    public string? Problem => IsCollecting ? _source?.Unsupported : null;

    public string SwitchLabel => $"Collect stash pictures when I press {KeyLabel} in the game";

    public string Lead =>
        $"Path of Exile 2 has no stash API, so SSE will read your stash from a picture of the game window, one per press of the stash key. To build that, it needs real pictures of stash tabs, and you can help: switch this on, open your stash in the game, and press {KeyLabel} once for each tab. Nothing is read from the pictures yet.";

    public string WhatIsTaken =>
        "Each press takes one picture of the game window: the game only, without other windows or the mouse pointer, and only while the game is in front. Holding the key, or a key sent by another program, takes nothing more. Windows draws a yellow border around the game for a moment while it does. " +
        (Key.Vk == 0xA5 ? "If Right Alt does something in your game, change the stash key under Keys above, to F8 for example." : $"The stash key is {KeyLabel}; change it under Keys above.");

    public string WhereSaved =>
        $"Saved as PNG files in {Folder}, one folder per day, each with a small .json file beside it: the time, SSE's version, the game window's size, your monitor's resolution, Windows display scaling, HDR on or off, and how long the picture took. Label and delete them on My wealth.";

    public const string Privacy =
        "The pictures stay on this computer: SSE never uploads them, and deleting one deletes its files. Chat and character names may be visible in them: crop or blur those before you share a picture.";

    public string PrivacyText => Privacy;

    public string ReviewLead =>
        "When you're done in the game, label each picture: what it shows, and the game's own UI scale. Labels are saved beside the picture as you type. Tick Held out on about a third of them, taken in another play session: they stay apart from the ones used to tune the reader, to check it fairly.";

    /// <summary>The source that takes the pictures, so Settings can say when this computer can't.</summary>
    public void UseSource(IStashFrameSource source)
    {
        _source = source;
        OnPropertyChanged(nameof(Problem));
    }

    partial void OnIsCollectingChanged(bool value)
    {
        if (_shell.Settings.CollectStashPictures != value)
        {
            _shell.Settings.CollectStashPictures = value;
            _shell.Hud?.ApplySettings(); // a stash key on a side button needs the mouse observer
        }
    }

    /// <summary>The stash key changed in Settings.</summary>
    public void ApplySettings()
    {
        Key = _shell.Settings.StashKey;
        OnPropertyChanged(nameof(Key));
        OnPropertyChanged(nameof(KeyLabel));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(SwitchLabel));
        OnPropertyChanged(nameof(Lead));
        OnPropertyChanged(nameof(WhatIsTaken));
    }

    /// <summary>Re-reads the pictures folder.</summary>
    public void Reload()
    {
        Pictures.Clear();
        foreach (var p in Store.Load())
        {
            Pictures.Add(new StashPictureViewModel(this, p));
        }
    }

    /// <summary>A picture the collector just saved (on the UI thread).</summary>
    public void Add(StashPicture picture)
    {
        Pictures.Insert(0, new StashPictureViewModel(this, picture));
        Message = null;
    }

    internal StashPicture SaveLabels(StashPicture picture, StashPictureLabels labels)
    {
        try
        {
            var saved = Store.SaveLabels(picture, labels);
            OnPropertyChanged(nameof(Labelled));
            OnPropertyChanged(nameof(Summary));
            return saved;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Message = $"Couldn't save the labels of {Path.GetFileName(picture.PngPath)}: {e.Message}";
            return picture;
        }
    }

    [RelayCommand]
    private void Review()
    {
        Reload();
        Message = null;
        IsReviewing = true;
        _shell.Navigate(_shell.Wealth);
    }

    [RelayCommand]
    private void DoneReviewing()
    {
        IsReviewing = false;
        Message = null;
    }

    /// <summary>Opens the pictures folder in Explorer. Only ever on a click.</summary>
    [RelayCommand]
    private void OpenFolder()
    {
        Directory.CreateDirectory(Folder);
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { Folder }, UseShellExecute = false });
    }

    [RelayCommand]
    private void Delete(StashPictureViewModel? picture)
    {
        if (picture is null)
        {
            return;
        }

        try
        {
            Store.Delete(picture.Picture);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Message = $"Couldn't delete the picture from {picture.Name}: {e.Message}";
            return;
        }

        Pictures.Remove(picture);
        Message = $"Deleted the picture from {picture.Name} and its notes.";
    }

    private void OnCountChanged()
    {
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(HasPictures));
        OnPropertyChanged(nameof(Labelled));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(ShowCard));
        OnPropertyChanged(nameof(ShowSummaryCard));
    }
}

/// <summary>One picture in the review list, with the labels the player fills in. Each change is saved to
/// the picture's notes file straight away.</summary>
public sealed partial class StashPictureViewModel : ObservableObject
{
    private readonly StashPicturesViewModel _owner;
    private readonly bool _ready;
    private ImageSource? _thumbnail;
    private bool _thumbnailAsked;

    public StashPictureViewModel(StashPicturesViewModel owner, StashPicture picture)
    {
        _owner = owner;
        Picture = picture;
        TabChoices = StashTabTypes.All.Select(t => new TabChoiceViewModel(this, t.Value, t.Label)).ToList();
        var labels = picture.Notes.Labels;
        TabType = labels.TabType;
        Subtab = labels.CurrencySubtab ?? "";
        UiScale = labels.UiScale?.ToString(CultureInfo.InvariantCulture) ?? "";
        Note = labels.Note ?? "";
        HeldOut = labels.HeldOut;
        _ready = true;
    }

    public StashPicture Picture { get; private set; }

    private DateTimeOffset Local => Picture.Notes.CapturedAt.ToLocalTime();

    /// <summary>" (2)" for the second picture taken within one second (its file is "14-30-05-2.png").</summary>
    private string Suffix
    {
        get
        {
            var stem = Path.GetFileNameWithoutExtension(Picture.PngPath);
            return stem.Count(c => c == '-') == 3 ? $" ({stem[(stem.LastIndexOf('-') + 1)..]})" : "";
        }
    }

    /// <summary>Short and unique among the pictures: "27 Nov 14:30:05".</summary>
    public string Name => Local.ToString("d MMM HH:mm:ss", CultureInfo.InvariantCulture) + Suffix;

    public string Title => Local.ToString("d MMM yyyy, HH:mm:ss", CultureInfo.InvariantCulture) + Suffix;

    /// <summary>The automatic notes in one line.</summary>
    public string Facts
    {
        get
        {
            var n = Picture.Notes;
            var parts = new List<string>();
            if (n.GameWindow is { } w)
            {
                parts.Add($"Game window {w.ClientWidth}×{w.ClientHeight}{(w.Mode is { } m ? $", {m}" : "")}");
            }
            else if (n.Picture.Width > 0)
            {
                parts.Add($"Picture {n.Picture.Width}×{n.Picture.Height}");
            }

            if (n.Monitor is { } mon)
            {
                parts.Add($"monitor {(mon.Name is { } name ? name + " " : "")}{mon.Width}×{mon.Height} at {mon.ScalingPercent}%");
                parts.Add(mon.Hdr ? $"HDR on{(mon.SdrWhiteNits is { } nits ? $", SDR white {nits:0} nits" : "")}" : "HDR off");
            }

            if (n.Capture is { } c)
            {
                parts.Add(c.PixelFormat.StartsWith("R16", StringComparison.Ordinal) ? "16-bit" : "8-bit");
                parts.Add($"{c.PressToFrameMs.ToString("0", CultureInfo.InvariantCulture)} ms after the press");
            }

            return parts.Count == 0 ? "No notes were found for this picture." : string.Join(" · ", parts);
        }
    }

    public IReadOnlyList<TabChoiceViewModel> TabChoices { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCurrency))]
    public partial string? TabType { get; set; }

    public bool IsCurrency => TabType == StashTabTypes.Currency;

    [ObservableProperty]
    public partial string Subtab { get; set; } = "";

    /// <summary>The game's UI scale, as the player reads it in the game's options: any number.</summary>
    [ObservableProperty]
    public partial string UiScale { get; set; } = "";

    [ObservableProperty]
    public partial string? UiScaleProblem { get; private set; }

    [ObservableProperty]
    public partial string Note { get; set; } = "";

    [ObservableProperty]
    public partial bool HeldOut { get; set; }

    public string SubtabName => $"Currency subtab of the picture from {Name}";

    public string UiScaleName => $"In-game UI scale of the picture from {Name}";

    public string NoteName => $"Note on the picture from {Name}";

    public string HeldOutName => $"Held out: the picture from {Name}";

    public string DeleteName => $"Delete the picture from {Name}";

    /// <summary>A small copy of the picture, decoded off the UI thread the first time it is asked for.</summary>
    public ImageSource? Thumbnail
    {
        get
        {
            if (!_thumbnailAsked)
            {
                _thumbnailAsked = true;
                _ = LoadThumbnailAsync();
            }

            return _thumbnail;
        }
    }

    /// <summary>Decodes the thumbnail on this thread, for rendering snapshots.</summary>
    public void LoadThumbnailNow()
    {
        _thumbnailAsked = true;
        _thumbnail = Decode(Picture.PngPath);
        OnPropertyChanged(nameof(Thumbnail));
    }

    private async Task LoadThumbnailAsync()
    {
        var path = Picture.PngPath;
        _thumbnail = await Task.Run(() => Decode(path));
        OnPropertyChanged(nameof(Thumbnail));
    }

    /// <summary>Loaded whole and closed (OnLoad), so deleting the file afterwards works.</summary>
    private static ImageSource? Decode(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.DecodePixelWidth = 320;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    partial void OnTabTypeChanged(string? value)
    {
        foreach (var choice in TabChoices)
        {
            choice.Refresh();
        }

        Save();
    }

    partial void OnSubtabChanged(string value) => Save();

    partial void OnUiScaleChanged(string value) => Save();

    partial void OnNoteChanged(string value) => Save();

    partial void OnHeldOutChanged(bool value) => Save();

    /// <summary>"100", "100 %", "0.8" and "0,8" all read; anything else is not saved.</summary>
    public static bool TryParseScale(string text, out double scale)
    {
        var t = text.Trim().TrimEnd('%').Trim().Replace(',', '.');
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out scale) && scale > 0 && scale <= 1000;
    }

    private void Save()
    {
        if (!_ready)
        {
            return;
        }

        double? scale = Picture.Notes.Labels.UiScale;
        if (UiScale.Trim().Length == 0)
        {
            scale = null;
            UiScaleProblem = null;
        }
        else if (TryParseScale(UiScale, out var s))
        {
            scale = s;
            UiScaleProblem = null;
        }
        else
        {
            UiScaleProblem = "Type a number, as the game shows it: 100, 0.8...";
        }

        var subtab = Subtab.Trim();
        var note = Note.Trim();
        var labels = new StashPictureLabels(TabType, IsCurrency && subtab.Length > 0 ? subtab : null, scale, note.Length > 0 ? note : null, HeldOut);
        if (labels != Picture.Notes.Labels)
        {
            Picture = _owner.SaveLabels(Picture, labels);
        }
    }

    /// <summary>The row as a screen reader says it (WPF reads an item through ToString).</summary>
    public override string ToString() =>
        $"Stash picture from {Name}" + (StashTabTypes.Label(TabType) is { } t ? $", {t}" : ", no tab type yet") + (HeldOut ? ", held out" : "");
}

/// <summary>One of the tab type pills under a picture.</summary>
public sealed partial class TabChoiceViewModel(StashPictureViewModel picture, string value, string label) : ObservableObject
{
    public string Value => value;

    public string Label => label;

    /// <summary>Unique on the page: the pills repeat under every picture.</summary>
    public string SpokenName => $"{label}: the picture from {picture.Name}";

    public bool IsChosen => picture.TabType == value;

    [RelayCommand]
    private void Choose() => picture.TabType = value;

    internal void Refresh() => OnPropertyChanged(nameof(IsChosen));

    public override string ToString() => SpokenName;
}
