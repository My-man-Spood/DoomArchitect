using System.Numerics;

namespace DoomArchitect.Core.Map;

/// <summary>
/// Aggregate root for a single map. Owns every Vertex/Linedef/Sector and is
/// the only way to mutate them, so the vertex-linedef-sector adjacency
/// graph and the dirty-tracking on Sector can never drift out of sync.
/// </summary>
public sealed class MapData
{
    private readonly List<Vertex> _vertices = new();
    private readonly List<Linedef> _linedefs = new();
    private readonly List<Sector> _sectors = new();

    public IReadOnlyList<Vertex> Vertices => _vertices;
    public IReadOnlyList<Linedef> Linedefs => _linedefs;
    public IReadOnlyList<Sector> Sectors => _sectors;

    public Vertex CreateVertex(Vector2 position)
    {
        var vertex = new Vertex(position);
        _vertices.Add(vertex);
        return vertex;
    }

    public Sector CreateSector(double floorHeight, double ceilingHeight)
    {
        var sector = new Sector(floorHeight, ceilingHeight);
        _sectors.Add(sector);
        return sector;
    }

    public Linedef CreateLinedef(Vertex start, Vertex end, Sector? front, Sector? back)
    {
        var linedef = new Linedef(start, end);

        if (front != null) linedef.Front = new Sidedef(front);
        if (back != null) linedef.Back = new Sidedef(back);

        start.AddLinedef(linedef);
        end.AddLinedef(linedef);
        _linedefs.Add(linedef);

        return linedef;
    }

    /// <summary>
    /// Moves a vertex, dirtying only the sectors of linedefs touching it -
    /// never the rest of the map.
    /// </summary>
    public void MoveVertex(Vertex vertex, Vector2 newPosition)
    {
        vertex.Position = newPosition;

        foreach (var linedef in vertex.Linedefs)
        {
            linedef.MarkAdjacentSectorsDirty();
        }
    }

    public IEnumerable<Sector> GetDirtySectors() => _sectors.Where(s => s.NeedsRebuild);

    public void ClearDirty(Sector sector) => sector.NeedsRebuild = false;
}
