using System.Linq;
using System.Numerics;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Undo;

namespace DoomArchitect.Core.Tests.Undo;

public class DissolveVerticesCommandTests
{
    [Fact]
    public void Do_SingleTwoLineVertex_RebuildsOneNewEdgeSpanningTheFarVertices()
    {
        var map = new MapData();
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(10, 0));
        var c = map.CreateVertex(new Vector2(20, 0));
        var ab = map.CreateLinedef(a, b, null, null);
        var bc = map.CreateLinedef(b, c, null, null);

        new DissolveVerticesCommand(map, new[] { b }).Do();

        Assert.DoesNotContain(b, map.Vertices);
        Assert.DoesNotContain(ab, map.Linedefs);
        Assert.DoesNotContain(bc, map.Linedefs);
        Assert.Equal(2, map.Vertices.Count);
        Assert.Single(map.Linedefs);

        var survivor = map.Linedefs[0];
        Assert.NotEqual(ab, survivor);
        Assert.NotEqual(bc, survivor);
        Assert.True((survivor.Start == a && survivor.End == c) || (survivor.Start == c && survivor.End == a));
    }

    [Fact]
    public void Do_ChainOfConsecutiveTwoLineVertices_CollapsesTheWholeChainIntoOneEdge()
    {
        var map = new MapData();
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(10, 0));
        var c = map.CreateVertex(new Vector2(20, 0));
        var d = map.CreateVertex(new Vector2(30, 0));
        var ab = map.CreateLinedef(a, b, null, null);
        var bc = map.CreateLinedef(b, c, null, null);
        var cd = map.CreateLinedef(c, d, null, null);

        new DissolveVerticesCommand(map, new[] { b, c }).Do();

        Assert.DoesNotContain(b, map.Vertices);
        Assert.DoesNotContain(c, map.Vertices);
        Assert.Contains(a, map.Vertices);
        Assert.Contains(d, map.Vertices);
        Assert.DoesNotContain(ab, map.Linedefs);
        Assert.DoesNotContain(bc, map.Linedefs);
        Assert.DoesNotContain(cd, map.Linedefs);
        Assert.Single(map.Linedefs);

        var survivor = map.Linedefs[0];
        Assert.True((survivor.Start == a && survivor.End == d) || (survivor.Start == d && survivor.End == a));
    }

    [Fact]
    public void Do_VertexWhoseFarEndsAreAlreadyDirectlyConnected_JoinsThatThirdLinesSectorsInsteadOfMerging()
    {
        var map = new MapData();
        var sectorA = map.CreateSector(0, 128);
        var sectorB = map.CreateSector(0, 128);

        var top = map.CreateVertex(new Vector2(0, 10));
        var left = map.CreateVertex(new Vector2(-5, 0));
        var right = map.CreateVertex(new Vector2(5, 0));

        var toLeft = map.CreateLinedef(top, left, null, null);
        var toRight = map.CreateLinedef(top, right, null, null);
        var closingLine = map.CreateLinedef(left, right, sectorA, sectorB);

        new DissolveVerticesCommand(map, new[] { top }).Do();

        Assert.DoesNotContain(top, map.Vertices);
        Assert.DoesNotContain(toLeft, map.Linedefs);
        Assert.DoesNotContain(toRight, map.Linedefs);
        // Not merged into a new edge - just removed, since the "smart merge"
        // was skipped in favor of joining the sectors on the third line instead.
        Assert.Contains(closingLine, map.Linedefs);
        Assert.Equal(sectorB, closingLine.Front!.Sector);
        Assert.Equal(sectorB, closingLine.Back!.Sector);
        Assert.DoesNotContain(sectorA, map.Sectors);
    }

    [Fact]
    public void Do_JunctionVertexWithThreeLinedefs_FallsBackToPlainCascadeRemoval()
    {
        var map = new MapData();
        var center = map.CreateVertex(new Vector2(0, 0));
        var north = map.CreateVertex(new Vector2(0, 10));
        var east = map.CreateVertex(new Vector2(10, 0));
        var south = map.CreateVertex(new Vector2(0, -10));
        var toNorth = map.CreateLinedef(center, north, null, null);
        var toEast = map.CreateLinedef(center, east, null, null);
        var toSouth = map.CreateLinedef(center, south, null, null);

        new DissolveVerticesCommand(map, new[] { center }).Do();

        Assert.DoesNotContain(center, map.Vertices);
        Assert.DoesNotContain(toNorth, map.Linedefs);
        Assert.DoesNotContain(toEast, map.Linedefs);
        Assert.DoesNotContain(toSouth, map.Linedefs);
    }

    [Fact]
    public void Undo_SingleTwoLineVertex_FullyRestoresTheOriginalTwoLines()
    {
        var map = new MapData();
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(10, 0));
        var c = map.CreateVertex(new Vector2(20, 0));
        var ab = map.CreateLinedef(a, b, null, null);
        var bc = map.CreateLinedef(b, c, null, null);

        var command = new DissolveVerticesCommand(map, new[] { b });
        command.Do();
        command.Undo();

        Assert.Contains(b, map.Vertices);
        Assert.Contains(ab, map.Linedefs);
        Assert.Contains(bc, map.Linedefs);
        Assert.Equal(b, ab.End);
        Assert.Equal(b, bc.Start);
        Assert.Equal(3, map.Vertices.Count);
        Assert.Equal(2, map.Linedefs.Count);
    }

    [Fact]
    public void Undo_ChainOfConsecutiveTwoLineVertices_FullyRestoresEveryOriginalVertexAndLine()
    {
        var map = new MapData();
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(10, 0));
        var c = map.CreateVertex(new Vector2(20, 0));
        var d = map.CreateVertex(new Vector2(30, 0));
        var ab = map.CreateLinedef(a, b, null, null);
        var bc = map.CreateLinedef(b, c, null, null);
        var cd = map.CreateLinedef(c, d, null, null);

        var command = new DissolveVerticesCommand(map, new[] { b, c });
        command.Do();
        command.Undo();

        Assert.Equal(4, map.Vertices.Count);
        Assert.Equal(3, map.Linedefs.Count);
        Assert.Contains(ab, map.Linedefs);
        Assert.Contains(bc, map.Linedefs);
        Assert.Contains(cd, map.Linedefs);
        Assert.Equal(b, ab.End);
        Assert.Equal(b, bc.Start);
        Assert.Equal(c, bc.End);
        Assert.Equal(c, cd.Start);
    }

    [Fact]
    public void Undo_TryJoinSectorsCase_RestoresBothSectorsAndTheThirdLine()
    {
        var map = new MapData();
        var sectorA = map.CreateSector(0, 128);
        var sectorB = map.CreateSector(0, 128);

        var top = map.CreateVertex(new Vector2(0, 10));
        var left = map.CreateVertex(new Vector2(-5, 0));
        var right = map.CreateVertex(new Vector2(5, 0));

        var toLeft = map.CreateLinedef(top, left, null, null);
        var toRight = map.CreateLinedef(top, right, null, null);
        var closingLine = map.CreateLinedef(left, right, sectorA, sectorB);

        var command = new DissolveVerticesCommand(map, new[] { top });
        command.Do();
        command.Undo();

        Assert.Contains(top, map.Vertices);
        Assert.Contains(toLeft, map.Linedefs);
        Assert.Contains(toRight, map.Linedefs);
        Assert.Contains(sectorA, map.Sectors);
        Assert.Equal(sectorA, closingLine.Front!.Sector);
        Assert.Equal(sectorB, closingLine.Back!.Sector);
    }
}
