using System.Numerics;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Tests.Map;

namespace DoomArchitect.Core.Tests.Geometry;

public class MapErrorCheckerTests
{
    [Fact]
    public void CheckOverlappingVertices_TwoVerticesAtTheSamePosition_AreFlaggedTogether()
    {
        var map = new MapData();
        var a = map.CreateVertex(new Vector2(10, 20));
        var b = map.CreateVertex(new Vector2(10, 20));

        var result = Assert.Single(MapErrorChecker.CheckOverlappingVertices(map));

        Assert.Equal(new[] { a, b }, result.Vertices);
    }

    [Fact]
    public void CheckOverlappingVertices_NoSharedPosition_ReturnsEmpty()
    {
        var map = new MapData();
        map.CreateVertex(new Vector2(0, 0));
        map.CreateVertex(new Vector2(10, 10));

        Assert.Empty(MapErrorChecker.CheckOverlappingVertices(map));
    }

    [Fact]
    public void CheckStrayVertices_VertexWithNoLinedefs_IsFlagged()
    {
        var map = new MapData();
        var vertex = map.CreateVertex(new Vector2(5, 5));

        var result = Assert.Single(MapErrorChecker.CheckStrayVertices(map));

        Assert.Equal(new[] { vertex }, result.Vertices);
    }

    [Fact]
    public void CheckStrayVertices_VertexTouchingARealLinedef_IsNotFlagged()
    {
        var map = new MapData();
        var (_, boxVertices) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));

        Assert.DoesNotContain(MapErrorChecker.CheckStrayVertices(map), r => r.Vertices.Any(v => boxVertices.Contains(v)));
    }

    [Fact]
    public void CheckLinedefSides_LinedefWithNoSidedefsAtAll_IsFlagged()
    {
        var map = new MapData();
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(10, 0));
        var linedef = map.CreateLinedef(a, b, front: null, back: null);

        var result = Assert.Single(MapErrorChecker.CheckLinedefSides(map));

        Assert.Equal(new[] { linedef }, result.Linedefs);
    }

    [Fact]
    public void CheckLinedefSides_BackOnlyLinedef_IsFlagged()
    {
        var map = new MapData();
        var sector = map.CreateSector(0, 128);
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(10, 0));
        var linedef = map.CreateLinedef(a, b, front: null, back: sector);

        var result = Assert.Single(MapErrorChecker.CheckLinedefSides(map));

        Assert.Equal(new[] { linedef }, result.Linedefs);
    }

    [Fact]
    public void CheckLinedefSides_SelfReferencingLinedef_IsFlagged()
    {
        var map = new MapData();
        var sector = map.CreateSector(0, 128);
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(10, 0));
        var linedef = map.CreateLinedef(a, b, front: sector, back: sector);

        var result = Assert.Single(MapErrorChecker.CheckLinedefSides(map));

        Assert.Equal(new[] { linedef }, result.Linedefs);
        Assert.Equal(new[] { sector }, result.Sectors);
    }

    [Fact]
    public void CheckLinedefSides_NormalTwoSidedLinedefBetweenDifferentSectors_IsNotFlagged()
    {
        var map = new MapData();
        var sectorA = map.CreateSector(0, 128);
        var sectorB = map.CreateSector(16, 128);
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(10, 0));
        map.CreateLinedef(a, b, front: sectorA, back: sectorB);

        Assert.Empty(MapErrorChecker.CheckLinedefSides(map));
    }

    [Fact]
    public void CheckUnclosedSectors_SectorWithADanglingExtraSidedef_IsFlagged()
    {
        var map = new MapData();
        var (sector, boxVertices) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));

        // A dangling stub off one corner, never forming a closed loop of
        // its own - the exact shape SectorTracer.Trace silently drops
        // rather than reporting on its own.
        var stubEnd = map.CreateVertex(new Vector2(100, 100));
        map.CreateLinedef(boxVertices[2], stubEnd, front: sector, back: null);

        var result = Assert.Single(MapErrorChecker.CheckUnclosedSectors(map));

        Assert.Equal(new[] { sector }, result.Sectors);
    }

    [Fact]
    public void CheckUnclosedSectors_CleanClosedSector_ReturnsEmpty()
    {
        var map = new MapData();
        map.CreateClosedSector(0, 128, new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));

        Assert.Empty(MapErrorChecker.CheckUnclosedSectors(map));
    }

    [Fact]
    public void CheckUnclosedSectors_SelfReferencingOnlySector_IsNotFlaggedAsUnclosed()
    {
        // A sector with nothing but a self-referencing linedef has no
        // real boundary to close at all - CheckLinedefSides' own
        // self-referencing check covers this case instead.
        var map = new MapData();
        var sector = map.CreateSector(0, 128);
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(10, 0));
        map.CreateLinedef(a, b, front: sector, back: sector);

        Assert.Empty(MapErrorChecker.CheckUnclosedSectors(map));
    }

    [Fact]
    public void RunAll_CleanMap_ReturnsEmpty()
    {
        var map = new MapData();
        map.CreateClosedSector(0, 128, new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));

        Assert.Empty(MapErrorChecker.RunAll(map));
    }
}
