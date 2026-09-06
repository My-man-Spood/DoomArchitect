using System.Numerics;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Tests.Geometry;

public class UniformGridSpatialIndexTests
{
    [Fact]
    public void QueryAlongRay_RayThroughASector_FindsThatSector()
    {
        var map = new MapData();
        var sector = BuildSquareSector(map, 0, 0, 256);
        var index = new UniformGridSpatialIndex();
        index.Rebuild(map);

        var result = index.QueryAlongRay(new Vector2(-1000, 128), new Vector2(1, 0));

        Assert.Contains(sector, result.Sectors);
    }

    [Fact]
    public void QueryAlongRay_RayThroughALinedef_FindsThatLinedef()
    {
        var map = new MapData();
        var sector = BuildSquareSector(map, 0, 0, 256);
        var linedef = sector.Sidedefs[0].Linedef;
        var index = new UniformGridSpatialIndex();
        index.Rebuild(map);

        var result = index.QueryAlongRay(new Vector2(-1000, 128), new Vector2(1, 0));

        Assert.Contains(linedef, result.Linedefs);
    }

    [Fact]
    public void QueryAlongRay_RayFarFromEverything_FindsNothing()
    {
        var map = new MapData();
        BuildSquareSector(map, 0, 0, 256);
        var index = new UniformGridSpatialIndex();
        index.Rebuild(map);

        // Aimed straight up, nowhere near the sector's XY footprint at all.
        var result = index.QueryAlongRay(new Vector2(-100000, -100000), new Vector2(0, -1));

        Assert.Empty(result.Sectors);
        Assert.Empty(result.Linedefs);
    }

    [Fact]
    public void QueryAlongRay_EmptyMap_FindsNothingAndDoesNotHang()
    {
        var map = new MapData();
        var index = new UniformGridSpatialIndex();
        index.Rebuild(map);

        var result = index.QueryAlongRay(Vector2.Zero, new Vector2(1, 0));

        Assert.Empty(result.Sectors);
        Assert.Empty(result.Linedefs);
    }

    [Fact]
    public void QueryAlongRay_AxisAlignedRayCrossingManyCells_StillFindsFarSector()
    {
        // Cell size is 256 - place a sector several cells away along a
        // perfectly axis-aligned ray, to exercise the zero-direction-
        // component path across many traversal steps.
        var map = new MapData();
        var sector = BuildSquareSector(map, 2000, 2000, 256);
        var index = new UniformGridSpatialIndex();
        index.Rebuild(map);

        var result = index.QueryAlongRay(new Vector2(0, 2100), new Vector2(1, 0));

        Assert.Contains(sector, result.Sectors);
    }

    [Fact]
    public void QueryAlongRay_OriginInsideBounds_StillFindsGeometryBehindAndAhead()
    {
        var map = new MapData();
        var sector = BuildSquareSector(map, 0, 0, 256);
        var index = new UniformGridSpatialIndex();
        index.Rebuild(map);

        // Origin sits inside the sector's own bounding box already.
        var result = index.QueryAlongRay(new Vector2(128, 128), new Vector2(1, 0));

        Assert.Contains(sector, result.Sectors);
    }

    private static Sector BuildSquareSector(MapData map, float x, float y, float size)
    {
        var sector = map.CreateSector(0, 128);
        var v0 = map.CreateVertex(new Vector2(x, y));
        var v1 = map.CreateVertex(new Vector2(x, y + size));
        var v2 = map.CreateVertex(new Vector2(x + size, y + size));
        var v3 = map.CreateVertex(new Vector2(x + size, y));

        map.CreateLinedef(v0, v1, sector, null);
        map.CreateLinedef(v1, v2, sector, null);
        map.CreateLinedef(v2, v3, sector, null);
        map.CreateLinedef(v3, v0, sector, null);

        return sector;
    }
}
