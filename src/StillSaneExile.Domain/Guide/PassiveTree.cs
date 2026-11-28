using System.Text.Json;

namespace StillSaneExile.Domain.Guide;

public enum TreeNodeKind
{
    Normal,
    Attribute,
    Notable,
    Keystone,
    Jewel,
    Mastery,

    /// <summary>A class's starting point at the centre of the tree.</summary>
    Start,

    AscendancyStart,
}

/// <summary>A passive skill node, as Grinding Gear Games' export has it. <see cref="Stats"/> are the
/// game's lines with their markup removed.</summary>
public sealed record TreeNode(int Id, string Name, TreeNodeKind Kind, double X, double Y, string? Ascendancy, IReadOnlyList<string> Stats)
{
    public bool IsPick => Kind is TreeNodeKind.Notable or TreeNodeKind.Keystone;

    /// <summary>Counts as a passive point when allocated: everything but the starting points.</summary>
    public bool IsPoint => Kind is not (TreeNodeKind.Start or TreeNodeKind.AscendancyStart);
}

/// <summary>A connection between two nodes. A curve along an orbit has the orbit's centre; the rest are
/// straight lines.</summary>
public sealed record TreeEdge(int From, int To, double? CentreX, double? CentreY)
{
    public bool IsArc => CentreX is not null && CentreY is not null;
}

public sealed record TreeAscendancy(string Id, string Name, string ClassName);

/// <summary>Path of Exile 2's passive tree from <c>data/tree/poe2-tree.json</c>, a trimmed copy of Grinding
/// Gear Games' official export (github.com/grindinggear/poe2-skilltree-export, listed under "Data
/// Exports" in GGG's developer docs) made by <c>scripts/update-passive-tree.ps1</c>. It ships with the
/// app; nothing is downloaded.</summary>
public sealed class PassiveTree
{
    private PassiveTree(string version, string source, string fetched, IReadOnlyDictionary<int, TreeNode> nodes, IReadOnlyList<TreeEdge> edges, IReadOnlyList<TreeAscendancy> ascendancies)
    {
        Version = version;
        Source = source;
        Fetched = fetched;
        Nodes = nodes;
        Edges = edges;
        Ascendancies = ascendancies;
    }

    public static PassiveTree Empty { get; } = new("", "", "", new Dictionary<int, TreeNode>(), [], []);

    /// <summary>The game version of the export ("0.5.5").</summary>
    public string Version { get; }

    public string Source { get; }

    public string Fetched { get; }

    public IReadOnlyDictionary<int, TreeNode> Nodes { get; }

    public IReadOnlyList<TreeEdge> Edges { get; }

    public IReadOnlyList<TreeAscendancy> Ascendancies { get; }

    public bool IsEmpty => Nodes.Count == 0;

    /// <exception cref="FormatException">The file is not a tree SSE can draw.</exception>
    public static PassiveTree Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var nodes = new Dictionary<int, TreeNode>();
            foreach (var n in root.GetProperty("nodes").EnumerateArray())
            {
                var stats = n.TryGetProperty("stats", out var s) ? s.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : [];
                var node = new TreeNode(
                    n.GetProperty("id").GetInt32(),
                    n.GetProperty("name").GetString() ?? "",
                    Kind(n.GetProperty("kind").GetString()),
                    n.GetProperty("x").GetDouble(),
                    n.GetProperty("y").GetDouble(),
                    n.TryGetProperty("ascendancy", out var a) ? a.GetString() : null,
                    stats);
                nodes[node.Id] = node;
            }

            var edges = new List<TreeEdge>();
            foreach (var e in root.GetProperty("edges").EnumerateArray())
            {
                var v = e.EnumerateArray().ToList();
                var edge = new TreeEdge(v[0].GetInt32(), v[1].GetInt32(), v.Count > 3 ? v[2].GetDouble() : null, v.Count > 3 ? v[3].GetDouble() : null);
                if (nodes.ContainsKey(edge.From) && nodes.ContainsKey(edge.To))
                {
                    edges.Add(edge);
                }
            }

