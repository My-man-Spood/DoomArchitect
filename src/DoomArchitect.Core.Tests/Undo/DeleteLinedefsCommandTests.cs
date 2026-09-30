using System.Linq;
using System.Numerics;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Tests.Map;
using DoomArchitect.Core.Undo;

namespace DoomArchitect.Core.Tests.Undo;

public class DeleteLinedefsCommandTests
{
    [Fact]
    public void Do_RemovesTheLinedefButLeavesItsVerticesAlone()
    {
        var map = new MapData();
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(10, 0));
        var line = map.CreateLinedef(a, b, null, null);

        new DeleteLinedefsCommand(map, new[] { line }).Do();

        Assert.DoesNotContain(line, map.Linedefs);
        Assert.Contains(a, map.Vertices);
        Assert.Contains(b, map.Vertices);
        Assert.Empty(a.Linedefs);
        Assert.Empty(b.Linedefs);
    }

    [Fact]
    public void Do_RemovesTheWallsSidedefFromItsSector_AndMarksTheSectorDirty()
    {
        var map = new MapData();
        var (sector, vertices) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));
        var wall = map.Linedefs[0];
        map.ClearDirty(sector);

        new DeleteLinedefsCommand(map, new[] { wall }).Do();

        Assert.DoesNotContain(wall, sector.Sidedefs.Select(s => s.Linedef));
        Assert.True(sector.NeedsRebuild);
        Assert.Equal(4, vertices.Length); // untouched, only the wall itself is removed
    }

    [Fact]
    public void Do_MultipleLinedefs_AllRemovedAsOneUndoStep()
    {
        var map = new MapData();
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(10, 0));
        var c = map.CreateVertex(new Vector2(20, 0));
        var line1 = map.CreateLinedef(a, b, null, null);
        var line2 = map.CreateLinedef(b, c, null, null);

        var command = new DeleteLinedefsCommand(map, new[] { line1, line2 });
        command.Do();

        Assert.Empty(map.Linedefs);

        command.Undo();

        Assert.Equal(2, map.Linedefs.Count);
        Assert.Contains(line1, map.Linedefs);
        Assert.Contains(line2, map.Linedefs);
    }

    [Fact]
    public void Undo_RestoresTheLinedefAndItsSectorMembership()
    {
        var map = new MapData();
        var (sector, _) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));
        var wall = map.Linedefs[0];
        var originalSidedefCount = sector.Sidedefs.Count;

        var command = new DeleteLinedefsCommand(map, new[] { wall });
        command.Do();
        command.Undo();

        Assert.Contains(wall, map.Linedefs);
        Assert.Equal(originalSidedefCount, sector.Sidedefs.Count);
        Assert.Contains(wall, sector.Sidedefs.Select(s => s.Linedef));
    }
}
