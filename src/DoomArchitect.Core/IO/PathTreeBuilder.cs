namespace DoomArchitect.Core.IO;

/// <summary>
/// Folds a flat list of `/`-delimited relative paths (the shape both
/// <see cref="Pk3File"/> and <see cref="DirectoryResource"/> already keep
/// their own entries in) into a real nested <see cref="ResourceTreeNode"/>
/// tree - the one shared implementation of that nesting logic, instead of
/// each container type writing it twice. Unlike <see cref="WadFile.BuildTree"/>
/// (which preserves a WAD's own real, meaningful lump order), this sorts
/// its output - folders before files, alphabetically within each - since a
/// PK3/directory's own entry order is just whatever its backing
/// dictionary happens to enumerate in, not something worth preserving;
/// matches the sorted file-explorer convention the browser is modeled on.
/// </summary>
public static class PathTreeBuilder
{
    public static ResourceTreeNode Build(string rootDisplayName, ResourceTreeNodeKind rootKind, IEnumerable<string> relativePaths)
    {
        var root = new ResourceTreeNode { DisplayName = rootDisplayName, Kind = rootKind };
        var foldersByPath = new Dictionary<string, ResourceTreeNode>(StringComparer.OrdinalIgnoreCase) { [""] = root };

        foreach (var path in relativePaths)
        {
            var segments = path.Split('/');
            var current = root;
            var currentPath = "";

            for (var i = 0; i < segments.Length - 1; i++)
            {
                currentPath = currentPath.Length == 0 ? segments[i] : $"{currentPath}/{segments[i]}";
                if (!foldersByPath.TryGetValue(currentPath, out var folder))
                {
                    folder = new ResourceTreeNode { DisplayName = segments[i], Kind = ResourceTreeNodeKind.Folder, Path = currentPath };
                    foldersByPath.Add(currentPath, folder);
                    current.Children.Add(folder);
                }

                current = folder;
            }

            current.Children.Add(new ResourceTreeNode { DisplayName = segments[^1], Kind = ResourceTreeNodeKind.File, Path = path });
        }

        SortRecursively(root);
        return root;
    }

    private static void SortRecursively(ResourceTreeNode node)
    {
        node.Children.Sort((a, b) =>
        {
            if (a.Kind != b.Kind && (a.Kind == ResourceTreeNodeKind.Folder || b.Kind == ResourceTreeNodeKind.Folder))
                return a.Kind == ResourceTreeNodeKind.Folder ? -1 : 1;

            return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
        });

        foreach (var child in node.Children) SortRecursively(child);
    }

    /// <summary>
    /// Replaces any <c>.wad</c>-named file directly inside a top-level
    /// folder literally named <c>maps</c> with its own real lump
    /// structure (<see cref="WadFile.BuildTree"/>), read via
    /// <paramref name="readFile"/> - the real GZDoom/ZDoom per-map-WAD
    /// convention (<c>OpenMapMenu.FindMapsSubfolder</c>'s own established
    /// scoping - top-level only, not any depth). Falls back to leaving a
    /// file as a plain leaf if <paramref name="readFile"/> returns null or
    /// the bytes don't parse as a real WAD - a stray or corrupt
    /// <c>.wad</c> shouldn't break browsing the rest of the folder. The
    /// replacement node deliberately keeps the original leaf's own
    /// <see cref="ResourceTreeNode.Path"/> (needed to resolve back to the
    /// real file later) while taking on the nested WAD's own
    /// <see cref="ResourceTreeNode.Kind"/>/<see cref="ResourceTreeNode.Children"/> -
    /// that's also exactly the signal a UI consumer needs to tell "a
    /// nested, expanded WAD root" apart from "the top-level resource's own
    /// synthetic root" (which <see cref="WadFile.BuildTree"/> never gives
    /// a <see cref="ResourceTreeNode.Path"/> to at all).
    /// </summary>
    public static void ExpandNestedWads(ResourceTreeNode root, Func<string, byte[]?> readFile)
    {
        foreach (var child in root.Children)
        {
            if (child.Kind == ResourceTreeNodeKind.Folder && child.DisplayName.Equals("maps", StringComparison.OrdinalIgnoreCase))
            {
                ExpandWadChildren(child, readFile);
            }
        }
    }

    private static void ExpandWadChildren(ResourceTreeNode mapsFolder, Func<string, byte[]?> readFile)
    {
        for (var i = 0; i < mapsFolder.Children.Count; i++)
        {
            var child = mapsFolder.Children[i];
            if (child.Kind != ResourceTreeNodeKind.File || !child.DisplayName.EndsWith(".wad", StringComparison.OrdinalIgnoreCase)) continue;

            var bytes = readFile(child.Path!);
            if (bytes == null) continue;

            WadFile nested;
            try { nested = WadFile.Read(new MemoryStream(bytes)); }
            catch { continue; }

            var nestedRoot = nested.BuildTree(child.DisplayName);
            var replacement = new ResourceTreeNode { DisplayName = child.DisplayName, Kind = nestedRoot.Kind, Path = child.Path };
            foreach (var grandchild in nestedRoot.Children) replacement.Children.Add(grandchild);
            mapsFolder.Children[i] = replacement;
        }
    }
}
