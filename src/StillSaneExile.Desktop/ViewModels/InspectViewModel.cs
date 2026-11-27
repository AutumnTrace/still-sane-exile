using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Domain.Items;
using StillSaneExile.Domain.Pricing;
using StillSaneExile.Domain.Trade;

namespace StillSaneExile.Desktop.ViewModels;

public sealed record PracticeItem(string Label, string File);

public sealed partial class InspectViewModel : ObservableObject
{
    private const int MaxRecent = 20;
    private readonly ShellViewModel _shell;
    private readonly ClipboardReader _clipboard;
    private int _captureCounter;

    public InspectViewModel(ShellViewModel shell, ClipboardReader clipboard)
    {
        _shell = shell;
        _clipboard = clipboard;
    }

    public ObservableCollection<ItemViewModel> Recent { get; } = [];

    public IReadOnlyList<PracticeItem> PracticeItems { get; } =
    [
        new("Crossbow · advanced copy", "rare-crossbow-advanced.txt"),
        new("Crossbow · hybrid, normal copy", "rare-crossbow-normal-hybrid.txt"),
        new("Boots · normal copy", "rare-boots-normal.txt"),
        new("Magic crossbow · base", "magic-crossbow-normal.txt"),
        new("Unique ring · baseline", "unique-ring.txt"),
        new("Exalted Orb stack", "currency-exalted.txt"),
    ];

    public IReadOnlyList<string> CurrencyChoices => Currencies.Display;

    public bool IsDemo => _shell.Session.IsDemo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial ItemViewModel? Selected { get; set; }

    public bool HasSelection => Selected is not null;

    [ObservableProperty]
    public partial string? Message { get; set; }

    [ObservableProperty]
    public partial string NewAmount { get; set; } = "";

    [ObservableProperty]
    public partial string NewCurrency { get; set; } = "divine";

    [ObservableProperty]
    public partial string NewSeller { get; set; } = "";

    [ObservableProperty]
    public partial string NewLink { get; set; } = "";

    [ObservableProperty]
    public partial string NewNotes { get; set; } = "";

    /// <summary>Parses item text into a snapshot and adds it to the in-memory recent list. Text that is
    /// not an item is discarded immediately and never stored.</summary>
    public ItemViewModel? AddFromText(string? text, string origin)
    {
        var id = $"{origin}-{Interlocked.Increment(ref _captureCounter)}";
        var parsed = ItemTextParser.Parse(text, _shell.Session.Now, id);
        if (!parsed.Ok)
        {
            Message = parsed.Error;
            return null;
        }

        var vm = new ItemViewModel(parsed.Item!, _shell.Session);
        Recent.Insert(0, vm);
        while (Recent.Count > MaxRecent)
        {
            Recent.RemoveAt(Recent.Count - 1);
        }

        Selected = vm;
        Message = null;
        return vm;
    }

    /// <summary>Same snapshots, new session (profile, league or currency changed).</summary>
    public void Rebuild()
    {
        var selectedId = Selected?.Item.CaptureId;
        var items = Recent.Select(r => r.Item).ToList();
        Recent.Clear();
        foreach (var item in items)
        {
            Recent.Add(new ItemViewModel(item, _shell.Session));
        }

        Selected = Recent.FirstOrDefault(r => r.Item.CaptureId == selectedId) ?? Recent.FirstOrDefault();
        OnPropertyChanged(nameof(IsDemo));
    }

    [RelayCommand]
    private void PasteFromClipboard()
    {
        // Explicit button press inside the app: one read.
        var read = _clipboard.ReadNow();
        if (read.Text is null)
        {
            Message = read.Error;
            return;
        }

        AddFromText(read.Text, "paste");
    }

    [RelayCommand]
    private void LoadPractice(PracticeItem? item)
    {
        if (item is null)
        {
            return;
        }

        var path = Path.Combine(_shell.Paths.Fixtures, "items", item.File);
        AddFromText(File.ReadAllText(path), "practice");
    }

    [RelayCommand]
    private void OpenTrade()
    {
        Process.Start(new ProcessStartInfo(TradeHandoff.OfficialTradeUrl) { UseShellExecute = true });
    }

    [RelayCommand]
    private void CopyInstructions()
    {
        if (Selected is null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(Selected.Instructions);
            Message = "Worksheet copied. Set the same filters on the trade site.";
        }
        catch (ExternalException)
        {
            Message = "The clipboard is busy. Try again.";
        }
    }

    [RelayCommand]
    private void OpenCraft() => _shell.OpenCraftWith(Selected);

    [RelayCommand]
    private void SetNewCurrency(string currency) => NewCurrency = currency;

    [RelayCommand]
    private void CycleMatch(WorksheetRowViewModel? row) => row?.CycleMatch();

    [RelayCommand]
    private void ToggleGroup(WorksheetRowViewModel? row) => row?.ToggleGroup();

    [RelayCommand]
    private void ApplyRelaxation(RelaxationSuggestion? s)
    {
        if (s is not null)
        {
            Selected?.ApplyRelaxation(s);
        }
    }

    [RelayCommand]
    private void AddComparable()
    {
        if (Selected is null)
        {
            return;
        }

        if (!decimal.TryParse(NewAmount, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            Message = "Enter the listing's asking price as a positive number, e.g. 2.5.";
            return;
        }

        var session = _shell.Session;
        var now = session.Now;
        var link = string.IsNullOrWhiteSpace(NewLink) ? null : NewLink.Trim();
        if (link is not null && !(Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps))
        {
            Message = "The listing link must be an https:// address (or leave it empty).";
            return;
        }

        var comparable = new Comparable(
            Guid.NewGuid().ToString("N"), session.League, amount, NewCurrency, now,
            string.IsNullOrWhiteSpace(NewSeller) ? null : NewSeller.Trim(),
            ListingId: link, link,
            string.IsNullOrWhiteSpace(NewNotes) ? null : NewNotes.Trim(),
            ComparableOrigin.PlayerEntered,
            now.AddDays(_shell.Settings.ComparableExpiryDays));
        _shell.Store.AddComparable(session.ProfileId, Selected.SetKey, Selected.Item.RawText, comparable, now);
        NewAmount = NewSeller = NewLink = NewNotes = "";
        Selected.RefreshPrice();
        Message = null;
    }

    [RelayCommand]
    private void RemoveComparable(ComparableViewModel? c)
    {
        if (c is null || c.Seeded)
        {
            return;
        }

        _shell.Store.RemoveComparable(c.Id);
        Selected?.RefreshPrice();
    }
}
