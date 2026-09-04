using System.Numerics;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Tests.Map;

namespace DoomArchitect.Core.Tests.Geometry;

public class PolygonCutterTests
{
    [Fact]
    public void Cut_SectorWithOneHole_MergesIntoOnePolygonWithAreaMinusTheHole()
    {
        var map = new MapData();
        var (sector, _) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 100), new Vector2(100, 100), new Vector2(100, 0));

        map.CreateClosedBoundary(sector,
            new Vector2(40, 40), new Vector2(60, 40), new Vector2(60, 60), new Vector2(40, 60));

        var tree = PolygonNesting.BuildTree(SectorTracer.Trace(sector));
        var polygons = PolygonCutter.Cut(tree);

        var merged = Assert.Single(polygons);
        Assert.Equal(9600f, Area(merged), 3);
    }

    [Fact]
    public void Cut_HoleAlignedWithAHorizontalOuterEdge_HandlesTheParallelRayCase()
    {
        // A "C"-shaped outer sector: a 100x100 box with a 50x20 notch cut
        // into its right side, so the notch's own edges are horizontal
        // but don't span the full width - leaving room for a hole vertex
        // to sit at the exact same height without touching the boundary.
        var map = new MapData();
        var (sector, _) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 100), new Vector2(100, 100), new Vector2(100, 60),
            new Vector2(50, 60), new Vector2(50, 40), new Vector2(100, 40), new Vector2(100, 0));

        // Rightmost vertex (30, 60) sits exactly on the notch edge's y=60
        // line, so casting a ray from it is parallel to that outer edge -
        // the case a normal line-intersection test can't resolve.
        map.CreateClosedBoundary(sector,
            new Vector2(10, 50), new Vector2(30, 60), new Vector2(10, 70));

        var tree = PolygonNesting.BuildTree(SectorTracer.Trace(sector));
        var merged = Assert.Single(PolygonCutter.Cut(tree));

        const float outerArea = 9000f; // 100x100 box minus a 50x20 notch
        const float holeArea = 200f; // triangle, base 20 x height 20 / 2
        Assert.Equal(outerArea - holeArea, Area(merged), 2);
    }

    [Fact]
    public void Cut_NoHoles_PassesLoopsThroughUnchanged()
    {
        var map = new MapData();
        var sector = map.CreateSector(0, 128);

        var shared = map.CreateVertex(new Vector2(0, 0));
        var aLeft = map.CreateVertex(new Vector2(-10, 0));
        var aTop = map.CreateVertex(new Vector2(0, 10));
        var bRight = map.CreateVertex(new Vector2(10, 0));
        var bBottom = map.CreateVertex(new Vector2(0, -10));

        map.CreateLinedef(shared, aLeft, front: sector, back: null);
        map.CreateLinedef(aLeft, aTop, front: sector, back: null);
        map.CreateLinedef(aTop, shared, front: sector, back: null);

        map.CreateLinedef(shared, bRight, front: sector, back: null);
        map.CreateLinedef(bRight, bBottom, front: sector, back: null);
        map.CreateLinedef(bBottom, shared, front: sector, back: null);

        var tree = PolygonNesting.BuildTree(SectorTracer.Trace(sector));
        var polygons = PolygonCutter.Cut(tree);

        Assert.Equal(2, polygons.Count);
        Assert.All(polygons, p => Assert.Equal(3, p.Count));
        Assert.All(polygons, p => Assert.Equal(50f, Area(p), 3));
    }

    [Fact]
    public void Cut_IslandInsideAHole_ProducesTwoIndependentPolygons()
    {
        var map = new MapData();
        var (sector, _) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 100), new Vector2(100, 100), new Vector2(100, 0));

        map.CreateClosedBoundary(sector,
            new Vector2(20, 20), new Vector2(40, 20), new Vector2(40, 40), new Vector2(20, 40));

        map.CreateClosedBoundary(sector,
            new Vector2(25, 25), new Vector2(25, 35), new Vector2(35, 35), new Vector2(35, 25));

        var tree = PolygonNesting.BuildTree(SectorTracer.Trace(sector));
        var polygons = PolygonCutter.Cut(tree);

        Assert.Equal(2, polygons.Count);

        // Ring (outer 100x100 minus the 20x20 hole) and the 10x10 island,
        // in whichever order the queue happened to process them.
        var areas = polygons.Select(Area).OrderBy(a => a).ToArray();
        Assert.Equal(100f, areas[0], 3);
        Assert.Equal(9600f, areas[1], 3);
    }

    private static float Area(IReadOnlyList<Vector2> polygon)
    {
        var sum = 0f;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }
        return MathF.Abs(sum) * 0.5f;
    }
}
