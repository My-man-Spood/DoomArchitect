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

/// <summary>
/// Reads the classic WAD container format: a 12-byte header
/// (identification, lump count, directory offset) followed by a flat
/// directory of (position, size, 8-character name) entries, each
/// pointing at a run of raw bytes elsewhere in the file. A map's lumps
/// are identified purely by their position relative to a map marker lump
/// (e.g. "MAP01") - the format itself has no explicit grouping.
/// </summary>
public sealed class WadFile
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
    /// WADs) aren't supported yet - see TODO.md.
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
}
