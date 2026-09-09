using System.Numerics;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Tests.Map;

public class MapDataTests
{
    [Fact]
    public void NewSector_StartsDirty()
    {
        var map = new MapData();

        var sector = map.CreateSector(floorHeight: 0, ceilingHeight: 128);

        Assert.True(sector.NeedsRebuild);
    }

    [Fact]
    public void MovingVertex_DirtiesOnlySectorsTouchingIt()
    {
        var map = new MapData();

        var (touchedSector, touchedVertices) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));
        var (untouchedSector, _) = map.CreateClosedSector(0, 128,
            new Vector2(200, 200), new Vector2(200, 264), new Vector2(264, 264), new Vector2(264, 200));

        foreach (var sector in map.Sectors) map.ClearDirty(sector);

        map.MoveVertex(touchedVertices[0], new Vector2(-10, -10));

        Assert.True(touchedSector.NeedsRebuild);
        Assert.False(untouchedSector.NeedsRebuild);
    }

    [Fact]
    public void MovingVertex_DirtiesBothSidesOfATwoSidedLinedef()
    {
        var map = new MapData();

        var v1 = map.CreateVertex(new Vector2(0, 0));
        var v2 = map.CreateVertex(new Vector2(64, 0));

        var frontSector = map.CreateSector(floorHeight: 0, ceilingHeight: 128);
        var backSector = map.CreateSector(floorHeight: 0, ceilingHeight: 96);

        map.CreateLinedef(v1, v2, front: frontSector, back: backSector);

        foreach (var sector in map.Sectors) map.ClearDirty(sector);

        map.MoveVertex(v2, new Vector2(70, 5));

        Assert.True(frontSector.NeedsRebuild);
        Assert.True(backSector.NeedsRebuild);
    }

    [Fact]
    public void MovingVertex_UpdatesItsPosition()
    {
        var map = new MapData();
        var vertex = map.CreateVertex(new Vector2(0, 0));

        map.MoveVertex(vertex, new Vector2(12, 34));

        Assert.Equal(new Vector2(12, 34), vertex.Position);
    }

    [Fact]
    public void CreateLinedef_RegistersItselfOnBothEndpoints()
    {
        var map = new MapData();
        var v1 = map.CreateVertex(new Vector2(0, 0));
        var v2 = map.CreateVertex(new Vector2(64, 0));

        var linedef = map.CreateLinedef(v1, v2, front: map.CreateSector(0, 128), back: null);

        Assert.Contains(linedef, v1.Linedefs);
        Assert.Contains(linedef, v2.Linedefs);
    }

    [Fact]
    public void CreateThing_AddsToThings()
    {
        var map = new MapData();

        var thing = map.CreateThing(new Vector2(64, 128), type: 1);

        Assert.Contains(thing, map.Things);
        Assert.Equal(new Vector2(64, 128), thing.Position);
        Assert.Equal(1, thing.Type);
    }

    [Fact]
    public void MoveThing_UpdatesPositionAndMarksItDirty()
    {
        var map = new MapData();
        var thing = map.CreateThing(new Vector2(0, 0), type: 1);

        map.MoveThing(thing, new Vector2(12, 34));

        Assert.Equal(new Vector2(12, 34), thing.Position);
        Assert.True(thing.NeedsUpdate);
    }

    [Fact]
    public void GetDirtyThings_ReturnsOnlyThingsThatMoved()
    {
        var map = new MapData();
        var moved = map.CreateThing(new Vector2(0, 0), type: 1);
        var untouched = map.CreateThing(new Vector2(64, 64), type: 1);
        foreach (var thing in map.Things) map.ClearDirty(thing);

        map.MoveThing(moved, new Vector2(10, 10));

        Assert.Contains(moved, map.GetDirtyThings());
        Assert.DoesNotContain(untouched, map.GetDirtyThings());
    }

    [Fact]
    public void ClearDirty_Thing_RemovesItFromTheDirtySet()
    {
        var map = new MapData();
        var thing = map.CreateThing(new Vector2(0, 0), type: 1);
        map.MoveThing(thing, new Vector2(10, 10));

        map.ClearDirty(thing);

        Assert.DoesNotContain(thing, map.GetDirtyThings());
    }
}
