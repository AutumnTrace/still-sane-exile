using System.Diagnostics;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using StillSaneExile.Desktop.Services;
using StillSaneExile.Domain.Items;
using StillSaneExile.Domain.Pricing;
using StillSaneExile.Domain.Wealth;

namespace StillSaneExile.Desktop.ViewModels;

/// <summary>"My wealth": what the active profile holds in the active league, from counts typed in or
/// imported from a CSV file. Every save adds observations to an append-only ledger; the list shows the
/// latest count per tab and item. An import is checked row by row and stored only on Apply, all rows in
/// one transaction. Everything is read through the session, so a league switch can't mix leagues.</summary>
public sealed partial class WealthViewModel : ObservableObject
{
    public const string FormatDoc = "https://github.com/AutumnTrace/still-sane-exile/blob/main/docs/WEALTH-CSV.md";

    private readonly ShellViewModel _shell;

    /// <summary>The file being previewed, kept so a league or currency switch re-checks the same bytes
    /// against the new session, and Apply stores exactly what was previewed.</summary>
    private (byte[] Bytes, string Name)? _pending;

    public WealthViewModel(ShellViewModel shell)
    {
        _shell = shell;
        Reload();
        shell.StashPictures.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StashPicturesViewModel.IsReviewing))
            {
                OnPropertyChanged(nameof(ShowHoldings));
                OnPropertyChanged(nameof(ShowPreview));
            }
        };
    }

    /// <summary>"Stash pictures you collected": while reviewing, the list takes the holdings' place.</summary>
    public StashPicturesViewModel Pictures => _shell.StashPictures;

    public bool ShowHoldings => !HasPreview && !Pictures.IsReviewing;

    public bool ShowPreview => HasPreview && !Pictures.IsReviewing;

    public bool IsDemo => _shell.Session.IsDemo;

    public string HoldingsLead => _shell.Session switch
    {
        { IsDemo: true } => "The latest count of each item, tab by tab, in the demo profile. Values use the demo's made-up prices.",
        { Book.Quotes.Count: 0 } s => $"The latest count of each item, tab by tab, in {s.League}. This league has no prices yet, so nothing is valued.",
        var s => $"The latest count of each item, tab by tab, in {s.League}. Values use this league's prices.",
    };

    /// <summary>What the Wealth milestone still owes, kept honest: these are not built yet.</summary>
    public IReadOnlyList<string> Later { get; } =
    [
        "\"Save visible stash\" previews recognized holdings and omissions; you confirm the tab before anything is stored.",
        "History keeps quantities and the price book used: value at the time, or revalue at today's rates.",
        "Session rate only from comparable holdings at fixed prices; transfers between tracked tabs net to zero.",
        "Held-key price chips never create history on their own.",
    ];

    [ObservableProperty]
    public partial IReadOnlyList<HoldingRowViewModel> Holdings { get; private set; } = [];

    [ObservableProperty]
    public partial string TotalText { get; private set; } = "";

    [ObservableProperty]
    public partial string PricedText { get; private set; } = "";

    [ObservableProperty]
    public partial string? Message { get; set; }

    [ObservableProperty]
    public partial HoldingRowViewModel? SelectedHolding { get; set; }

    [ObservableProperty]
    public partial string NewTab { get; set; } = "";

    [ObservableProperty]
    public partial string NewItemId { get; set; } = "";

    [ObservableProperty]
    public partial string NewVariant { get; set; } = "";

    [ObservableProperty]
    public partial string NewQuantity { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<string> FormErrors { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview), nameof(ShowHoldings), nameof(ShowPreview))]
    [NotifyCanExecuteChangedFor(nameof(ApplyImportCommand))]
    public partial ImportPreviewViewModel? Preview { get; private set; }

    public bool HasPreview => Preview is not null;

    /// <summary>Re-reads the ledger through the active session. Called at start and whenever the session
    /// is swapped (profile, league or display currency), so nothing from the old one stays on screen,
    /// including a status line about it.</summary>
    public void Reload()
    {
        var s = _shell.Session;
        Message = null;
        var price = new PriceText(s.Converter, s.DisplayCurrency, s.Now);
        var rows = WealthLedger.Current(_shell.Store.WealthObservations(s.ProfileId, s.League))
            .Select(h => new HoldingRowViewModel(h, HoldingValuation.Identity(s.Book, h.ItemId, h.Variant),
                HoldingValuation.Value(h, s.Book, s.Converter, s.DisplayCurrency, s.Now), price, s.Now))
            .ToList();
        var priced = rows.Where(r => r.Priced).ToList();
        Holdings = rows;
        TotalText = priced.Count > 0 ? price.One(priced.Sum(r => r.Value.Value)) : "Unpriced";
        PricedText = $"{priced.Count} of {rows.Count} priced";
        Preview = _pending is { } p ? BuildPreview(p.Bytes, p.Name) : null;
        OnPropertyChanged(nameof(IsDemo));
        OnPropertyChanged(nameof(HoldingsLead));
    }

    /// <summary>Reads and checks a file. Nothing is stored until <see cref="ApplyImportCommand"/>.</summary>
    public void PreviewFile(string path)
    {
        var name = Path.GetFileName(path);
        try
        {
            var size = new FileInfo(path).Length;
            if (size > WealthCsv.MaxBytes)
            {
                _pending = null;
                Preview = new ImportPreviewViewModel(name, WealthCsv.TooBig(size), [], _shell.Session);
                Message = null;
                return;
            }

            _pending = (File.ReadAllBytes(path), name);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Message = $"Couldn't open {name}: {e.Message}";
            return;
        }

        Preview = BuildPreview(_pending.Value.Bytes, name);
        SelectedHolding = null;
        Message = null;
        Pictures.IsReviewing = false; // the preview takes the left side
    }

    private ImportPreviewViewModel BuildPreview(byte[] bytes, string name)
    {
        var s = _shell.Session;
        var csv = WealthCsv.Read(bytes, name, s.League, s.Now);
        var checks = WealthLedger.Check(csv.Entries, _shell.Store.WealthObservations(s.ProfileId, s.League));
        return new ImportPreviewViewModel(name, csv, checks, s);
    }

    partial void OnSelectedHoldingChanged(HoldingRowViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        NewTab = value.Tab;
        NewItemId = value.ItemId;
        NewVariant = value.Entry.Variant ?? "";
        NewQuantity = value.Entry.Quantity.ToString(CultureInfo.InvariantCulture);
        FormErrors = [];
    }

    /// <summary>A typed count is a new observation at the session's "now": the demo clock in the demo.</summary>
    [RelayCommand]
    private void SaveManual()
    {
        var s = _shell.Session;
        var errors = new[]
        {
            HoldingFields.Tab(NewTab, out var tab),
            HoldingFields.ItemId(NewItemId, out var id),
            HoldingFields.Variant(NewVariant, out var variant),
            HoldingFields.Quantity(NewQuantity, out var quantity),
        }.OfType<string>().ToList();
        FormErrors = errors;
        if (errors.Count > 0)
        {
            return;
        }

        var key = new HoldingKey(tab, id, variant);
        var at = WealthLedger.ManualTime(s.Now, key, _shell.Store.WealthObservations(s.ProfileId, s.League));
        var write = _shell.Store.AddWealthObservations(s.ProfileId, s.League, [new HoldingEntry(s.League, tab, id, variant, quantity, at, HoldingOrigin.Manual)], s.Now);
        if (write.Conflict is not null)
        {
            FormErrors = [write.Conflict];
            return;
        }

        NewItemId = NewVariant = NewQuantity = "";
        SelectedHolding = null;
        Reload();
        var name = HoldingValuation.Name(s.Book, id, variant) ?? id;
        Message = (quantity == 0 ? $"Saved: no {name} left in {tab}." : $"Saved: {quantity.ToString("#,0", CultureInfo.InvariantCulture)} × {name} in {tab}.")
            + (HoldingValuation.IsPriced(s.Book, id, variant) ? ""
                : s.Book.Quotes.Count == 0 ? " This league has no prices yet, so it's counted but not valued."
                : " This league's prices don't include it, so it's counted but not valued.");
    }

    [RelayCommand]
    private void ImportCsv()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import holdings from a CSV file",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() == true)
        {
            PreviewFile(dialog.FileName);
        }
    }

    /// <summary>Demo only: the bundled practice files in fixtures/wealth.</summary>
    [RelayCommand]
    private void PreviewPractice(string file) => PreviewFile(Path.Combine(_shell.Paths.Fixtures, "wealth", file));

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void ApplyImport()
    {
        var s = _shell.Session;
        if (Preview is not { CanApply: true } preview || _pending is not { } pending)
        {
            return;
        }

        if (!ReferenceEquals(preview.Session, s))
        {
            Preview = BuildPreview(pending.Bytes, pending.Name);
            return;
        }

        var write = _shell.Store.AddWealthObservations(s.ProfileId, s.League, preview.ToAdd, s.Now);
        if (write.Conflict is not null)
        {
            Message = write.Conflict;
            Preview = BuildPreview(pending.Bytes, pending.Name);
            return;
        }

        _pending = null;
        Reload();
        Message = $"Imported {Count(write.Added, "row")} from {preview.FileName}."
            + (write.AlreadyStored > 0 ? $" {Count(write.AlreadyStored, "row")} {(write.AlreadyStored == 1 ? "was" : "were")} already stored." : "");
    }

    private bool CanApply() => Preview?.CanApply == true;

    [RelayCommand]
    private void CancelImport()
    {
        _pending = null;
        Preview = null;
        Message = "Import cancelled. Nothing was stored.";
    }

    [RelayCommand]
    private void OpenFormat() => Process.Start(new ProcessStartInfo(FormatDoc) { UseShellExecute = true });

    internal static string Count(int n, string noun) => $"{n.ToString("#,0", CultureInfo.InvariantCulture)} {noun}{(n == 1 ? "" : "s")}";

    internal static string Utc(DateTimeOffset t)
    {
        var u = t.UtcDateTime;
        return u.ToString(u.Second == 0 && u.Millisecond == 0 ? "yyyy-MM-dd HH':'mm" : "yyyy-MM-dd HH':'mm':'ss", CultureInfo.InvariantCulture) + " UTC";
    }
}

