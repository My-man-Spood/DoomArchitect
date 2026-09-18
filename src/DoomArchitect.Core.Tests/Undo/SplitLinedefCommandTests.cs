using System.Linq;
using System.Numerics;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Undo;

namespace DoomArchitect.Core.Tests.Undo;

public class SplitLinedefCommandTests
{
    [Fact]
    public void Do_SplitsTheLinedefAtTheGivenPosition()
    {
        var map = new MapData();
        var start = map.CreateVertex(new Vector2(0, 0));
        var end = map.CreateVertex(new Vector2(100, 0));
        var linedef = map.CreateLinedef(start, end, null, null);

        var command = new SplitLinedefCommand(map, linedef, new Vector2(40, 0));
        command.Do();

        Assert.Equal(3, map.Vertices.Count);
        Assert.Equal(2, map.Linedefs.Count);
        Assert.Equal(new Vector2(40, 0), linedef.End.Position);
        var newHalf = map.Linedefs.Single(l => l != linedef);
        Assert.Equal(linedef.End, newHalf.Start);
        Assert.Equal(end, newHalf.End);
        Assert.Same(command.CreatedVertex, linedef.End);
    }

    [Fact]
    public void Undo_FullyRestoresTheOriginalLinedef()
    {
        var map = new MapData();
        var start = map.CreateVertex(new Vector2(0, 0));
        var end = map.CreateVertex(new Vector2(100, 0));
        var linedef = map.CreateLinedef(start, end, null, null);

        var command = new SplitLinedefCommand(map, linedef, new Vector2(40, 0));
        command.Do();

        command.Undo();

        Assert.Equal(2, map.Vertices.Count);
        var remaining = Assert.Single(map.Linedefs);
        Assert.Same(linedef, remaining);
        Assert.Same(start, linedef.Start);
        Assert.Same(end, linedef.End);
        Assert.Equal(1, end.Linedefs.Count(l => l == linedef));
    }

    [Fact]
    public void Do_DuplicatesBothSidedefsOntoTheNewHalf()
    {
        var map = new MapData();
        var start = map.CreateVertex(new Vector2(0, 0));
        var end = map.CreateVertex(new Vector2(100, 0));
        var frontSector = map.CreateSector(0, 128);
        var backSector = map.CreateSector(0, 96);
        var linedef = map.CreateLinedef(start, end, frontSector, backSector);
        linedef.Front!.MiddleTexture = "FRONTTEX";
        linedef.Back!.MiddleTexture = "BACKTEX";

        var command = new SplitLinedefCommand(map, linedef, new Vector2(40, 0));
        command.Do();

        var newHalf = map.Linedefs.Single(l => l != linedef);
        Assert.Equal("FRONTTEX", newHalf.Front!.MiddleTexture);
        Assert.Equal("BACKTEX", newHalf.Back!.MiddleTexture);
        Assert.Same(frontSector, newHalf.Front!.Sector);
        Assert.Same(backSector, newHalf.Back!.Sector);
    }
}
