using DoomArchitect.Core.Map;
using MapVector2 = System.Numerics.Vector2;

namespace DoomArchitect.Core.IO;

/// <summary>
/// Reads the classic (vanilla Doom-format) binary map lumps - a close
/// port of UDB's own <c>DoomMapSetIO</c>: same record layouts, same
/// "warn and drop" recovery for malformed references (verified against
/// the actual UDB source, not general Doom-format community knowledge -
/// notably the sidedef texture field order is Upper, Lower, Middle, not
/// the commonly-assumed Upper, Middle, Lower).
///
/// Hexen/ZDoom-format maps are rejected rather than misparsed, using the
/// same signal UDB itself relies on for format matching: a
/// <c>BEHAVIOR</c> lump alongside the map. That format has different
/// record layouts entirely (16-byte linedefs with 5 args instead of a
/// tag, thing flags/specials laid out differently) and isn't ported here.
///
/// <c>THINGS</c> uses vanilla Doom's 10-byte record
/// (<c>x, y, angle, type, flags</c>) - the 20-byte Hexen-format record
/// (which adds a tid/z/action/args) doesn't apply here, since Hexen/ZDoom
/// maps are already rejected above via the <c>BEHAVIOR</c> check.
/// </summary>
public static class ClassicMapReader
{
    private const int VertexRecordSize = 4;
    private const int SectorRecordSize = 26;
    private const int SidedefRecordSize = 30;
    private const int LinedefRecordSize = 14;
    private const int ThingRecordSize = 10;
    private const int NoSidedef = ushort.MaxValue;

    private static readonly string[] MapLumpNames =
    {
        "THINGS", "LINEDEFS", "SIDEDEFS", "VERTEXES", "SEGS", "SSECTORS",
        "NODES", "SECTORS", "REJECT", "BLOCKMAP", "BEHAVIOR", "SCRIPTS",
    };

    public static (MapData Map, IReadOnlyList<string> Warnings) Read(WadFile wad, string mapName)
    {
        var lumps = FindMapLumps(wad, mapName);

        if (lumps.Any(l => l.Name.Equals("BEHAVIOR", StringComparison.OrdinalIgnoreCase)))
        {
            throw new NotSupportedException(
                $"Map '{mapName}' is in Hexen/ZDoom format (has a BEHAVIOR lump) - only classic Doom-format binary maps are supported.");
        }

        var vertexesData = RequireLump(lumps, "VERTEXES", mapName).Data;
        var sectorsData = RequireLump(lumps, "SECTORS", mapName).Data;
        var sidedefsData = RequireLump(lumps, "SIDEDEFS", mapName).Data;
        var linedefsData = RequireLump(lumps, "LINEDEFS", mapName).Data;

        var warnings = new List<string>();
        var map = new MapData();

        var vertices = ReadVertices(map, vertexesData);
        var sectors = ReadSectors(map, sectorsData);
        ReadLinedefs(map, linedefsData, sidedefsData, vertices, sectors, warnings);

        var thingsLump = lumps.FirstOrDefault(l => l.Name.Equals("THINGS", StringComparison.OrdinalIgnoreCase));
        if (thingsLump != null) ReadThings(map, thingsLump.Data);

        return (map, warnings);
    }

    private static void ReadThings(MapData map, byte[] data)
    {
        var count = data.Length / ThingRecordSize;
        using var reader = new BinaryReader(new MemoryStream(data));

        for (var i = 0; i < count; i++)
        {
            var x = reader.ReadInt16();
            var y = reader.ReadInt16();
            var angle = reader.ReadInt16();
            var type = reader.ReadInt16();
            var flags = reader.ReadUInt16();

            var thing = map.CreateThing(new MapVector2(x, y), type);
            thing.Angle = angle;
            thing.RawFlags = flags;
        }
    }

    /// <summary>
    /// Every lump belonging to this map: a bounded scan forward from the
    /// marker, stopping at the first lump whose name isn't a recognized
    /// binary map lump - which in practice is always the next map's own
    /// marker, or the end of the WAD. Bounded on purpose: an unbounded
    /// search for e.g. "VERTEXES" across the whole WAD could pick up a
    /// later map's lump if this one were missing its own.
    /// </summary>
    private static List<WadLump> FindMapLumps(WadFile wad, string mapName)
    {
        var markerIndex = -1;
        for (var i = 0; i < wad.Lumps.Count; i++)
        {
            if (wad.Lumps[i].Name.Equals(mapName, StringComparison.OrdinalIgnoreCase))
            {
                markerIndex = i;
                break;
            }
        }

        if (markerIndex < 0) throw new KeyNotFoundException($"No map named '{mapName}' found in this WAD.");

        var lumps = new List<WadLump>();
        for (var i = markerIndex + 1; i < wad.Lumps.Count; i++)
        {
            var lump = wad.Lumps[i];
            if (!MapLumpNames.Contains(lump.Name, StringComparer.OrdinalIgnoreCase)) break;
            lumps.Add(lump);
        }

        return lumps;
    }

    private static WadLump RequireLump(List<WadLump> lumps, string name, string mapName)
    {
        var lump = lumps.FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (lump == null)
        {
            throw new NotSupportedException($"Map '{mapName}' has no {name} lump - not a valid classic binary-format map.");
        }

        return lump;
    }

    private static List<Vertex> ReadVertices(MapData map, byte[] data)
    {
        var count = data.Length / VertexRecordSize;
        var vertices = new List<Vertex>(count);
        using var reader = new BinaryReader(new MemoryStream(data));

        for (var i = 0; i < count; i++)
        {
            var x = reader.ReadInt16();
            var y = reader.ReadInt16();
            vertices.Add(map.CreateVertex(new MapVector2(x, y)));
        }

        return vertices;
    }

