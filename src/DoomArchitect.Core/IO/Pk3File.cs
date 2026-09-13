using System.IO.Compression;

namespace DoomArchitect.Core.IO;

/// <summary>
/// Reads a PK3 - a real GZDoom/ZDoom-convention zip archive that stands in
/// for a WAD's flat lump directory with a folder structure instead
/// (<c>patches/</c>, <c>textures/</c>, <c>flats/</c>, <c>sprites/</c>,
/// <c>graphics/</c> in place of <c>P_START</c>/<c>P_END</c> etc. - verified
/// against UDB's own real <c>PK3StructuredReader</c>). Deliberately built on
/// .NET's own built-in <see cref="System.IO.Compression.ZipArchive"/>
/// instead of the third-party archive library UDB uses (SharpCompress,
/// there only so UDB can tolerate a PK3 that's secretly a rar/7z file) -
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
            // matches UDB's own real PK3 behavior (DirectoryFilesList.cs),
            // not real GZDoom's last-wins - see the plan's Context section
            // for why this project follows UDB here rather than "fixing" it.
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
    /// The real fallback order GZDoom/UDB search patches in when a lookup
    /// isn't restricted to one specific namespace (UDB's own
    /// <c>PatchLocations</c>) - applied here as this container's one
    /// general-purpose lookup order, since DoomArchitect's texture pipeline
    /// doesn't yet distinguish call-sites as finely as UDB's does (a
    /// deliberate simplification, not a UDB behavior gap).
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
    /// nothing and guards against archives written by non-compliant tools -
    /// the same real-world gotcha UDB itself guards against explicitly.
    /// </summary>
    private static string Normalize(string path) => path.Replace('\\', '/');

    public void Dispose()
    {
        _archive.Dispose();
        _stream.Dispose();
    }
}
