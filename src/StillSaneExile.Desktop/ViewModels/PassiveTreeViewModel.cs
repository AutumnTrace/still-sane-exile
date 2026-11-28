using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StillSaneExile.Domain.Guide;

namespace StillSaneExile.Desktop.ViewModels;

/// <summary>One of the build's passive trees, for the picker above the drawing.</summary>
public sealed record TreeSpecChoice(int Index, string Title, bool IsShown)
{
    public string? Tag => IsShown ? "active" : null;

    public override string ToString() => Title;
}

/// <summary>A keystone or notable the build takes, one row of the list under the drawing.</summary>
public sealed record TreePickViewModel(string Name, string Kind, string Stats, string Note)
{
    public bool HasNote => Note.Length > 0;

    /// <summary>The row as a screen reader says it (WPF reads a list row through ToString).</summary>
    public override string ToString() => string.Join(", ", new[] { Name, Kind, OneLine(Stats, ", "), Note }.Where(s => s.Length > 0));

    /// <summary>Stat lines run together: "Storm and Plant Spells: • deal 50% more damage, • cost 50% less".</summary>
    internal static string OneLine(string stats, string separator) =>
        stats.Replace(":\n", ": ", StringComparison.Ordinal).Replace("\n", separator, StringComparison.Ordinal);
}

/// <summary>The build's passive tree drawn on Path of Exile 2's tree from GGG's export, which ships with
/// the app: the points it takes, its keystones and notables by name, and its ascendancy. The build's
/// trees come from the pasted code; nothing is fetched.</summary>
public sealed partial class PassiveTreeViewModel : ObservableObject
{
    private readonly Func<PassiveTree> _tree;
    private PobBuild? _build;
    private int? _picked;
    private int? _shown;

    public PassiveTreeViewModel(Func<PassiveTree> tree)
    {
        _tree = tree;
    }

    public bool HasSpecs => _build?.Trees.Count > 0;

    public bool HasData => !_tree().IsEmpty;

    public bool CanDraw => HasSpecs && HasData;

    public string NoDataText => "SSE's copy of the passive tree is missing or damaged, so this build's tree cannot be drawn. Reinstalling SSE brings it back.";

    public IReadOnlyList<TreeSpecChoice> Specs { get; private set; } = [];

    public bool HasSpecChoice => Specs.Count > 1;

    public TreeAllocation? Allocation { get; private set; }

    public string Summary { get; private set; } = "";

    public string? Warning { get; private set; }

    public bool HasWarning => Warning is not null;

    public string Source => _tree() is { IsEmpty: false } t
        ? $"Drawn on the Path of Exile 2 tree from Grinding Gear Games' own export, game version {t.Version}. Wheel or the buttons to zoom, drag to move."
        : "";

    public bool HasAscendancy => Allocation?.Ascendancy is not null;

    public string AscendancyTitle => Allocation?.Ascendancy is { } a
        ? $"{a.Name}: {(Allocation.AscendancyPoints == 0 ? "no points yet" : Count(Allocation.AscendancyPoints))}"
        : "";

    public IReadOnlyList<TreePickViewModel> Picks { get; private set; } = [];

    public IReadOnlyList<TreePickViewModel> AscendancyPicks { get; private set; } = [];

    public bool HasPicks => Picks.Count > 0;

    public bool HasAscendancyPicks => AscendancyPicks.Count > 0;

    /// <summary>The node under the mouse in either drawing.</summary>
    [ObservableProperty]
    public partial TreeNode? Hovered { get; set; }

    partial void OnHoveredChanged(TreeNode? value)
    {
        OnPropertyChanged(nameof(HoveredText));
        OnPropertyChanged(nameof(HasHovered));
    }

    public string HoveredText => Hovered is { } n
        ? $"{(n.Name.Length > 0 ? n.Name : KindName(n.Kind))} ({KindName(n.Kind).ToLowerInvariant()}{(Allocation?.Nodes.Contains(n.Id) == true ? ", taken" : "")}){(n.Stats.Count > 0 ? ": " + TreePickViewModel.OneLine(string.Join("\n", n.Stats), "; ") : "")}"
        : "Point at a node to read it.";

    public bool HasHovered => Hovered is not null;

