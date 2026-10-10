using System.Numerics;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Undo;

namespace DoomArchitect.Core.Tests.Undo;

public class FlipLinedefsCommandTests
{
    [Fact]
    public void Do_TwoSidedLine_SwapsEndpointsAndSides()
    {
        var map = new MapData();
        var front = map.CreateSector(0, 128);
        var back = map.CreateSector(0, 96);
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(100, 0));
        var line = map.CreateLinedef(a, b, front, back);
        var originalFront = line.Front;
        var originalBack = line.Back;

        new FlipLinedefsCommand(new[] { line }).Do();

        Assert.Same(b, line.Start);
        Assert.Same(a, line.End);
        Assert.Same(originalBack, line.Front);
        Assert.Same(originalFront, line.Back);
    }

    [Fact]
    public void Undo_RestoresOriginalEndpointsAndSides()
    {
        var map = new MapData();
        var front = map.CreateSector(0, 128);
        var back = map.CreateSector(0, 96);
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(100, 0));
        var line = map.CreateLinedef(a, b, front, back);
        var originalFront = line.Front;
        var originalBack = line.Back;

        var command = new FlipLinedefsCommand(new[] { line });
        command.Do();
        command.Undo();

        Assert.Same(a, line.Start);
        Assert.Same(b, line.End);
        Assert.Same(originalFront, line.Front);
        Assert.Same(originalBack, line.Back);
    }
}