    private static List<Sector> ReadSectors(MapData map, byte[] data)
    {
        var count = data.Length / SectorRecordSize;
        var sectors = new List<Sector>(count);
        using var reader = new BinaryReader(new MemoryStream(data));

        for (var i = 0; i < count; i++)
        {
            var floorHeight = reader.ReadInt16();
            var ceilingHeight = reader.ReadInt16();
            var floorTexture = DoomBinaryNames.Read(reader.ReadBytes(8));
            var ceilingTexture = DoomBinaryNames.Read(reader.ReadBytes(8));
            var brightness = reader.ReadInt16();
            var special = reader.ReadUInt16();
            var tag = reader.ReadUInt16();

            var sector = map.CreateSector(floorHeight, ceilingHeight);
            sector.FloorTexture = floorTexture;
            sector.CeilingTexture = ceilingTexture;
            sector.Brightness = brightness;
            if (special != 0) sector.SetCustomField("special", (long)special);
            if (tag != 0) sector.SetCustomField("id", (long)tag);
            sectors.Add(sector);
        }

        return sectors;
    }

    private static void ReadLinedefs(
        MapData map, byte[] linedefsData, byte[] sidedefsData, List<Vertex> vertices, List<Sector> sectors, List<string> warnings)
    {
        var sidedefCount = sidedefsData.Length / SidedefRecordSize;
        var count = linedefsData.Length / LinedefRecordSize;
        using var reader = new BinaryReader(new MemoryStream(linedefsData));

        for (var i = 0; i < count; i++)
        {
            var v1Index = reader.ReadUInt16();
            var v2Index = reader.ReadUInt16();
            var flags = reader.ReadUInt16();
            var special = reader.ReadUInt16();
            var tag = reader.ReadUInt16();
            var s1Index = reader.ReadUInt16();
            var s2Index = reader.ReadUInt16();

            if (v1Index >= vertices.Count || v2Index >= vertices.Count)
            {
                warnings.Add($"Linedef {i} references one or more invalid vertices. Linedef has been removed.");
                continue;
            }

            var start = vertices[v1Index];
            var end = vertices[v2Index];

            if (ManhattanDistance(start.Position, end.Position) <= 0.0001f)
            {
                warnings.Add($"Linedef {i} is zero-length. Linedef has been removed.");
                continue;
            }

            var front = ResolveSidedef(s1Index, sidedefsData, sidedefCount, sectors, i, warnings);
            var back = ResolveSidedef(s2Index, sidedefsData, sidedefCount, sectors, i, warnings);

            var linedef = map.CreateLinedef(start, end, front?.Sector, back?.Sector);
            if (flags != 0) linedef.SetCustomField("flags", (long)flags);
            if (special != 0) linedef.SetCustomField("special", (long)special);
            if (tag != 0) linedef.SetCustomField("id", (long)tag);

            if (front.HasValue) ApplySidedefData(linedef.Front!, front.Value.Record);
            if (back.HasValue) ApplySidedefData(linedef.Back!, back.Value.Record);
        }
    }

    private readonly record struct SidedefRecord(
        int OffsetX, int OffsetY, string UpperTexture, string LowerTexture, string MiddleTexture, int SectorIndex);

    private readonly record struct ResolvedSidedef(Sector Sector, SidedefRecord Record);

    private static ResolvedSidedef? ResolveSidedef(
        int index, byte[] sidedefsData, int sidedefCount, List<Sector> sectors, int linedefIndex, List<string> warnings)
    {
        if (index == NoSidedef) return null;

        if (index >= sidedefCount)
        {
            warnings.Add($"Linedef {linedefIndex} references invalid sidedef {index}. Sidedef has been removed.");
            return null;
        }

        var record = ReadSidedefRecord(sidedefsData, index);

        if (record.SectorIndex >= sectors.Count)
        {
            warnings.Add($"Sidedef {index} references invalid sector {record.SectorIndex}. Sidedef has been removed.");
            return null;
        }

        return new ResolvedSidedef(sectors[record.SectorIndex], record);
    }

    /// <summary>
    /// Field order, confirmed against UDB's own read/write code rather
    /// than assumed: offsetx, offsety, UPPER texture, LOWER texture,
    /// MIDDLE texture, sector - upper-then-lower-then-middle, not the
    /// upper-then-middle-then-lower order a casual guess would produce.
    /// </summary>
    private static SidedefRecord ReadSidedefRecord(byte[] data, int index)
    {
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);
        stream.Seek((long)index * SidedefRecordSize, SeekOrigin.Begin);

        var offsetX = reader.ReadInt16();
        var offsetY = reader.ReadInt16();
        var upper = DoomBinaryNames.Read(reader.ReadBytes(8));
        var lower = DoomBinaryNames.Read(reader.ReadBytes(8));
        var middle = DoomBinaryNames.Read(reader.ReadBytes(8));
        var sectorIndex = reader.ReadUInt16();

        return new SidedefRecord(offsetX, offsetY, upper, lower, middle, sectorIndex);
    }

    private static void ApplySidedefData(Sidedef sidedef, SidedefRecord record)
    {
        sidedef.OffsetX = record.OffsetX;
        sidedef.OffsetY = record.OffsetY;
        sidedef.UpperTexture = record.UpperTexture;
        sidedef.LowerTexture = record.LowerTexture;
        sidedef.MiddleTexture = record.MiddleTexture;
    }

    private static float ManhattanDistance(MapVector2 a, MapVector2 b) =>
        MathF.Abs(a.X - b.X) + MathF.Abs(a.Y - b.Y);
}
