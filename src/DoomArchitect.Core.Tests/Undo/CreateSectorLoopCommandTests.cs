using System.Linq;
using System.Numerics;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Tests.Map;
using DoomArchitect.Core.Undo;

namespace DoomArchitect.Core.Tests.Undo;

public class CreateSectorLoopCommandTests
{
    private static readonly Vector2[] ClockwiseSquare =
    {
        new(0, 0), new(0, 64), new(64, 64), new(64, 0),
    };

    private static readonly Vector2[] CounterClockwiseSquare =
    {
        new(0, 0), new(64, 0), new(64, 64), new(0, 64),
    };

    [Fact]
    public void Do_ClockwiseLoop_EveryLinedefFrontIsTheNewSectorAndBackIsNull()
    {
        var map = new MapData();
        var command = new CreateSectorLoopCommand(map, ClockwiseSquare);

        command.Do();

        var sector = Assert.Single(map.Sectors);
        Assert.Equal(4, sector.Sidedefs.Count);
        Assert.All(map.Linedefs, l =>
        {
            Assert.Same(sector, l.Front?.Sector);
            Assert.Null(l.Back);
        });
    }

    [Fact]
    public void Do_CounterClockwiseLoop_EveryLinedefBackIsTheNewSectorAndFrontIsNull()
    {
        var map = new MapData();
        var command = new CreateSectorLoopCommand(map, CounterClockwiseSquare);

        command.Do();

        var sector = Assert.Single(map.Sectors);
        Assert.All(map.Linedefs, l =>
        {
            Assert.Same(sector, l.Back?.Sector);
            Assert.Null(l.Front);
        });
    }

    [Fact]
    public void Do_ProducesASectorThatTracesBackToTheSameFourCorners()
    {
        var map = new MapData();
        var command = new CreateSectorLoopCommand(map, ClockwiseSquare);

        command.Do();

        var sector = Assert.Single(map.Sectors);
        var loop = Assert.Single(SectorTracer.Trace(sector));
        Assert.Equal(4, loop.Vertices.Count);
        Assert.All(ClockwiseSquare, p => Assert.Contains(loop.Vertices, v => v.Position == p));
    }

    [Fact]
    public void Do_UsesTheGivenFloorAndCeilingHeights()
    {
        var map = new MapData();
        var command = new CreateSectorLoopCommand(map, ClockwiseSquare, floorHeight: 16, ceilingHeight: 96);

        command.Do();

        var sector = Assert.Single(map.Sectors);
        Assert.Equal(16, sector.FloorHeight);
        Assert.Equal(96, sector.CeilingHeight);
    }

    /// <summary>Matches UDB's own real defaults exactly (verified against its source), not <see cref="Sector"/>'s own plain class defaults.</summary>
    [Fact]
    public void Do_WithNoOverrides_UsesUdbsRealDefaultFloorCeilingAndBrightness()
    {
        var map = new MapData();
        var command = new CreateSectorLoopCommand(map, ClockwiseSquare);

        command.Do();

        var sector = Assert.Single(map.Sectors);
        Assert.Equal(CreateSectorLoopCommand.DefaultFloorTexture, sector.FloorTexture);
        Assert.Equal(CreateSectorLoopCommand.DefaultCeilingTexture, sector.CeilingTexture);
        Assert.Equal(CreateSectorLoopCommand.DefaultBrightness, sector.Brightness);
    }

    /// <summary>
    /// Every wall this command creates is a one-sided linedef's Middle -
    /// always a "required" sidedef part in real UDB, the one case that
    /// actually gets its default wall texture written rather than staying
    /// blank/"-".
    /// </summary>
    [Fact]
    public void Do_WithNoOverrides_EveryOneSidedWallGetsUdbsRealDefaultWallTexture()
    {
        var map = new MapData();
        var command = new CreateSectorLoopCommand(map, ClockwiseSquare);

        command.Do();

        Assert.All(map.Linedefs, l => Assert.Equal(CreateSectorLoopCommand.DefaultWallTexture, l.Front!.MiddleTexture));
    }

    [Fact]
    public void Do_GivenExplicitTextures_UsesThemInsteadOfTheDefaults()
    {
        var map = new MapData();
        var command = new CreateSectorLoopCommand(
            map, ClockwiseSquare, floorTexture: "MYFLOOR", ceilingTexture: "MYCEIL", wallTexture: "MYWALL");

        command.Do();

        var sector = Assert.Single(map.Sectors);
        Assert.Equal("MYFLOOR", sector.FloorTexture);
        Assert.Equal("MYCEIL", sector.CeilingTexture);
        Assert.All(map.Linedefs, l => Assert.Equal("MYWALL", l.Front!.MiddleTexture));
    }

    [Fact]
    public void Undo_RemovesEverySectorVertexAndLinedefItCreated()
    {
        var map = new MapData();
        var command = new CreateSectorLoopCommand(map, ClockwiseSquare);
        command.Do();

        command.Undo();

        Assert.Empty(map.Sectors);
        Assert.Empty(map.Vertices);
        Assert.Empty(map.Linedefs);
    }

    [Fact]
    public void Undo_LeavesUnrelatedExistingGeometryUntouched()
    {
        var map = new MapData();
        var (otherSector, otherVertices) = map.CreateClosedSector(0, 128,
            new Vector2(200, 200), new Vector2(200, 264), new Vector2(264, 264), new Vector2(264, 200));
        var command = new CreateSectorLoopCommand(map, ClockwiseSquare);
        command.Do();

        command.Undo();

        Assert.Contains(otherSector, map.Sectors);
        Assert.All(otherVertices, v => Assert.Contains(v, map.Vertices));
    }

    [Fact]
    public void Redo_AfterUndo_ProducesAnEquivalentSectorAgain()
    {
        var map = new MapData();
        var command = new CreateSectorLoopCommand(map, ClockwiseSquare);
        command.Do();
        command.Undo();

        command.Do();

        var sector = Assert.Single(map.Sectors);
        var loop = Assert.Single(SectorTracer.Trace(sector));
        Assert.Equal(4, loop.Vertices.Count);
        Assert.All(ClockwiseSquare, p => Assert.Contains(loop.Vertices, v => v.Position == p));
    }
}
