using System.IO.Compression;

namespace DoomArchitect.Core.IO;

/// <summary>
/// Reads a PK3 - a real GZDoom/ZDoom-convention zip archive that stands in
/// for a WAD's flat lump directory with a folder structure instead
/// (<c>patches/</c>, <c>textures/</c>, <c>flats/</c>, <c>sprites/</c>,
/// <c>graphics/</c> in place of <c>P_START</c>/<c>P_END</c> etc.).
/// Deliberately built on .NET's own built-in
/// <see cref="System.IO.Compression.ZipArchive"/> instead of a third-party
/// archive library that also tolerates a PK3 secretly being a rar/7z file -
/// a real PK3 is a zip file by spec, and that extra tolerance isn't a
/// real-world need here.
///
/// The archive is opened once and its entry index built eagerly (so a bad
/// file fails fast at <see cref="Open"/>, matching <see cref="WadFile.Read(string)"/>'s
/// own eager-validate style), then kept open for the lifetime of this
/// instance - individual entries are still decompressed lazily, on each
/// <see cref="FindLump"/>/<see cref="FindNamespaceLumps"/> call that
/// actually needs their bytes, since <see cref="Textures.TextureSet"/>
/// already caches every resolved texture/flat/sprite/patch by name, so a
/// second cache in here would just double memory for no benefit. Call
/// <see cref="Dispose"/> for prompt release of the underlying file handle;
/// it isn't required (the handle is released on garbage collection either
/// way) since nothing upstream currently tracks container lifetimes to
/// call it deterministically.
/// </summary>
public sealed class Pk3File : IResourceContainer, IDisposable
{
    private readonly Stream _stream;
    private readonly ZipArchive _archive;
    private readonly Dictionary<string, ZipArchiveEntry> _entriesByPath = new(StringComparer.OrdinalIgnoreCase);

    private Pk3File(Stream stream, ZipArchive archive)
    {
        _stream = stream;
        _archive = archive;

        foreach (var entry in archive.Entries)
        {
            // A directory entry's own FullName ends with '/', which leaves
            // Name empty - nothing to index, it carries no data of its own.
            if (entry.Name.Length == 0) continue;

            var key = Normalize(entry.FullName);
            // First entry wins on a duplicate path, later ones are dropped -
            // matches UDB's own behavior, not real GZDoom's last-wins.
            _entriesByPath.TryAdd(key, entry);
        }
    }

    public static Pk3File Open(string path) => Open(File.OpenRead(path));

    /// <summary>Mirrors <see cref="WadFile.Read(Stream)"/>'s own path/stream split - mainly so tests can build a PK3 entirely in memory, without touching the filesystem.</summary>
    public static Pk3File Open(Stream stream)
    {
        try
        {
            var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            return new Pk3File(stream, archive);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The fallback order GZDoom/UDB search patches in when a lookup isn't
    /// restricted to one specific namespace. Applied here as this
    /// container's one general-purpose lookup order, since DoomArchitect's
    /// texture pipeline doesn't yet distinguish call-sites as finely as UDB
    /// does (a deliberate simplification).
    /// </summary>
    private static readonly ResourceNamespace[] FallbackOrder =
    {
        ResourceNamespace.Patches, ResourceNamespace.Textures, ResourceNamespace.Flats,
        ResourceNamespace.Sprites, ResourceNamespace.Graphics,
    };

    public WadLump? FindLump(string name)
    {
        foreach (var (path, entry) in _entriesByPath)
        {
            if (path.Contains('/')) continue; // root entries only, checked first
            if (TitleMatches(path, name)) return ReadLump(name, entry);
        }

        foreach (var ns in FallbackOrder)
        {
            var lump = FindInNamespace(ns, name);
            if (lump != null) return lump;
        }

        return null;
    }

    /// <summary>An exact, normalized full-path match (e.g. `zscript/actors/actor.zs`) - falls back to a root-level title match (ignoring the query's own extension) for an include written as a bare title rather than a full path.</summary>
    public byte[]? FindByPath(string path)
    {
        var normalized = Normalize(path);
        if (_entriesByPath.TryGetValue(normalized, out var entry)) return ReadLump(path, entry).Data;

        return FindLump(System.IO.Path.GetFileNameWithoutExtension(path))?.Data;
    }

    public IReadOnlyList<WadLump> FindNamespaceLumps(ResourceNamespace ns)
    {
        var folder = FolderFor(ns) + "/";
        var result = new List<WadLump>();

        foreach (var (path, entry) in _entriesByPath)
        {
            if (!path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) continue;
            var remainder = path[folder.Length..];
            if (remainder.Length == 0 || remainder.Contains('/')) continue; // direct children only

            result.Add(ReadLump(Path.GetFileNameWithoutExtension(remainder), entry));
        }

        return result;
    }

    private WadLump? FindInNamespace(ResourceNamespace ns, string name)
    {
        var folder = FolderFor(ns) + "/";
        foreach (var (path, entry) in _entriesByPath)
        {
            if (!path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) continue;
            var remainder = path[folder.Length..];
            if (remainder.Length == 0 || remainder.Contains('/')) continue;
            if (TitleMatches(remainder, name)) return ReadLump(name, entry);
        }

        return null;
    }

    /// <summary>Every real entry's own full path, folded into a real nested tree by <see cref="PathTreeBuilder"/> - directory entries were never indexed in the first place (see the constructor's own remarks), so nothing extra needs excluding here.</summary>
    public ResourceTreeNode BuildTree(string displayName) =>
        PathTreeBuilder.Build(displayName, ResourceTreeNodeKind.Pk3Container, _entriesByPath.Keys);

    /// <summary>A zip archive's own entries aren't independently addressable files on disk at all - never a match.</summary>
    public bool ContainsFile(string absolutePath) => false;

    /// <summary>A zip archive entry has no standalone on-disk path of its own to resolve to.</summary>
    public string? ResolveAbsolutePath(string relativePath) => null;

    private static bool TitleMatches(string path, string name) =>
        Path.GetFileNameWithoutExtension(path).Equals(name, StringComparison.OrdinalIgnoreCase);

    private static WadLump ReadLump(string name, ZipArchiveEntry entry)
    {
        using var entryStream = entry.Open();
        using var memory = new MemoryStream();
        entryStream.CopyTo(memory);
        return new WadLump(name, memory.ToArray());
    }

    private static string FolderFor(ResourceNamespace ns) => ns switch
    {
        ResourceNamespace.Patches => "patches",
        ResourceNamespace.Textures => "textures",
        ResourceNamespace.Flats => "flats",
        ResourceNamespace.Sprites => "sprites",
        ResourceNamespace.Graphics => "graphics",
        _ => throw new ArgumentOutOfRangeException(nameof(ns)),
    };

    /// <summary>
    /// .NET's own <see cref="ZipArchiveEntry.FullName"/> is always
    /// '/'-separated per the zip spec, but a defensive backslash swap costs
    /// nothing and guards against archives written by non-compliant tools.
    /// </summary>
    private static string Normalize(string path) => path.Replace('\\', '/');

    public void Dispose()
    {
        _archive.Dispose();
        _stream.Dispose();
    }
}
