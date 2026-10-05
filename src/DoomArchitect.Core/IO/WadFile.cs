using System.Text;

namespace DoomArchitect.Core.IO;

public sealed class WadLump
{
    public WadLump(string name, byte[] data)
    {
        Name = name;
        Data = data;
    }

    public string Name { get; }

    public byte[] Data { get; }
}

/// <summary>One map's own contiguous lump run, found by <see cref="WadFile.FindMapLumpGroups"/> - <see cref="Lumps"/> is everything strictly after the marker (not including the marker itself), up to (and, for a UDMF group, including) its own terminator.</summary>
public sealed record MapLumpGroup(string MarkerName, bool IsUdmf, IReadOnlyList<WadLump> Lumps);

/// <summary>
/// The two real map-lump-name sets, confirmed from the UDMF spec (classic
/// binary-format lumps) and this project's own supported UDMF lump set -
/// the single shared source of truth every "find this map's own lump group"
/// consumer in this file/<see cref="ClassicMapReader"/>/<see cref="MapFileSaver"/>
/// reads from, instead of each maintaining its own copy.
/// </summary>
public static class WadMapLumpNames
{
    public static readonly IReadOnlyList<string> Classic = new[]
    {
        "THINGS", "LINEDEFS", "SIDEDEFS", "VERTEXES", "SEGS", "SSECTORS",
        "NODES", "SECTORS", "REJECT", "BLOCKMAP", "BEHAVIOR", "SCRIPTS",
    };

    /// <summary>Everything that can follow a map marker's own <c>TEXTMAP</c> lump, per the UDMF spec - lumps this project doesn't model yet (<c>BEHAVIOR</c>/<c>DIALOGUE</c>/<c>ZNODES</c>/<c>BLOCKMAP</c>/<c>REJECT</c>/<c>SCRIPTS</c>) are included here purely so they're recognized as part of the group, not silently dropped.</summary>
    public static readonly IReadOnlyList<string> Udmf = new[]
    {
        "TEXTMAP", "BEHAVIOR", "DIALOGUE", "ZNODES", "BLOCKMAP", "REJECT", "SCRIPTS", "ENDMAP",
    };
}

/// <summary>
/// Reads the classic WAD container format: a 12-byte header
/// (identification, lump count, directory offset) followed by a flat
/// directory of (position, size, 8-character name) entries, each
/// pointing at a run of raw bytes elsewhere in the file. A map's lumps
/// are identified purely by their position relative to a map marker lump
/// (e.g. "MAP01") - the format itself has no explicit grouping.
/// </summary>
public sealed class WadFile : IResourceContainer
{
    private WadFile(IReadOnlyList<WadLump> lumps)
    {
        Lumps = lumps;
    }

    public IReadOnlyList<WadLump> Lumps { get; }

    public static WadFile Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public static WadFile Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

        var identification = new string(reader.ReadChars(4));
        if (identification != "IWAD" && identification != "PWAD")
        {
            throw new InvalidDataException($"Not a WAD file (expected 'IWAD' or 'PWAD', found '{identification}').");
        }

        var lumpCount = reader.ReadInt32();
        var directoryOffset = reader.ReadInt32();

        stream.Seek(directoryOffset, SeekOrigin.Begin);
        var entries = new (int Position, int Size, string Name)[lumpCount];
        for (var i = 0; i < lumpCount; i++)
        {
            var position = reader.ReadInt32();
            var size = reader.ReadInt32();
            var name = ReadLumpName(reader);
            entries[i] = (position, size, name);
        }

        var lumps = new List<WadLump>(lumpCount);
        foreach (var entry in entries)
        {
            stream.Seek(entry.Position, SeekOrigin.Begin);
            lumps.Add(new WadLump(entry.Name, reader.ReadBytes(entry.Size)));
        }

