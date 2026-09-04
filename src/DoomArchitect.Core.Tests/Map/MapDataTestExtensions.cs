using System.Numerics;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Tests.Map;

internal static class MapDataTestExtensions
{
    /// <summary>
    /// Test-only convenience: creates a sector and wires up a closed loop
    /// of vertices/linedefs around it in one call, since a sector never
    /// owns its own boundary - that's exactly the seam being shortcut here.
    /// </summary>
    public static (Sector Sector, Vertex[] Vertices) CreateClosedSector(
        this MapData map,
        double floorHeight,
        double ceilingHeight,
        params Vector2[] boundary)
    {
        var sector = map.CreateSector(floorHeight, ceilingHeight);
        var vertices = boundary.Select(map.CreateVertex).ToArray();

        for (var i = 0; i < vertices.Length; i++)
        {
            var next = vertices[(i + 1) % vertices.Length];
            map.CreateLinedef(vertices[i], next, front: sector, back: null);
        }

        return (sector, vertices);
    }
}
