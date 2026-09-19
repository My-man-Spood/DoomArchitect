using System.Numerics;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Undo;

namespace DoomArchitect.Core.Tests.Undo;

public class CreateThingCommandTests
{
    [Fact]
    public void Do_CreatesAThingAtThePositionWithRealUdbDefaults()
    {
        var map = new MapData();

        var command = new CreateThingCommand(map, new Vector2(64, 128));
        command.Do();

        var thing = Assert.Single(map.Things);
        Assert.Same(thing, command.CreatedThing);
        Assert.Equal(new Vector2(64, 128), thing.Position);
        Assert.Equal(CreateThingCommand.DefaultType, thing.Type);
        Assert.Equal(CreateThingCommand.DefaultAngle, thing.Angle);
        Assert.Equal(CreateThingCommand.DefaultRawFlags, thing.RawFlags);
    }

    [Fact]
    public void Do_WithAnExplicitType_UsesThatTypeInsteadOfTheDefault()
    {
        var map = new MapData();

        var command = new CreateThingCommand(map, new Vector2(64, 128), type: 3001); // Imp
        command.Do();

        Assert.Equal(3001, command.CreatedThing.Type);
    }

    [Fact]
    public void Undo_RemovesTheCreatedThing()
    {
        var map = new MapData();
        var command = new CreateThingCommand(map, new Vector2(64, 128));
        command.Do();

        command.Undo();

        Assert.Empty(map.Things);
    }

    [Fact]
    public void Redo_AfterUndo_CreatesAnEquivalentThingAgain()
    {
        var map = new MapData();
        var command = new CreateThingCommand(map, new Vector2(64, 128));
        command.Do();
        command.Undo();

        command.Do();

        var thing = Assert.Single(map.Things);
        Assert.Equal(new Vector2(64, 128), thing.Position);
        Assert.Equal(CreateThingCommand.DefaultType, thing.Type);
    }
}
