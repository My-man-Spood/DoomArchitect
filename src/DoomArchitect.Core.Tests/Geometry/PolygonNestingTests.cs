using System.Numerics;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Tests.Map;

namespace DoomArchitect.Core.Tests.Geometry;

public class PolygonNestingTests
{
    [Fact]
    public void BuildTree_SectorWithOneHole_NestsHoleUnderOuter()
    {
        var map = new MapData();
        var (sector, _) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 100), new Vector2(100, 100), new Vector2(100, 0));

        map.CreateClosedBoundary(sector,
            new Vector2(40, 40), new Vector2(60, 40), new Vector2(60, 60), new Vector2(40, 60));

        var loops = SectorTracer.Trace(sector);
        var tree = PolygonNesting.BuildTree(loops);

        var outer = Assert.Single(tree);
        Assert.True(outer.Loop.IsClockwise);

        var hole = Assert.Single(outer.Children);
        Assert.False(hole.Loop.IsClockwise);
    }

    [Fact]
    public void BuildTree_TwoDisjointLoops_AreBothRootsWithNoChildren()
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

        var loops = SectorTracer.Trace(sector);
        var tree = PolygonNesting.BuildTree(loops);

        Assert.Equal(2, tree.Count);
        Assert.All(tree, node => Assert.Empty(node.Children));
    }

    [Fact]
    public void BuildTree_IslandInsideAHole_NestsThreeLevelsDeep()
    {
        var map = new MapData();
        var (sector, _) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 100), new Vector2(100, 100), new Vector2(100, 0));

        map.CreateClosedBoundary(sector,
            new Vector2(20, 20), new Vector2(40, 20), new Vector2(40, 40), new Vector2(20, 40));

        map.CreateClosedBoundary(sector,
            new Vector2(25, 25), new Vector2(25, 35), new Vector2(35, 35), new Vector2(35, 25));

        var loops = SectorTracer.Trace(sector);
        var tree = PolygonNesting.BuildTree(loops);

        var outer = Assert.Single(tree);
        Assert.True(outer.Loop.IsClockwise);

        var hole = Assert.Single(outer.Children);
        Assert.False(hole.Loop.IsClockwise);

        var island = Assert.Single(hole.Children);
        Assert.True(island.Loop.IsClockwise);
    }
}