        return new WadFile(lumps);
    }

    private static string ReadLumpName(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(8);
        var length = Array.IndexOf(bytes, (byte)0);
        if (length < 0) length = bytes.Length;
        return Encoding.ASCII.GetString(bytes, 0, length);
    }

    public WadLump? FindLump(string name) =>
        Lumps.FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The index of the first lump named <paramref name="name"/>, or -1 - shared by every "locate a map's own marker" lookup (<see cref="ClassicMapReader"/>/<see cref="MapFileSaver"/> each used to re-implement this identically).</summary>
    public static int FindMarkerIndex(IReadOnlyList<WadLump> lumps, string name)
    {
        for (var i = 0; i < lumps.Count; i++)
        {
            if (lumps[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return i;
        }

        return -1;
    }

    /// <summary>
    /// Scans forward from <paramref name="start"/> while each lump's name is
    /// in <paramref name="knownNames"/>, returning the index just past the
    /// group - the one shared "collect a map's own lump run" primitive every
    /// consumer in this file/<see cref="ClassicMapReader"/>/<see cref="MapFileSaver"/>
    /// now goes through, instead of each independently re-scanning. Stops
    /// immediately after <paramref name="stopAfterName"/> if given (UDMF's
    /// own <c>ENDMAP</c> terminator) - without it, stops at the first lump
    /// whose name isn't in the known set at all (the classic-format case,
    /// which has no terminator lump of its own).
    /// </summary>
    public static int FindGroupEnd(IReadOnlyList<WadLump> lumps, int start, IReadOnlyCollection<string> knownNames, string? stopAfterName = null)
    {
        var i = start;
        while (i < lumps.Count && knownNames.Contains(lumps[i].Name, StringComparer.OrdinalIgnoreCase))
        {
            var isStop = stopAfterName != null && lumps[i].Name.Equals(stopAfterName, StringComparison.OrdinalIgnoreCase);
            i++;
            if (isStop) break;
        }

        return i;
    }

    /// <summary>
    /// Every map in this WAD, each with its own marker name and real lump
    /// group (<see cref="FindGroupEnd"/>, same real UDMF-vs-classic
    /// lookahead <see cref="FindUdmfMapNames"/>/<see cref="FindClassicMapNames"/>
    /// already use) - the general version those two only ever needed a
    /// single-lump lookahead for, built for a tree-browser-style consumer
    /// that needs each map's own full, ordered lump list, not just its
    /// name. Walks the whole lump list once, in order; any lump not
    /// recognized as starting a map (and not already consumed as part of
    /// one) is simply skipped over here - a caller that wants "every lump
    /// not in any map group" can still derive that by set difference against
    /// <see cref="Lumps"/>.
    /// </summary>
    public IReadOnlyList<MapLumpGroup> FindMapLumpGroups()
    {
        var groups = new List<MapLumpGroup>();
        var i = 0;

        while (i < Lumps.Count)
        {
            var isUdmf = i + 1 < Lumps.Count && Lumps[i + 1].Name.Equals("TEXTMAP", StringComparison.OrdinalIgnoreCase);
            var isClassic = !isUdmf && i + 1 < Lumps.Count && Lumps[i + 1].Name.Equals("THINGS", StringComparison.OrdinalIgnoreCase);

            if (!isUdmf && !isClassic)
            {
                i++;
                continue;
            }

            var markerName = Lumps[i].Name;
            var bodyStart = i + 1;
            var bodyEnd = isUdmf
                ? FindGroupEnd(Lumps, bodyStart, WadMapLumpNames.Udmf, stopAfterName: "ENDMAP")
                : FindGroupEnd(Lumps, bodyStart, WadMapLumpNames.Classic);

            groups.Add(new MapLumpGroup(markerName, isUdmf, Lumps.Skip(bodyStart).Take(bodyEnd - bodyStart).ToList()));
            i = bodyEnd;
        }

        return groups;
    }

    /// <summary>A WAD has no real path hierarchy - a ZScript `#include` inside one references another lump directly by (bare) name, so this just strips any path/extension and delegates to <see cref="FindLump"/>.</summary>
    public byte[]? FindByPath(string path) => FindLump(Path.GetFileNameWithoutExtension(path))?.Data;

    /// <summary>
    /// Every lump strictly between the first <paramref name="startMarker"/>
    /// and the next <paramref name="endMarker"/> that follows it (e.g.
    /// <c>S_START</c>/<c>S_END</c> bounding a WAD's sprites) - empty if
    /// either marker is missing. The first "marker-bounded range" lookup in
    /// this codebase; <see cref="FindUdmfMapNames"/>/
    /// <see cref="FindClassicMapNames"/> only ever need a single-lump
    /// lookahead, not a whole range.
    /// </summary>
    public IReadOnlyList<WadLump> FindLumpsBetweenMarkers(string startMarker, string endMarker)
    {
        var startIndex = -1;
        for (var i = 0; i < Lumps.Count; i++)
        {
            if (Lumps[i].Name.Equals(startMarker, StringComparison.OrdinalIgnoreCase))
            {
                startIndex = i;
                break;
            }
        }

        if (startIndex < 0) return Array.Empty<WadLump>();

        var result = new List<WadLump>();
        for (var i = startIndex + 1; i < Lumps.Count; i++)
        {
            if (Lumps[i].Name.Equals(endMarker, StringComparison.OrdinalIgnoreCase)) return result;
            result.Add(Lumps[i]);
        }

        return Array.Empty<WadLump>();
    }

    /// <summary>
    /// Maps a <see cref="ResourceNamespace"/> onto the real WAD marker pair
    /// that bounds it and delegates to <see cref="FindLumpsBetweenMarkers"/>.
    /// <see cref="ResourceNamespace.Graphics"/> has no WAD equivalent at all
    /// - it's a PK3-only convention - so it always returns empty here.
    /// </summary>
    public IReadOnlyList<WadLump> FindNamespaceLumps(ResourceNamespace ns) => ns switch
    {
        ResourceNamespace.Patches => FindLumpsBetweenMarkers("P_START", "P_END"),
        ResourceNamespace.Textures => FindLumpsBetweenMarkers("TX_START", "TX_END"),
        ResourceNamespace.Flats => FindLumpsBetweenMarkers("F_START", "F_END"),
        ResourceNamespace.Sprites => FindLumpsBetweenMarkers("S_START", "S_END"),
        _ => Array.Empty<WadLump>(),
    };

    /// <summary>
    /// Names of every map marker lump immediately followed by
    /// <c>TEXTMAP</c>, in file order - i.e. every map in this WAD that
    /// <see cref="ReadMapTextMap"/> can actually load.
    /// </summary>
    public IReadOnlyList<string> FindUdmfMapNames()
    {
        var names = new List<string>();
        for (var i = 0; i < Lumps.Count - 1; i++)
        {
            if (Lumps[i + 1].Name.Equals("TEXTMAP", StringComparison.OrdinalIgnoreCase))
            {
                names.Add(Lumps[i].Name);
            }
        }

        return names;
    }

    /// <summary>
    /// Names of every map marker lump immediately followed by
    /// <c>THINGS</c>, in file order - a classic binary-format map marker
    /// always starts with that lump. Doesn't distinguish Doom-format from
    /// Hexen/ZDoom-format (both start the same way) - that's
    /// <see cref="ClassicMapReader.Read"/>'s job, since telling them apart
    /// means actually scanning the lump group for <c>BEHAVIOR</c>.
    /// </summary>
    public IReadOnlyList<string> FindClassicMapNames()
    {
        var names = new List<string>();
        for (var i = 0; i < Lumps.Count - 1; i++)
        {
            if (Lumps[i + 1].Name.Equals("THINGS", StringComparison.OrdinalIgnoreCase))
            {
                names.Add(Lumps[i].Name);
            }
        }

        return names;
    }

    /// <summary>
    /// The UDMF <c>TEXTMAP</c> lump for the named map, decoded as ASCII
    /// text - per the UDMF spec, this must be the very first lump after
    /// the map marker. Throws if the map exists but isn't in UDMF format;
    /// classic binary-format maps (as shipped in the original id Software
    /// WADs) aren't supported yet - see TODO/TODO.md.
    /// </summary>
    public string ReadMapTextMap(string mapName)
    {
        var markerIndex = -1;
        for (var i = 0; i < Lumps.Count; i++)
        {
            if (Lumps[i].Name.Equals(mapName, StringComparison.OrdinalIgnoreCase))
            {
                markerIndex = i;
                break;
            }
        }

        if (markerIndex < 0) throw new KeyNotFoundException($"No map named '{mapName}' found in this WAD.");

        var nextIndex = markerIndex + 1;
        if (nextIndex >= Lumps.Count || !Lumps[nextIndex].Name.Equals("TEXTMAP", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"Map '{mapName}' is not in UDMF format - classic binary-format maps aren't supported yet.");
        }

        return Encoding.ASCII.GetString(Lumps[nextIndex].Data);
    }

    /// <summary>
    /// A browsable tree for this WAD: one <see cref="ResourceTreeNodeKind.MapGroup"/>
    /// child per map (<see cref="FindMapLumpGroups"/>, each with its own
    /// lump children, in order), every other lump a flat top-level
    /// <see cref="ResourceTreeNodeKind.Lump"/> child - a direct re-walk of
    /// the same marker/group-end logic <see cref="FindMapLumpGroups"/> uses
    /// (sharing the real scanning primitive, <see cref="FindGroupEnd"/>,
    /// not re-implementing it) rather than built from its own output, since
    /// a loose lump has no representation there at all. Deliberately NOT
    /// sorted, unlike <see cref="PathTreeBuilder"/>'s own output - a WAD's
    /// lump order is real file order, meaningful to preserve for browsing,
    /// not an arbitrary dictionary enumeration order.
    /// </summary>
    public ResourceTreeNode BuildTree(string displayName)
    {
        var root = new ResourceTreeNode { DisplayName = displayName, Kind = ResourceTreeNodeKind.WadContainer };
        var i = 0;

        while (i < Lumps.Count)
        {
            var isUdmf = i + 1 < Lumps.Count && Lumps[i + 1].Name.Equals("TEXTMAP", StringComparison.OrdinalIgnoreCase);
            var isClassic = !isUdmf && i + 1 < Lumps.Count && Lumps[i + 1].Name.Equals("THINGS", StringComparison.OrdinalIgnoreCase);

            if (!isUdmf && !isClassic)
            {
                root.Children.Add(new ResourceTreeNode { DisplayName = Lumps[i].Name, Kind = ResourceTreeNodeKind.Lump, Path = Lumps[i].Name, LumpIndex = i });
                i++;
                continue;
            }

            var markerName = Lumps[i].Name;
            var bodyStart = i + 1;
            var bodyEnd = isUdmf
                ? FindGroupEnd(Lumps, bodyStart, WadMapLumpNames.Udmf, stopAfterName: "ENDMAP")
                : FindGroupEnd(Lumps, bodyStart, WadMapLumpNames.Classic);

            var mapNode = new ResourceTreeNode { DisplayName = markerName, Kind = ResourceTreeNodeKind.MapGroup, Path = markerName, LumpIndex = i };
            for (var j = bodyStart; j < bodyEnd; j++)
            {
                mapNode.Children.Add(new ResourceTreeNode { DisplayName = Lumps[j].Name, Kind = ResourceTreeNodeKind.Lump, Path = Lumps[j].Name, LumpIndex = j });
            }

            root.Children.Add(mapNode);
            i = bodyEnd;
        }

        return root;
    }

    /// <summary>A WAD's own lumps aren't independently addressable files on disk at all - never a match.</summary>
    public bool ContainsFile(string absolutePath) => false;

    /// <summary>A WAD lump has no standalone on-disk path of its own to resolve to.</summary>
    public string? ResolveAbsolutePath(string relativePath) => null;

    /// <summary>
    /// Replaces the data of the lump at <paramref name="index"/>, leaving
    /// every other lump's name/position/bytes untouched - the one generic
    /// "write this single lump back" primitive, deliberately separate from
    /// <see cref="MapFileSaver"/>'s own splicing logic, which only ever
    /// operates on a map's own marker-to-end group, not a standalone named
    /// lump like <c>SCRIPTS</c>/<c>ZSCRIPT</c>. By index rather than name,
    /// since a WAD can have more than one lump sharing a name (a Hexen-
    /// format WAD's own per-map <c>SCRIPTS</c> lump, one per map) - matching
    /// by name alone can't tell those apart. Callers hand the result to
    /// <see cref="WadWriter.Write"/> for the actual on-disk rebuild, same as
    /// every other save path in this project already does - never an
    /// in-place binary patch.
    /// </summary>
    public static IReadOnlyList<WadLump> WithReplacedLumpData(IReadOnlyList<WadLump> lumps, int index, byte[] newData)
    {
        var result = new List<WadLump>(lumps);
        result[index] = new WadLump(lumps[index].Name, newData);
        return result;
    }
}
