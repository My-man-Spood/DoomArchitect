using DoomArchitect.Core.Map;
using MapVector2 = System.Numerics.Vector2;

namespace DoomArchitect.Core.IO;

/// <summary>
/// Reads UDMF text into a <see cref="MapData"/>. A close port of UDB's
/// own <c>UniversalStreamReader</c> (UniversalStreamReader.cs:135-497):
/// same field defaults, same read order (vertices, then sectors, then
/// linedefs - which resolve sidedefs on demand, by indexing directly into
/// the raw parsed <c>sidedef</c> blocks, never pre-building sidedef
/// objects nothing references), and the same "log a warning and drop"
/// recovery for malformed references rather than aborting the whole load.
/// </summary>
public static class UdmfReader
{
    private static readonly string[] KnownBlockNames = { "namespace", "vertex", "sector", "linedef", "sidedef" };
    private static readonly HashSet<string> KnownVertexFields = new() { "x", "y" };
    private static readonly HashSet<string> KnownSectorFields =
        new() { "heightfloor", "heightceiling", "texturefloor", "textureceiling", "lightlevel" };
    private static readonly HashSet<string> KnownLinedefFields = new() { "v1", "v2", "sidefront", "sideback" };
    private static readonly HashSet<string> KnownSidedefFields =
        new() { "sector", "offsetx", "offsety", "texturetop", "texturebottom", "texturemiddle" };

    public static UdmfDocument Read(string text)
    {
        var warnings = new List<string>();
        var root = UdmfTreeParser.Parse(text, warnings);
        var @namespace = ReadNamespace(root, warnings);

        var map = new MapData();
        var vertices = ReadVertices(map, FindBlocks(root, "vertex"));
        var sectors = ReadSectors(map, FindBlocks(root, "sector"));
        ReadLinedefs(map, FindBlocks(root, "linedef"), vertices, sectors, FindBlocks(root, "sidedef"), warnings);

        var unknownBlocks = root.Blocks.Where(b => !KnownBlockNames.Contains(b.Name)).ToList();

        return new UdmfDocument(map, @namespace, unknownBlocks, warnings);
    }

    private static string ReadNamespace(UdmfBlock root, List<string> warnings)
    {
        var value = root.Find("namespace");
        if (value.HasValue) return value.Value.AsString();

        warnings.Add("Missing 'namespace' declaration; defaulting to 'doom'.");
        return "doom";
    }

    private static List<UdmfBlock> FindBlocks(UdmfBlock root, string name) =>
        root.Blocks.Where(b => b.Name == name).ToList();

    private static List<Vertex> ReadVertices(MapData map, List<UdmfBlock> blocks)
    {
        var vertices = new List<Vertex>(blocks.Count);

        foreach (var block in blocks)
        {
            var x = block.Find("x")?.AsDouble() ?? 0.0;
            var y = block.Find("y")?.AsDouble() ?? 0.0;

            var vertex = map.CreateVertex(new MapVector2((float)x, (float)y));
            ApplyCustomFields(vertex.SetCustomField, block, KnownVertexFields);
            vertices.Add(vertex);
        }

        return vertices;
    }

    private static List<Sector> ReadSectors(MapData map, List<UdmfBlock> blocks)
    {
        var sectors = new List<Sector>(blocks.Count);

        foreach (var block in blocks)
        {
            var floorHeight = block.Find("heightfloor")?.AsDouble() ?? 0.0;
            var ceilingHeight = block.Find("heightceiling")?.AsDouble() ?? 0.0;

            var sector = map.CreateSector(floorHeight, ceilingHeight);
            sector.FloorTexture = block.Find("texturefloor")?.AsString() ?? "-";
            sector.CeilingTexture = block.Find("textureceiling")?.AsString() ?? "-";
            sector.Brightness = block.Find("lightlevel")?.AsInt() ?? 160;

            ApplyCustomFields(sector.SetCustomField, block, KnownSectorFields);
            sectors.Add(sector);
        }

        return sectors;
    }

    private static void ReadLinedefs(
        MapData map,
        List<UdmfBlock> linedefBlocks,
        List<Vertex> vertices,
        List<Sector> sectors,
        List<UdmfBlock> sidedefBlocks,
        List<string> warnings)
    {
        for (var i = 0; i < linedefBlocks.Count; i++)
        {
            var block = linedefBlocks[i];
            var v1Index = block.Find("v1")?.AsInt() ?? 0;
            var v2Index = block.Find("v2")?.AsInt() ?? 0;

            if (v1Index < 0 || v1Index >= vertices.Count || v2Index < 0 || v2Index >= vertices.Count)
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

            var front = ResolveSidedef(block.Find("sidefront")?.AsInt() ?? -1, sidedefBlocks, sectors, i, "front", warnings);
            var back = ResolveSidedef(block.Find("sideback")?.AsInt() ?? -1, sidedefBlocks, sectors, i, "back", warnings);

            var linedef = map.CreateLinedef(start, end, front?.Sector, back?.Sector);
            ApplyCustomFields(linedef.SetCustomField, block, KnownLinedefFields);

            if (front.HasValue) ApplySidedefData(linedef.Front!, front.Value.Block);
            if (back.HasValue) ApplySidedefData(linedef.Back!, back.Value.Block);
        }
    }

    private readonly record struct ResolvedSidedef(Sector Sector, UdmfBlock Block);

    private static ResolvedSidedef? ResolveSidedef(
        int index, List<UdmfBlock> sidedefBlocks, List<Sector> sectors, int linedefIndex, string side, List<string> warnings)
    {
        if (index < 0) return null;

        if (index >= sidedefBlocks.Count)
        {
            warnings.Add($"Linedef {linedefIndex} references invalid {side} sidedef {index}. Sidedef has been removed.");
            return null;
        }

        var block = sidedefBlocks[index];
        var sectorIndex = block.Find("sector")?.AsInt() ?? 0;

        if (sectorIndex < 0 || sectorIndex >= sectors.Count)
        {
            warnings.Add($"Sidedef {index} references invalid sector {sectorIndex}. Sidedef has been removed.");
            return null;
        }

        return new ResolvedSidedef(sectors[sectorIndex], block);
    }

    private static void ApplySidedefData(Sidedef sidedef, UdmfBlock block)
    {
        sidedef.OffsetX = block.Find("offsetx")?.AsInt() ?? 0;
        sidedef.OffsetY = block.Find("offsety")?.AsInt() ?? 0;
        sidedef.UpperTexture = block.Find("texturetop")?.AsString() ?? "-";
        sidedef.LowerTexture = block.Find("texturebottom")?.AsString() ?? "-";
        sidedef.MiddleTexture = block.Find("texturemiddle")?.AsString() ?? "-";
        ApplyCustomFields(sidedef.SetCustomField, block, KnownSidedefFields);
    }

    private static void ApplyCustomFields(Action<string, object> setCustomField, UdmfBlock block, HashSet<string> knownFields)
    {
        foreach (var assignment in block.Assignments)
        {
            if (knownFields.Contains(assignment.Key)) continue;
            setCustomField(assignment.Key, assignment.Value.ToObject());
        }
    }

    private static float ManhattanDistance(MapVector2 a, MapVector2 b) =>
        MathF.Abs(a.X - b.X) + MathF.Abs(a.Y - b.Y);
}