    [RelayCommand]
    private void ShowSpec(TreeSpecChoice? choice)
    {
        if (choice is null)
        {
            return;
        }

        _picked = choice.Index;
        Show(choice.Index);
    }

    /// <summary>Shows the build's tree for where the player is: the one they picked, else the latest
    /// whose title names a stage they have reached, else the one the author had open.</summary>
    public void Load(PobBuild? build, PlanPosition position)
    {
        var same = ReferenceEquals(build, _build);
        if (!same)
        {
            _picked = null;
        }

        _build = build;
        if (build is null || build.Trees.Count == 0)
        {
            (Specs, Allocation, Summary, Warning, Picks, AscendancyPicks, _shown) = ([], null, "", null, [], [], null);
            OnPropertyChanged(string.Empty);
            return;
        }

        var reached = -1;
        var stage = PlanStage.Start;
        for (var i = 0; i < build.Trees.Count; i++)
        {
            if (PlanNotesParser.StageInTitle(build.Trees[i].Title, stage) is { } named)
            {
                stage = named;
                if (named.ReachedBy(position.Act, position.Ascendancies))
                {
                    reached = i;
                }
            }
        }

        var index = _picked ?? (reached >= 0 ? reached : build.ActiveTree);
        if (!same || index != _shown)
        {
            Show(index); // a tick or a move elsewhere on the page leaves the drawing as it is
        }
    }

    private void Show(int index)
    {
        var build = _build!;
        index = Math.Clamp(index, 0, build.Trees.Count - 1);
        _shown = index;
        var spec = build.Trees[index];
        Specs = build.Trees.Select((t, i) => new TreeSpecChoice(i, t.Title ?? $"Tree {i + 1}", i == index)).ToList();
        var tree = _tree();
        if (tree.IsEmpty)
        {
            (Allocation, Picks, AscendancyPicks, Warning) = (null, [], [], null);
            Summary = $"{Count(spec.Nodes.Count)} in the code.";
            OnPropertyChanged(string.Empty);
            return;
        }

        var a = tree.Allocate(spec);
        Allocation = a;
        var parts = new List<string> { $"{Count(a.Points)} on the tree" };
        if (a.WeaponSetPoints(1) + a.WeaponSetPoints(2) > 0)
        {
            parts.Add($"{a.WeaponSetPoints(1)} for weapon set 1 and {a.WeaponSetPoints(2)} for weapon set 2 among them");
        }

        Summary = string.Join(" · ", parts);
        Warning = (a.SameVersion, a.Missing.Count) switch
        {
            (false, 0) => $"This tree was made for game version {Version(spec.TreeVersion)}; SSE has the {tree.Version} tree, so some points may have moved.",
            (false, var m) => $"This tree was made for game version {Version(spec.TreeVersion)}; SSE has the {tree.Version} tree, where {Count(m)} of it {(m == 1 ? "is" : "are")} not found and not drawn.",
            (true, 0) => null,
            (true, var m) => $"{Count(m)} of this tree {(m == 1 ? "is" : "are")} not on SSE's {tree.Version} tree and not drawn.",
        };
        Picks = a.Picks.Select(n => Pick(n, a)).ToList();
        AscendancyPicks = a.AscendancyPicks.Select(n => Pick(n, a)).ToList();
        Hovered = null;
        OnPropertyChanged(string.Empty);
    }

    private static TreePickViewModel Pick(TreeNode n, TreeAllocation a) =>
        new(n.Name, KindName(n.Kind), string.Join("\n", n.Stats), a.Notes.TryGetValue(n.Id, out var note) ? note : "");

    private static string KindName(TreeNodeKind kind) => kind switch
    {
        TreeNodeKind.Keystone => "Keystone",
        TreeNodeKind.Notable => "Notable",
        TreeNodeKind.Jewel => "Jewel socket",
        TreeNodeKind.Attribute => "Attribute",
        TreeNodeKind.Mastery => "Mastery",
        TreeNodeKind.Start => "Class start",
        TreeNodeKind.AscendancyStart => "Ascendancy start",
        _ => "Passive",
    };

    private static string Version(string? pob) => (pob ?? "unknown").Replace('_', '.');

    private static string Count(int n) => n.ToString(CultureInfo.InvariantCulture) + (n == 1 ? " point" : " points");
}
