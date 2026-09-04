using System.Numerics;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Tests.Map;

internal static class MapDataTestExtensions
{
    /// <summary>
    /// Test-only convenience: creates a sector and wires up a closed loop
    /// of one-sided walls (Front = the sector, Back = null) around it in
    /// one call, since a sector never owns its own boundary - that's
    /// exactly the seam being shortcut here. Supply <paramref name="boundary"/>
    /// clockwise for a normal outer boundary - a one-sided wall's Front
    /// must be on the right walking Start to End, which is only true
    /// going clockwise around the outside of a shape.
    /// </summary>
    public static (Sector Sector, Vertex[] Vertices) CreateClosedSector(
        this MapData map,
        double floorHeight,
        double ceilingHeight,
        params Vector2[] boundary)
    {
        var sector = map.CreateSector(floorHeight, ceilingHeight);
        var vertices = map.CreateClosedBoundary(sector, boundary);
        return (sector, vertices);
    }

    /// <summary>
    /// Like <see cref="CreateClosedSector"/>, but attaches the loop to an
    /// existing sector instead of creating a new one - use this to add a
    /// hole to a sector that already has an outer boundary. Supply
    /// <paramref name="boundary"/> counter-clockwise for a hole.
    /// </summary>
    public static Vertex[] CreateClosedBoundary(this MapData map, Sector sector, params Vector2[] boundary)
    {
        var vertices = boundary.Select(map.CreateVertex).ToArray();

        for (var i = 0; i < vertices.Length; i++)
        {
            var next = vertices[(i + 1) % vertices.Length];
            map.CreateLinedef(vertices[i], next, front: sector, back: null);
        }

        return vertices;
    }
}