/// <summary>One line of "What you hold".</summary>
public sealed class HoldingRowViewModel(HoldingEntry entry, MarketIdentity? identity, HoldingValue value, PriceText price, DateTimeOffset now)
{
    public HoldingEntry Entry => entry;
    public HoldingValue Value => value;
    public string Tab => entry.Tab;
    public string ItemId => entry.ItemId;
    public string Name => identity?.Name ?? entry.ItemId;

    /// <summary>The id beside a known name, so the id to type or import is always in view.</summary>
    public string IdHint => identity is null ? "" : $"  {entry.ItemId}";

    /// <summary>Names take the game's colours, as everywhere in the app; an unknown id stays plain.</summary>
    public ItemRarity Rarity => identity?.Kind switch
    {
        IdentityKind.Unique => ItemRarity.Unique,
        IdentityKind.Currency or IdentityKind.Commodity => ItemRarity.Currency,
        _ => ItemRarity.Normal,
    };

    public string Variant => entry.Variant ?? "";
    public string Quantity => entry.Quantity.ToString("#,0", CultureInfo.InvariantCulture);
    public string AsOf => ValueFormat.Age(now - entry.SnapshotAt);

    public string AsOfDetail => $"{WealthViewModel.Utc(entry.SnapshotAt)} · " +
        (entry.Origin == HoldingOrigin.Manual ? "typed in" : $"imported from {entry.Source}");

