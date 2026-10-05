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
}
