namespace DoomArchitect.Core.IO;

/// <summary>
/// Reads a loose folder on disk laid out with GZDoom/ZDoom's real PK3
/// folder convention (<c>patches/</c>, <c>textures/</c>, <c>flats/</c>,
/// <c>sprites/</c>, <c>graphics/</c>) instead of an actual zip archive -
/// UDB's own real "Directory" resource type
/// (<c>DataLocation.RESOURCE_DIRECTORY</c>/<c>DirectoryReader</c>), useful
/// for iterating on a mod's own loose ZSCRIPT/DECORATE/textures without
/// re-zipping into a PK3 on every change. Lookup logic mirrors
/// <see cref="Pk3File"/> exactly (same fallback-namespace order, same
/// first-entry-wins-on-duplicate-path rule) - a folder is functionally a
/// PK3 with its entries unzipped onto disk, nothing about the resolution
/// rules themselves differs.
///
/// Unlike <see cref="Pk3File"/>, no archive handle is held open - the
/// entry index (built eagerly, same as <see cref="Pk3File"/>) only
/// records each file's real on-disk path (preserving its actual casing,
/// which matters on case-sensitive filesystems even though lookups here
/// are case-insensitive), and content is read fresh via
/// <see cref="File.ReadAllBytes(string)"/> on every lookup that needs it -
/// there's nothing to <c>Dispose</c>.
/// </summary>
public sealed class DirectoryResource : IResourceContainer
{
    private readonly Dictionary<string, string> _pathsByRelativePath = new(StringComparer.OrdinalIgnoreCase);

    private DirectoryResource(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Normalize(Path.GetRelativePath(root, file));
            // First entry wins on a duplicate path (e.g. two files whose
            // names differ only by case on a case-sensitive filesystem) -
            // matches Pk3File's own rule; which file that actually is
            // depends on filesystem enumeration order, same real-world
            // ambiguity UDB's own DirectoryReader has.
            _pathsByRelativePath.TryAdd(relative, file);
        }
    }

    public static DirectoryResource Open(string root) => new(root);

    /// <summary>Same fallback order as <see cref="Pk3File.FindLump"/> - see its own remarks.</summary>
    private static readonly ResourceNamespace[] FallbackOrder =
    {
        ResourceNamespace.Patches, ResourceNamespace.Textures, ResourceNamespace.Flats,
        ResourceNamespace.Sprites, ResourceNamespace.Graphics,
    };

    public WadLump? FindLump(string name)
    {
        foreach (var (relative, fullPath) in _pathsByRelativePath)
        {
            if (relative.Contains('/')) continue; // root entries only, checked first
            if (TitleMatches(relative, name)) return ReadLump(name, fullPath);
        }

        foreach (var ns in FallbackOrder)
        {
            var lump = FindInNamespace(ns, name);
            if (lump != null) return lump;
        }

        return null;
    }

    /// <summary>An exact, normalized relative-path match - falls back to a root-level title match (ignoring the query's own extension) for an include written as a bare title rather than a full path. Mirrors <see cref="Pk3File.FindByPath"/>.</summary>
    public byte[]? FindByPath(string path)
    {
        var normalized = Normalize(path);
        if (_pathsByRelativePath.TryGetValue(normalized, out var fullPath)) return File.ReadAllBytes(fullPath);

        return FindLump(Path.GetFileNameWithoutExtension(path))?.Data;
    }

    public IReadOnlyList<WadLump> FindNamespaceLumps(ResourceNamespace ns)
    {
        var folder = FolderFor(ns) + "/";
        var result = new List<WadLump>();

        foreach (var (relative, fullPath) in _pathsByRelativePath)
        {
            if (!relative.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) continue;
            var remainder = relative[folder.Length..];
            if (remainder.Length == 0 || remainder.Contains('/')) continue; // direct children only

            result.Add(ReadLump(Path.GetFileNameWithoutExtension(remainder), fullPath));
        }

        return result;
    }

    private WadLump? FindInNamespace(ResourceNamespace ns, string name)
    {
        var folder = FolderFor(ns) + "/";
        foreach (var (relative, fullPath) in _pathsByRelativePath)
        {
            if (!relative.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) continue;
            var remainder = relative[folder.Length..];
            if (remainder.Length == 0 || remainder.Contains('/')) continue;
            if (TitleMatches(remainder, name)) return ReadLump(name, fullPath);
        }

        return null;
    }

    /// <summary>
    /// Every real file's own relative path, folded into a real nested
    /// tree by <see cref="PathTreeBuilder"/> - mirrors
    /// <see cref="Pk3File.BuildTree"/>. A second pass
    /// (<see cref="PathTreeBuilder.ExpandNestedWads"/>) then expands any
    /// real GZDoom/ZDoom per-map WAD it finds directly inside a top-level
    /// <c>maps/</c> folder into its own real lump structure - re-reads
    /// each one's current bytes fresh every call (<see cref="File.ReadAllBytes(string)"/>,
    /// matching this class's own existing "nothing cached" design), so a
    /// later write to one of them is picked up on the very next refresh.
    /// </summary>
    public ResourceTreeNode BuildTree(string displayName)
    {
        var root = PathTreeBuilder.Build(displayName, ResourceTreeNodeKind.DirectoryContainer, _pathsByRelativePath.Keys);
        PathTreeBuilder.ExpandNestedWads(root, relativePath => _pathsByRelativePath.TryGetValue(relativePath, out var fullPath) ? File.ReadAllBytes(fullPath) : null);
        return root;
    }

    /// <summary>Compares full, normalized paths rather than the raw string, so a differently-spelled but equivalent path (relative vs. absolute, a trailing separator, mixed slash direction) still matches.</summary>
    public bool ContainsFile(string absolutePath)
    {
        var normalized = Path.GetFullPath(absolutePath);
        foreach (var fullPath in _pathsByRelativePath.Values)
        {
            if (string.Equals(Path.GetFullPath(fullPath), normalized, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary><paramref name="relativePath"/> is expected verbatim as <see cref="BuildTree"/>'s own output names it (one of this container's own <c>_pathsByRelativePath</c> keys) - a direct, case-insensitive dictionary lookup, nothing to normalize.</summary>
    public string? ResolveAbsolutePath(string relativePath) =>
        _pathsByRelativePath.TryGetValue(relativePath, out var fullPath) ? fullPath : null;

    private static bool TitleMatches(string relative, string name) =>
        Path.GetFileNameWithoutExtension(relative).Equals(name, StringComparison.OrdinalIgnoreCase);

    private static WadLump ReadLump(string name, string fullPath) => new(name, File.ReadAllBytes(fullPath));

    private static string FolderFor(ResourceNamespace ns) => ns switch
    {
        ResourceNamespace.Patches => "patches",
        ResourceNamespace.Textures => "textures",
        ResourceNamespace.Flats => "flats",
        ResourceNamespace.Sprites => "sprites",
        ResourceNamespace.Graphics => "graphics",
        _ => throw new ArgumentOutOfRangeException(nameof(ns)),
    };

    private static string Normalize(string path) => path.Replace('\\', '/');
}
