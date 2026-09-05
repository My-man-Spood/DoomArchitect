using System.Text;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.IO;

/// <summary>
/// Writes a <see cref="UdmfDocument"/> back to UDMF text. A close port of
/// UDB's own <c>UniversalStreamWriter</c> (UniversalStreamWriter.cs:115-328):
/// same block order (namespace, unknown blocks, vertices, linedefs,
/// sidedefs, sectors) and the same per-block field-omission rules, which
/// are inconsistent by design across block types - sector always writes
/// its five core fields, sidedef omits offsets-when-zero and
/// textures-when-"-", linedef always writes <c>sidefront</c>/<c>sideback</c>
/// (as -1 when absent).
/// </summary>
public static class UdmfWriter
{
    public static string Write(UdmfDocument document)
    {
        var map = document.Map;
        var sb = new StringBuilder();

        UdmfTreeWriter.WriteAssignment(sb, 0, "namespace", document.Namespace);

        foreach (var block in document.UnknownBlocks)
        {
            UdmfTreeWriter.WriteBlock(sb, block, 0);
        }

        var vertexIndex = BuildIndex(map.Vertices);
        var sectorIndex = BuildIndex(map.Sectors);
        var (sidedefs, sidedefIndex) = BuildSidedefIndex(map.Linedefs);

        WriteVertices(sb, map.Vertices);
        WriteLinedefs(sb, map.Linedefs, vertexIndex, sidedefIndex);
        WriteSidedefs(sb, sidedefs, sectorIndex);
        WriteSectors(sb, map.Sectors);

        return sb.ToString();
    }

    private static Dictionary<T, int> BuildIndex<T>(IReadOnlyList<T> items) where T : notnull
    {
        var index = new Dictionary<T, int>();
        for (var i = 0; i < items.Count; i++) index[items[i]] = i;
        return index;
    }

    /// <summary>
    /// UDMF sidedefs are their own flat, index-referenced array, but
    /// <see cref="MapData"/> only reaches them via <c>Linedef.Front</c>/
    /// <c>Back</c>. The array is synthesized by walking linedefs in order
    /// and collecting front then back for each - the same order a fresh
    /// load of this exact output would reconstruct, so a load-then-save
    /// round-trip is stable.
    /// </summary>
    private static (List<Sidedef> Sidedefs, Dictionary<Sidedef, int> Index) BuildSidedefIndex(
        IReadOnlyList<Linedef> linedefs)
    {
        var sidedefs = new List<Sidedef>();
        var index = new Dictionary<Sidedef, int>();

        foreach (var linedef in linedefs)
        {
            if (linedef.Front != null)
            {
                index[linedef.Front] = sidedefs.Count;
                sidedefs.Add(linedef.Front);
            }

            if (linedef.Back != null)
            {
                index[linedef.Back] = sidedefs.Count;
                sidedefs.Add(linedef.Back);
            }
        }

        return (sidedefs, index);
    }

    private static void WriteVertices(StringBuilder sb, IReadOnlyList<Vertex> vertices)
    {
        foreach (var vertex in vertices)
        {
            BeginBlock(sb, "vertex");
            UdmfTreeWriter.WriteAssignment(sb, 1, "x", (double)vertex.Position.X);
            UdmfTreeWriter.WriteAssignment(sb, 1, "y", (double)vertex.Position.Y);
            WriteCustomFields(sb, vertex.CustomFields);
            EndBlock(sb);
        }
    }

    private static void WriteLinedefs(
        StringBuilder sb, IReadOnlyList<Linedef> linedefs, Dictionary<Vertex, int> vertexIndex, Dictionary<Sidedef, int> sidedefIndex)
    {
        foreach (var linedef in linedefs)
        {
            BeginBlock(sb, "linedef");
            UdmfTreeWriter.WriteAssignment(sb, 1, "v1", vertexIndex[linedef.Start]);
            UdmfTreeWriter.WriteAssignment(sb, 1, "v2", vertexIndex[linedef.End]);
            UdmfTreeWriter.WriteAssignment(sb, 1, "sidefront", linedef.Front != null ? sidedefIndex[linedef.Front] : -1);
            UdmfTreeWriter.WriteAssignment(sb, 1, "sideback", linedef.Back != null ? sidedefIndex[linedef.Back] : -1);
            WriteCustomFields(sb, linedef.CustomFields);
            EndBlock(sb);
        }
    }

    private static void WriteSidedefs(StringBuilder sb, IReadOnlyList<Sidedef> sidedefs, Dictionary<Sector, int> sectorIndex)
    {
        foreach (var sidedef in sidedefs)
        {
            BeginBlock(sb, "sidedef");
            UdmfTreeWriter.WriteAssignment(sb, 1, "sector", sectorIndex[sidedef.Sector]);
            if (sidedef.OffsetX != 0) UdmfTreeWriter.WriteAssignment(sb, 1, "offsetx", sidedef.OffsetX);
            if (sidedef.OffsetY != 0) UdmfTreeWriter.WriteAssignment(sb, 1, "offsety", sidedef.OffsetY);
            if (sidedef.UpperTexture != "-") UdmfTreeWriter.WriteAssignment(sb, 1, "texturetop", sidedef.UpperTexture);
            if (sidedef.LowerTexture != "-") UdmfTreeWriter.WriteAssignment(sb, 1, "texturebottom", sidedef.LowerTexture);
            if (sidedef.MiddleTexture != "-") UdmfTreeWriter.WriteAssignment(sb, 1, "texturemiddle", sidedef.MiddleTexture);
            WriteCustomFields(sb, sidedef.CustomFields);
            EndBlock(sb);
        }
    }

    private static void WriteSectors(StringBuilder sb, IReadOnlyList<Sector> sectors)
    {
        foreach (var sector in sectors)
        {
            BeginBlock(sb, "sector");
            UdmfTreeWriter.WriteAssignment(sb, 1, "heightfloor", (int)Math.Round(sector.FloorHeight));
            UdmfTreeWriter.WriteAssignment(sb, 1, "heightceiling", (int)Math.Round(sector.CeilingHeight));
            UdmfTreeWriter.WriteAssignment(sb, 1, "texturefloor", sector.FloorTexture);
            UdmfTreeWriter.WriteAssignment(sb, 1, "textureceiling", sector.CeilingTexture);
            UdmfTreeWriter.WriteAssignment(sb, 1, "lightlevel", sector.Brightness);
            WriteCustomFields(sb, sector.CustomFields);
            EndBlock(sb);
        }
    }

    private static void WriteCustomFields(StringBuilder sb, IReadOnlyDictionary<string, object> customFields)
    {
        foreach (var (key, value) in customFields)
        {
            UdmfTreeWriter.WriteAssignment(sb, 1, key, value);
        }
    }

    private static void BeginBlock(StringBuilder sb, string name) => sb.Append('\n').Append(name).Append("\n{\n");

    private static void EndBlock(StringBuilder sb) => sb.Append("}\n");
}
