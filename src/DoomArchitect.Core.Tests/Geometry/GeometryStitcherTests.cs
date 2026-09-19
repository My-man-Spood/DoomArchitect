using System.Linq;
using System.Numerics;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Tests.Geometry;

public class GeometryStitcherTests
{
    [Fact]
    public void SplitAgainstExistingLines_NewSegmentCrossesAnExistingWall_SplitsAtTheCrossing()
    {
        var map = new MapData();
        var sector = map.CreateSector(0, 128);
        var wallStart = map.CreateVertex(new Vector2(0, 0));
        var wallEnd = map.CreateVertex(new Vector2(100, 0));
        var wall = map.CreateLinedef(wallStart, wallEnd, sector, null);
        var existingLines = new[] { wall };

        var segmentStart = map.CreateVertex(new Vector2(50, -30));
        var segmentEnd = map.CreateVertex(new Vector2(50, 30));
        var segment = map.CreateLinedef(segmentStart, segmentEnd, null, null);

        var newLinedefs = new List<Linedef> { segment };
        var newVertices = new List<Vertex>();
        var undoActions = new List<Action>();

        GeometryStitcher.SplitAgainstExistingLines(map, segment, existingLines, newLinedefs, newVertices, undoActions);

        Assert.Equal(2, newLinedefs.Count);
        Assert.Single(newVertices);
        var splitVertex = newVertices[0];
        Assert.Equal(new Vector2(50, 0), splitVertex.Position);
        Assert.Same(splitVertex, segment.End);
    }

    [Fact]
    public void SplitAgainstExistingLines_NoCrossing_LeavesTheSegmentAlone()
    {
        var map = new MapData();
        var sector = map.CreateSector(0, 128);
        var wallStart = map.CreateVertex(new Vector2(0, 0));
        var wallEnd = map.CreateVertex(new Vector2(100, 0));
        var wall = map.CreateLinedef(wallStart, wallEnd, sector, null);

        var segmentStart = map.CreateVertex(new Vector2(200, -30));
        var segmentEnd = map.CreateVertex(new Vector2(200, 30));
        var segment = map.CreateLinedef(segmentStart, segmentEnd, null, null);

        var newLinedefs = new List<Linedef> { segment };
        var undoActions = new List<Action>();

        GeometryStitcher.SplitAgainstExistingLines(map, segment, new[] { wall }, newLinedefs, new List<Vertex>(), undoActions);

        Assert.Single(newLinedefs);
    }

    [Fact]
    public void JoinVerticesOntoExisting_MergesANearbyMovingVertexOntoTheFixedOne()
    {
        var map = new MapData();
        var existing = map.CreateVertex(new Vector2(50, 50));
        var moving = map.CreateVertex(new Vector2(50.001f, 50));
        var other = map.CreateVertex(new Vector2(0, 0));
        var line = map.CreateLinedef(other, moving, null, null);

        var movingList = new List<Vertex> { moving };
        var undoActions = new List<Action>();

        GeometryStitcher.JoinVerticesOntoExisting(map, new[] { existing }, movingList, GeometryStitcher.StitchDistance, undoActions);

        Assert.Empty(movingList);
        Assert.Same(existing, line.End);
        Assert.DoesNotContain(moving, map.Vertices);
    }

    [Fact]
    public void JoinVerticesWithinSet_MergesTwoCoincidentNewVertices()
    {
        var map = new MapData();
        var v1 = map.CreateVertex(new Vector2(10, 10));
        var v2 = map.CreateVertex(new Vector2(10.001f, 10));
        var other = map.CreateVertex(new Vector2(0, 0));
        var line = map.CreateLinedef(other, v2, null, null);

        var vertices = new List<Vertex> { v1, v2 };
        var undoActions = new List<Action>();

        GeometryStitcher.JoinVerticesWithinSet(map, vertices, GeometryStitcher.StitchDistance, undoActions);

        Assert.Single(vertices);
        Assert.Same(v1, line.End);
    }

    [Fact]
    public void SplitLinesByVertices_ExistingLineWithANewVertexSittingOnIt_SplitsTheExistingLine()
    {
        var map = new MapData();
        var sector = map.CreateSector(0, 128);
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(100, 0));
        var wall = map.CreateLinedef(a, b, sector, null);
        var newVertex = map.CreateVertex(new Vector2(50, 0));

        var lines = new List<Linedef> { wall };
        var trackInto = new List<Linedef>();
        var undoActions = new List<Action>();

        GeometryStitcher.SplitLinesByVertices(map, lines, new[] { newVertex }, GeometryStitcher.StitchDistance, trackInto, undoActions);

        Assert.Equal(2, lines.Count);
        Assert.Single(trackInto);
        Assert.Same(newVertex, wall.End);
    }

    [Fact]
    public void SplitLinesByVertices_VertexAlreadyAnEndpoint_DoesNotSplit()
    {
        var map = new MapData();
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(100, 0));
        var line = map.CreateLinedef(a, b, null, null);

        var lines = new List<Linedef> { line };
        var undoActions = new List<Action>();

        GeometryStitcher.SplitLinesByVertices(map, lines, new[] { a, b }, GeometryStitcher.StitchDistance, lines, undoActions);

        Assert.Single(lines);
    }

    [Fact]
    public void RemoveLoopedLinedefs_RemovesAZeroLengthLine()
    {
        var map = new MapData();
        var a = map.CreateVertex(new Vector2(0, 0));
        var looped = map.CreateLinedef(a, a, null, null);
        var real = map.CreateVertex(new Vector2(50, 50));
        var realLine = map.CreateLinedef(a, real, null, null);

        var lines = new List<Linedef> { looped, realLine };
        var undoActions = new List<Action>();

        GeometryStitcher.RemoveLoopedLinedefs(map, lines, undoActions);

        Assert.Single(lines);
        Assert.Contains(realLine, lines);
        Assert.DoesNotContain(looped, map.Linedefs);
    }

    [Fact]
    public void JoinOverlappingLines_TwoLinesSharingBothEndpoints_MergeIntoOne()
    {
        var map = new MapData();
        var sector = map.CreateSector(0, 128);
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(100, 0));
        var l1 = map.CreateLinedef(a, b, sector, null);
        var l2 = map.CreateLinedef(a, b, null, null);

        var lines = new List<Linedef> { l1, l2 };
        var undoActions = new List<Action>();

        GeometryStitcher.JoinOverlappingLines(map, lines, undoActions);

        Assert.Single(lines);
        Assert.DoesNotContain(l2, map.Linedefs);
        Assert.Same(sector, l1.Front!.Sector);
    }

    [Fact]
    public void FlipBackwardLinedefs_BackOnlyLine_FlipsToHaveAFront()
    {
        var map = new MapData();
        var backSector = map.CreateSector(0, 128);
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(100, 0));
        var line = map.CreateLinedef(a, b, null, backSector);
        var undoActions = new List<Action>();

        GeometryStitcher.FlipBackwardLinedefs(new List<Linedef> { line }, undoActions);

        Assert.NotNull(line.Front);
        Assert.Null(line.Back);
        Assert.Same(backSector, line.Front!.Sector);
        Assert.Same(b, line.Start);
        Assert.Same(a, line.End);
    }
}