    public bool Priced => value.Priced;
    public string ValueText => value.Priced ? price.One(value.Value) : "Unpriced";

    public string ValueDetail => !value.Priced
        ? value.Note
        : $"{price.One(value.Value / entry.Quantity)} each" + (value.Quote is { } q ? $" · price seen {ValueFormat.Age(now - q.ObservedTo)}" : "");

    /// <summary>The row as a screen reader says it: "Divine Orb, 15, tab Currency, as of 2 h ago, worth
    /// 15 div". WPF names a list row after its data object's ToString.</summary>
    public override string ToString() =>
        $"{Name}{(Variant.Length > 0 ? $" ({Variant})" : "")}, {Quantity}, tab {Tab}, as of {AsOf}, {(Priced ? $"worth {ValueText}" : "unpriced")}";
}

/// <summary>A checked CSV file, before anything is stored.</summary>
public sealed class ImportPreviewViewModel
{
    public ImportPreviewViewModel(string fileName, WealthCsvResult csv, IReadOnlyList<ImportCheck> checks, Session session)
    {
        FileName = fileName;
        Session = session;
        FileError = csv.FileError;

        // Checks come in the order of the file's usable rows.
        var next = 0;
        Rows = csv.Rows.Select(r => new ImportRowViewModel(r, r.Ok ? checks[next++] : null, session.Book)).ToList();
        Ready = Rows.Count(r => !r.IsError);
        Errors = Rows.Count - Ready;
        ToAdd = checks.Where(c => c.Status == ImportStatus.New).Select(c => c.Entry).ToList();
        var stored = checks.Count(c => c.Status == ImportStatus.AlreadyStored);
        var unpriced = Rows.Count(r => !r.IsError && !r.IsPriced);

        var you = session.IsDemo ? "the demo league" : $"\"{session.League}\"";
        var others = string.Join(", ", csv.OtherLeagues.Select(l => $"\"{l}\""));
        Headline = csv.OtherLeagues.Count == 0 ? null
            : csv.ReadyCount == 0 && Rows.All(r => r.Errors.Any(e => e.StartsWith("This row is for league", StringComparison.Ordinal)))
                ? $"This file is for league {others}; you are on {you}. Rows from another league are never carried over: switch league in Settings, or fix the file."
                : $"Some rows are for league {others}; you are on {you}. Rows from another league are never carried over.";

        Summary = $"{WealthViewModel.Count(Ready, "row")} ready, {Errors.ToString("#,0", CultureInfo.InvariantCulture)} with errors";
        Detail = string.Join(" · ", new[]
        {
            ToAdd.Count > 0 ? $"{ToAdd.Count} new" : null,
            stored > 0 ? $"{stored} already stored" : null,
            unpriced > 0 ? $"{unpriced} unpriced" : null,
        }.OfType<string>());

        ApplyNote = FileError is not null ? "Fix the file and import it again. Nothing has been stored."
            : Errors > 0 ? $"Apply is off while {Errors} {(Errors == 1 ? "row has an error" : "rows have errors")}: fix {(Errors == 1 ? "it" : "them")} in the file and import it again. Nothing has been stored."
            : ToAdd.Count == 0 ? "Everything in this file is already stored. Nothing to add."
            : $"Apply adds {WealthViewModel.Count(ToAdd.Count, "row")} in one go{(stored > 0 ? " and skips the ones already stored" : "")}. Nothing is stored until then.";
    }

