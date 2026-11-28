using System.IO;
using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Domain.Guide;

namespace StillSaneExile.Desktop.Services;

/// <summary>Reads <c>data/tree/poe2-tree.json</c>, which ships with the app, once per run. Nothing is
/// downloaded. A missing or broken file gives an empty tree: the guide then lists the build's points by
/// count only and says why it cannot draw them.</summary>
public static class PassiveTreeFile
{
    private static readonly Lock Gate = new();
    private static (string Path, PassiveTree Tree)? _loaded;

    public static PassiveTree Load(AppPaths paths)
    {
        lock (Gate)
        {
            if (_loaded is { } l && l.Path == paths.PassiveTree)
            {
                return l.Tree;
            }

            PassiveTree tree;
            try
            {
                tree = PassiveTree.Parse(File.ReadAllText(paths.PassiveTree));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
            {
                tree = PassiveTree.Empty;
            }

            _loaded = (paths.PassiveTree, tree);
            return tree;
        }
    }
}
