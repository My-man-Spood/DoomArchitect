using System.Numerics;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Undo;

namespace DoomArchitect.Core.Tests.Undo;

public class DeleteThingsCommandTests
{
    [Fact]
    public void Do_RemovesTheSelectedThing()
    {
        var map = new MapData();
        var thing = map.CreateThing(new Vector2(0, 0), 1);

        new DeleteThingsCommand(map, new[] { thing }).Do();

        Assert.DoesNotContain(thing, map.Things);
    }

    [Fact]
    public void Do_MultipleThings_AllRemovedAsOneUndoStep()
    {
        var map = new MapData();
        var a = map.CreateThing(new Vector2(0, 0), 1);
        var b = map.CreateThing(new Vector2(10, 0), 2);

        var command = new DeleteThingsCommand(map, new[] { a, b });
        command.Do();

        Assert.Empty(map.Things);

        command.Undo();

        Assert.Equal(2, map.Things.Count);
        Assert.Contains(a, map.Things);
        Assert.Contains(b, map.Things);
    }

    [Fact]
    public void Undo_RestoresTheThing()
    {
        var map = new MapData();
        var thing = map.CreateThing(new Vector2(0, 0), 1);

        var command = new DeleteThingsCommand(map, new[] { thing });
        command.Do();
        command.Undo();

        Assert.Contains(thing, map.Things);
    }
}