    public string FileName { get; }
    public Session Session { get; }
    public string? FileError { get; }
    public IReadOnlyList<ImportRowViewModel> Rows { get; }
    public int Ready { get; }
    public int Errors { get; }
    public IReadOnlyList<HoldingEntry> ToAdd { get; }
    public string? Headline { get; }
    public string Summary { get; }
    public string Detail { get; }
    public string ApplyNote { get; }
    public bool CanApply => FileError is null && Errors == 0 && ToAdd.Count > 0;
}

/// <summary>One row of the import preview: what it reads as, or why it can't be used.</summary>
public sealed class ImportRowViewModel
{
    public ImportRowViewModel(WealthCsvRow row, ImportCheck? check, PriceBook book)
    {
        RowText = $"Row {row.Row}";
        Errors = check?.Error is { } conflict ? [conflict] : row.Errors;
        IsError = Errors.Count > 0;
        IsQuiet = !IsError && check?.Status == ImportStatus.AlreadyStored;
        Status = IsError ? "Error" : IsQuiet ? "Already stored" : "New";
        if (row.Entry is { } e)
        {
            Tab = e.Tab;
            Item = HoldingValuation.Name(book, e.ItemId, e.Variant) is { } name ? $"{name} ({e.ItemId})" : e.ItemId;
            Variant = e.Variant ?? "";
            Quantity = e.Quantity.ToString(CultureInfo.InvariantCulture);
            Time = WealthViewModel.Utc(e.SnapshotAt);
            IsPriced = HoldingValuation.IsPriced(book, e.ItemId, e.Variant);
            PricedText = IsPriced ? "Priced" : "Unpriced";
        }
        else
        {
            Raw = row.Raw;
        }

        Note = !IsError && check?.OlderThanStored == true ? "A later count is already stored, so this one is kept as history." : null;
    }

    public string RowText { get; }
    public string Status { get; }
    public bool IsError { get; }

    /// <summary>Already stored: applying skips it, so it is shown quietly.</summary>
    public bool IsQuiet { get; }

    public IReadOnlyList<string> Errors { get; }
    public string Tab { get; } = "";
    public string Item { get; } = "";
    public string Variant { get; } = "";
    public string Quantity { get; } = "";
    public string Time { get; } = "";
    public bool IsPriced { get; }
    public string PricedText { get; } = "";

    /// <summary>The row as written, for rows that couldn't be read into fields.</summary>
    public string? Raw { get; }

    public string? Note { get; }

    /// <summary>The row as a screen reader says it (WPF reads a list row through ToString).</summary>
    public override string ToString() => string.Join(", ", new string?[] { RowText, Status, Raw ?? $"{Item}, {Quantity}, tab {Tab}" }.Concat(Errors).Append(Note).Where(s => !string.IsNullOrEmpty(s)));
}