            var ascendancies = root.GetProperty("classes").EnumerateArray()
                .SelectMany(c => c.GetProperty("ascendancies").EnumerateArray().Select(a => new TreeAscendancy(
                    a.GetProperty("id").GetString() ?? "",
                    a.GetProperty("name").GetString() ?? "",
                    c.GetProperty("name").GetString() ?? "")))
                .ToList();
            if (nodes.Count == 0)
            {
                throw new FormatException("The passive tree file has no nodes.");
            }

            return new PassiveTree(Text(root, "version"), Text(root, "source"), Text(root, "fetched"), nodes, edges, ascendancies);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            throw new FormatException("The passive tree file could not be read.", e);
        }
    }

    /// <summary>Where a build's tree lands on this tree. Nodes this tree does not have (another game
    /// version) are counted, never guessed.</summary>
    public TreeAllocation Allocate(PobTreeSpec spec)
    {
        var found = spec.Nodes.Where(Nodes.ContainsKey).ToHashSet();
        var missing = spec.Nodes.Where(id => !Nodes.ContainsKey(id)).ToList();

        // Path of Building writes the ascendancy's internal id, the same id as the export's. If it does
        // not match, the allocated ascendancy points say which one it is.
        var ascendancy = Ascendancies.FirstOrDefault(a => a.Id == spec.AscendancyId)
            ?? found.Select(id => Nodes[id].Ascendancy).OfType<string>().GroupBy(a => a).OrderByDescending(g => g.Count())
                .Select(g => Ascendancies.FirstOrDefault(a => a.Id == g.Key)).FirstOrDefault();
        return new TreeAllocation(this, found, missing, ascendancy, spec.WeaponSets, spec.NodeNotes, SameVersion(spec.TreeVersion));
    }

    /// <summary>Path of Building names tree versions "0_5"; the export is "0.5.5". The same major and
    /// minor version is the same tree as far as SSE can tell.</summary>
    internal bool SameVersion(string? pobVersion)
    {
        if (string.IsNullOrEmpty(pobVersion) || Version.Length == 0)
        {
            return true;
        }

        var wanted = string.Join('.', pobVersion.Split('_').Take(2));
        return Version == wanted || Version.StartsWith(wanted + ".", StringComparison.Ordinal);
    }

    private static TreeNodeKind Kind(string? kind) => kind switch
    {
        "attribute" => TreeNodeKind.Attribute,
        "notable" => TreeNodeKind.Notable,
        "keystone" => TreeNodeKind.Keystone,
        "jewel" => TreeNodeKind.Jewel,
        "mastery" => TreeNodeKind.Mastery,
        "start" => TreeNodeKind.Start,
        "ascendancyStart" => TreeNodeKind.AscendancyStart,
        _ => TreeNodeKind.Normal,
    };

    private static string Text(JsonElement root, string name) => root.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";
}

/// <summary>A build's passive tree laid over <see cref="PassiveTree"/>.</summary>
public sealed record TreeAllocation(
    PassiveTree Tree,
    IReadOnlySet<int> Nodes,
    IReadOnlyList<int> Missing,
    TreeAscendancy? Ascendancy,
    IReadOnlyDictionary<int, int> WeaponSets,
    IReadOnlyDictionary<int, string> Notes,
    bool SameVersion)
{
    public IEnumerable<TreeNode> Allocated => Nodes.Select(id => Tree.Nodes[id]);

    public int Points => Allocated.Count(n => n.Ascendancy is null && n.IsPoint);

    public int AscendancyPoints => Allocated.Count(n => n.Ascendancy is not null && n.IsPoint);

    /// <summary>Keystones first, then notables, each in tree order (by id), on the main tree.</summary>
    public IReadOnlyList<TreeNode> Picks => Allocated.Where(n => n.Ascendancy is null && n.IsPick).OrderBy(n => n.Kind == TreeNodeKind.Keystone ? 0 : 1).ThenBy(n => n.Id).ToList();

    public IReadOnlyList<TreeNode> AscendancyPicks => Allocated.Where(n => n.Ascendancy is not null && n.IsPick).OrderBy(n => n.Id).ToList();

    public int WeaponSetPoints(int set) => WeaponSets.Count(w => w.Value == set && Nodes.Contains(w.Key));

    /// <summary>Whether an edge runs between two allocated nodes.</summary>
    public bool Takes(TreeEdge edge) => Nodes.Contains(edge.From) && Nodes.Contains(edge.To);
}
