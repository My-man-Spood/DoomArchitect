using System.Numerics;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Undo;

namespace DoomArchitect.Core.Tests.Undo;

public class DeleteVerticesCommandTests
{
    [Fact]
    public void Do_VertexWithTwoLinedefs_MergesThemIntoOneAndRemovesTheOther()
    {
        var map = new MapData();
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(10, 0));
        var c = map.CreateVertex(new Vector2(20, 0));
        var line1 = map.CreateLinedef(a, b, null, null); // b.Linedefs[0]
        var line2 = map.CreateLinedef(b, c, null, null); // b.Linedefs[1] - discarded

        new DeleteVerticesCommand(map, new[] { b }).Do();

        Assert.DoesNotContain(b, map.Vertices);
        Assert.DoesNotContain(line2, map.Linedefs);
        Assert.Contains(line1, map.Linedefs);
        Assert.Equal(c, line1.End);
        Assert.Contains(line1, c.Linedefs);
        Assert.DoesNotContain(line1, b.Linedefs); // detached, even though b itself is gone
    }

    [Fact]
    public void Do_JunctionVertexWithThreeLinedefs_RemovesAllOfThem()
    {
        var map = new MapData();
        var center = map.CreateVertex(new Vector2(0, 0));
        var north = map.CreateVertex(new Vector2(0, 10));
        var east = map.CreateVertex(new Vector2(10, 0));
        var south = map.CreateVertex(new Vector2(0, -10));
        var toNorth = map.CreateLinedef(center, north, null, null);
        var toEast = map.CreateLinedef(center, east, null, null);
        var toSouth = map.CreateLinedef(center, south, null, null);

        new DeleteVerticesCommand(map, new[] { center }).Do();

        Assert.DoesNotContain(center, map.Vertices);
        Assert.DoesNotContain(toNorth, map.Linedefs);
        Assert.DoesNotContain(toEast, map.Linedefs);
        Assert.DoesNotContain(toSouth, map.Linedefs);
        Assert.Empty(north.Linedefs);
        Assert.Empty(east.Linedefs);
        Assert.Empty(south.Linedefs);
    }

    [Fact]
    public void Do_EndpointVertexWithOneLinedef_RemovesThatLinedefToo()
    {
        var map = new MapData();
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(10, 0));
        var line = map.CreateLinedef(a, b, null, null);

        new DeleteVerticesCommand(map, new[] { b }).Do();

        Assert.DoesNotContain(b, map.Vertices);
        Assert.DoesNotContain(line, map.Linedefs);
        Assert.Empty(a.Linedefs);
    }

    [Fact]
    public void Undo_TwoLinedefMergeCase_RestoresOriginalTopologyExactly()
    {
        var map = new MapData();
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(10, 0));
        var c = map.CreateVertex(new Vector2(20, 0));
        var line1 = map.CreateLinedef(a, b, null, null);
        var line2 = map.CreateLinedef(b, c, null, null);

        var command = new DeleteVerticesCommand(map, new[] { b });
        command.Do();
        command.Undo();

        Assert.Contains(b, map.Vertices);
        Assert.Contains(line1, map.Linedefs);
        Assert.Contains(line2, map.Linedefs);
        Assert.Equal(b, line1.End);
        Assert.Equal(b, line2.Start);
        Assert.Contains(line1, b.Linedefs);
        Assert.Contains(line2, b.Linedefs);
        Assert.DoesNotContain(line1, c.Linedefs);
    }

    [Fact]
    public void Undo_JunctionCase_RestoresVertexAndEveryLinedef()
    {
        var map = new MapData();
        var center = map.CreateVertex(new Vector2(0, 0));
        var north = map.CreateVertex(new Vector2(0, 10));
        var east = map.CreateVertex(new Vector2(10, 0));
        var toNorth = map.CreateLinedef(center, north, null, null);
        var toEast = map.CreateLinedef(center, east, null, null);

        var command = new DeleteVerticesCommand(map, new[] { center });
        command.Do();
        command.Undo();

        Assert.Contains(center, map.Vertices);
        Assert.Contains(toNorth, map.Linedefs);
        Assert.Contains(toEast, map.Linedefs);
        Assert.Contains(toNorth, north.Linedefs);
        Assert.Contains(toEast, east.Linedefs);
        Assert.Contains(toNorth, center.Linedefs);
        Assert.Contains(toEast, center.Linedefs);
    }

    [Fact]
    public void Do_MultipleVertices_OneUndoStepDeletesAllOfThem()
    {
        var map = new MapData();
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(10, 0));
        var c = map.CreateVertex(new Vector2(20, 0));
        map.CreateLinedef(a, b, null, null);
        map.CreateLinedef(b, c, null, null);

        var command = new DeleteVerticesCommand(map, new[] { a, b, c });
        command.Do();

        Assert.Empty(map.Vertices);
        Assert.Empty(map.Linedefs);

        command.Undo();

        Assert.Equal(3, map.Vertices.Count);
        Assert.Equal(2, map.Linedefs.Count);
    }
}
