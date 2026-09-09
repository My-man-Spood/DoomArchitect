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

    [Fact]
    public void SelectOnly_Vertex_SelectsOnlyThatOneAndDeselectsAnyOther()
    {
        var map = new MapData();
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(64, 0));
        map.SelectOnly(a);

        map.SelectOnly(b);

        Assert.False(a.IsSelected);
        Assert.True(b.IsSelected);
    }

    [Fact]
    public void ToggleSelect_Vertex_FlipsOnlyTheTargetedElement()
    {
        var map = new MapData();
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(64, 0));
        map.SelectOnly(a);

        map.ToggleSelect(b);

        Assert.True(a.IsSelected);
        Assert.True(b.IsSelected);

        map.ToggleSelect(b);

        Assert.True(a.IsSelected);
        Assert.False(b.IsSelected);
    }

    [Fact]
    public void ClearSelectedVertices_ClearsOnlyVertexSelection()
    {
        var map = new MapData();
        var vertex = map.CreateVertex(new Vector2(0, 0));
        var thing = map.CreateThing(new Vector2(0, 0), type: 1);
        map.SelectOnly(vertex);
        map.SelectOnly(thing);

        map.ClearSelectedVertices();

        Assert.False(vertex.IsSelected);
        Assert.True(thing.IsSelected);
    }

    [Fact]
    public void GetSelectedVertices_ReturnsExactlyTheSelectedSet()
    {
        var map = new MapData();
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(64, 0));
        map.SelectOnly(a);

        Assert.Equal(new[] { a }, map.GetSelectedVertices());
        Assert.DoesNotContain(b, map.GetSelectedVertices());
    }

    [Fact]
    public void SelectOnly_Linedef_SelectsOnlyThatOneAndDeselectsAnyOther()
    {
        var map = new MapData();
        var v1 = map.CreateVertex(new Vector2(0, 0));
        var v2 = map.CreateVertex(new Vector2(64, 0));
        var v3 = map.CreateVertex(new Vector2(64, 64));
        var sector = map.CreateSector(0, 128);
        var a = map.CreateLinedef(v1, v2, front: sector, back: null);
        var b = map.CreateLinedef(v2, v3, front: sector, back: null);
        map.SelectOnly(a);

        map.SelectOnly(b);

        Assert.False(a.IsSelected);
        Assert.True(b.IsSelected);
    }

    [Fact]
    public void ToggleSelect_Linedef_FlipsOnlyTheTargetedElement()
    {
        var map = new MapData();
        var v1 = map.CreateVertex(new Vector2(0, 0));
        var v2 = map.CreateVertex(new Vector2(64, 0));
        var sector = map.CreateSector(0, 128);
        var linedef = map.CreateLinedef(v1, v2, front: sector, back: null);

        map.ToggleSelect(linedef);
        Assert.True(linedef.IsSelected);

        map.ToggleSelect(linedef);
        Assert.False(linedef.IsSelected);
    }

    [Fact]
    public void ClearSelectedLinedefs_ClearsOnlyLinedefSelection()
    {
        var map = new MapData();
        var v1 = map.CreateVertex(new Vector2(0, 0));
        var v2 = map.CreateVertex(new Vector2(64, 0));
        var sector = map.CreateSector(0, 128);
        var linedef = map.CreateLinedef(v1, v2, front: sector, back: null);
        map.SelectOnly(linedef);
        map.SelectOnly(sector);

        map.ClearSelectedLinedefs();

        Assert.False(linedef.IsSelected);
        Assert.True(sector.IsSelected);
    }

    [Fact]
    public void GetSelectedLinedefs_ReturnsExactlyTheSelectedSet()
    {
        var map = new MapData();
        var v1 = map.CreateVertex(new Vector2(0, 0));
        var v2 = map.CreateVertex(new Vector2(64, 0));
        var v3 = map.CreateVertex(new Vector2(64, 64));
        var sector = map.CreateSector(0, 128);
        var a = map.CreateLinedef(v1, v2, front: sector, back: null);
        var b = map.CreateLinedef(v2, v3, front: sector, back: null);
        map.SelectOnly(a);

        Assert.Equal(new[] { a }, map.GetSelectedLinedefs());
        Assert.DoesNotContain(b, map.GetSelectedLinedefs());
    }

    [Fact]
    public void SelectOnly_Sector_SelectsOnlyThatOneAndDeselectsAnyOther()
    {
        var map = new MapData();
        var a = map.CreateSector(0, 128);
        var b = map.CreateSector(0, 96);
        map.SelectOnly(a);

        map.SelectOnly(b);

        Assert.False(a.IsSelected);
        Assert.True(b.IsSelected);
    }

    [Fact]
    public void ToggleSelect_Sector_FlipsOnlyTheTargetedElement()
    {
        var map = new MapData();
        var sector = map.CreateSector(0, 128);

        map.ToggleSelect(sector);
        Assert.True(sector.IsSelected);

        map.ToggleSelect(sector);
        Assert.False(sector.IsSelected);
    }

    [Fact]
    public void ClearSelectedSectors_ClearsOnlySectorSelection()
    {
        var map = new MapData();
        var sector = map.CreateSector(0, 128);
        var thing = map.CreateThing(new Vector2(0, 0), type: 1);
        map.SelectOnly(sector);
        map.SelectOnly(thing);

        map.ClearSelectedSectors();

        Assert.False(sector.IsSelected);
        Assert.True(thing.IsSelected);
    }

    [Fact]
    public void GetSelectedSectors_ReturnsExactlyTheSelectedSet()
    {
        var map = new MapData();
        var a = map.CreateSector(0, 128);
        var b = map.CreateSector(0, 96);
        map.SelectOnly(a);

        Assert.Equal(new[] { a }, map.GetSelectedSectors());
        Assert.DoesNotContain(b, map.GetSelectedSectors());
    }

    [Fact]
    public void SelectOnly_Thing_SelectsOnlyThatOneAndDeselectsAnyOther()
    {
        var map = new MapData();
        var a = map.CreateThing(new Vector2(0, 0), type: 1);
        var b = map.CreateThing(new Vector2(64, 0), type: 1);
        map.SelectOnly(a);

        map.SelectOnly(b);

        Assert.False(a.IsSelected);
        Assert.True(b.IsSelected);
    }

    [Fact]
    public void ToggleSelect_Thing_FlipsOnlyTheTargetedElement()
    {
        var map = new MapData();
        var thing = map.CreateThing(new Vector2(0, 0), type: 1);

        map.ToggleSelect(thing);
        Assert.True(thing.IsSelected);

        map.ToggleSelect(thing);
        Assert.False(thing.IsSelected);
    }

    [Fact]
    public void ClearSelectedThings_ClearsOnlyThingSelection()
    {
        var map = new MapData();
        var thing = map.CreateThing(new Vector2(0, 0), type: 1);
        var sector = map.CreateSector(0, 128);
        map.SelectOnly(thing);
        map.SelectOnly(sector);

        map.ClearSelectedThings();

        Assert.False(thing.IsSelected);
        Assert.True(sector.IsSelected);
    }

    [Fact]
    public void GetSelectedThings_ReturnsExactlyTheSelectedSet()
    {
        var map = new MapData();
        var a = map.CreateThing(new Vector2(0, 0), type: 1);
        var b = map.CreateThing(new Vector2(64, 0), type: 1);
        map.SelectOnly(a);

        Assert.Equal(new[] { a }, map.GetSelectedThings());
        Assert.DoesNotContain(b, map.GetSelectedThings());
    }

    [Fact]
    public void ConvertGeometrySelection_ToVertices_SelectsEndpointsOfSelectedLinedefs()
    {
        var map = new MapData();
        var (_, vertices) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));
        var linedef = vertices[0].Linedefs[0];
        map.SelectOnly(linedef);

        map.ConvertGeometrySelection(GeometrySelectionType.Vertices);

        Assert.True(linedef.Start.IsSelected);
        Assert.True(linedef.End.IsSelected);
    }

    [Fact]
    public void ConvertGeometrySelection_ToVertices_PreservesAlreadySelectedVertices()
    {
        var map = new MapData();
        var v1 = map.CreateVertex(new Vector2(0, 0));
        var v2 = map.CreateVertex(new Vector2(500, 500));
        map.SelectOnly(v2);
        var linedef = map.CreateLinedef(v1, map.CreateVertex(new Vector2(64, 0)), front: map.CreateSector(0, 128), back: null);
        map.ToggleSelect(linedef);

        map.ConvertGeometrySelection(GeometrySelectionType.Vertices);

        Assert.True(v2.IsSelected);
    }

    [Fact]
    public void ConvertGeometrySelection_ToVertices_SelectsVerticesTouchingASelectedSector()
    {
        var map = new MapData();
        var (sector, vertices) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));
        map.SelectOnly(sector);

        map.ConvertGeometrySelection(GeometrySelectionType.Vertices);

        Assert.All(vertices, v => Assert.True(v.IsSelected));
    }

    [Fact]
    public void ConvertGeometrySelection_ToVertices_ClearsLinedefAndSectorSelection()
    {
        var map = new MapData();
        var (sector, vertices) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));
        map.SelectOnly(sector);

        map.ConvertGeometrySelection(GeometrySelectionType.Vertices);

        Assert.Empty(map.GetSelectedSectors());
        Assert.Empty(map.GetSelectedLinedefs());
    }

    [Fact]
    public void ConvertGeometrySelection_ToLinedefs_SelectsALinedefOnlyWhenBothEndpointsAreSelected()
    {
        var map = new MapData();
        var (_, vertices) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));
        var boundLinedef = vertices[0].Linedefs.Single(l => l.Start == vertices[0] && l.End == vertices[1]);
        map.ToggleSelect(vertices[0]);
        map.ToggleSelect(vertices[1]);

        map.ConvertGeometrySelection(GeometrySelectionType.Linedefs);

        Assert.True(boundLinedef.IsSelected);
    }

    [Fact]
    public void ConvertGeometrySelection_ToLinedefs_DoesNotSelectALinedefWithOnlyOneEndpointSelected()
    {
        var map = new MapData();
        var (_, vertices) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));
        map.ToggleSelect(vertices[0]);

        map.ConvertGeometrySelection(GeometrySelectionType.Linedefs);

        Assert.Empty(map.GetSelectedLinedefs());
    }

    [Fact]
    public void ConvertGeometrySelection_ToLinedefs_SelectsEveryLinedefOfASelectedSector()
    {
        var map = new MapData();
        var (sector, _) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));
        map.SelectOnly(sector);

        map.ConvertGeometrySelection(GeometrySelectionType.Linedefs);

        Assert.All(sector.Sidedefs, sd => Assert.True(sd.Linedef.IsSelected));
    }

    [Fact]
    public void ConvertGeometrySelection_ToLinedefs_ClearsVertexAndSectorSelection()
    {
        var map = new MapData();
        var (sector, _) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));
        map.SelectOnly(sector);

        map.ConvertGeometrySelection(GeometrySelectionType.Linedefs);

        Assert.Empty(map.GetSelectedVertices());
        Assert.Empty(map.GetSelectedSectors());
    }

    [Fact]
    public void ConvertGeometrySelection_ToSectors_SelectsASectorWhenEveryBorderingLinedefIsSelected()
    {
        var map = new MapData();
        var (sector, _) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));
        foreach (var sidedef in sector.Sidedefs) map.ToggleSelect(sidedef.Linedef);

        map.ConvertGeometrySelection(GeometrySelectionType.Sectors);

        Assert.True(sector.IsSelected);
    }

    [Fact]
    public void ConvertGeometrySelection_ToSectors_DoesNotSelectASectorMissingOneBorderingLinedef()
    {
        var map = new MapData();
        var (sector, _) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));
        var linedefs = sector.Sidedefs.Select(sd => sd.Linedef).ToList();
        foreach (var linedef in linedefs.Take(linedefs.Count - 1)) map.ToggleSelect(linedef);

        map.ConvertGeometrySelection(GeometrySelectionType.Sectors);

        Assert.False(sector.IsSelected);
    }

    [Fact]
    public void ConvertGeometrySelection_ToSectors_HoleLoopMustAlsoBeFullySelected()
    {
        var map = new MapData();
        var (sector, _) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 256), new Vector2(256, 256), new Vector2(256, 0));
        map.CreateClosedBoundary(sector,
            new Vector2(96, 96), new Vector2(96, 160), new Vector2(160, 160), new Vector2(160, 96));
        var linedefs = sector.Sidedefs.Select(sd => sd.Linedef).ToList();

        foreach (var linedef in linedefs.Take(linedefs.Count - 1)) map.ToggleSelect(linedef);
        map.ConvertGeometrySelection(GeometrySelectionType.Sectors);
        Assert.False(sector.IsSelected);

        // The conversion above clears linedef selection regardless of
        // outcome, so every linedef needs re-selecting, not just the one
        // that was missing.
        foreach (var linedef in linedefs) map.ToggleSelect(linedef);
        map.ConvertGeometrySelection(GeometrySelectionType.Sectors);
        Assert.True(sector.IsSelected);
    }

    [Fact]
    public void ConvertGeometrySelection_ToSectors_PreservesAnAlreadySelectedSector()
    {
        var map = new MapData();
        var (sector, _) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));
        map.SelectOnly(sector);

        map.ConvertGeometrySelection(GeometrySelectionType.Sectors);

        Assert.True(sector.IsSelected);
    }

    [Fact]
    public void ConvertGeometrySelection_ToSectors_ClearsVertexSelectionAndRebuildsLinedefSelection()
    {
        var map = new MapData();
        var (sector, vertices) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));
        map.ToggleSelect(vertices[0]);
        foreach (var sidedef in sector.Sidedefs) map.ToggleSelect(sidedef.Linedef);

        map.ConvertGeometrySelection(GeometrySelectionType.Sectors);

        Assert.Empty(map.GetSelectedVertices());
        Assert.Equal(
            sector.Sidedefs.Select(sd => sd.Linedef).ToHashSet(),
            map.GetSelectedLinedefs().ToHashSet());
    }

    [Fact]
    public void ConvertGeometrySelection_NeverTouchesThingSelection()
    {
        var map = new MapData();
        var thing = map.CreateThing(new Vector2(0, 0), type: 1);
        map.SelectOnly(thing);

        map.ConvertGeometrySelection(GeometrySelectionType.Vertices);
        map.ConvertGeometrySelection(GeometrySelectionType.Linedefs);
        map.ConvertGeometrySelection(GeometrySelectionType.Sectors);

        Assert.True(thing.IsSelected);
    }

    [Fact]
    public void ToggleSelect_Sector_SelectingItSelectsItsBorderingLinedefs()
    {
        var map = new MapData();
        var (sector, _) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));

        map.ToggleSelect(sector);

        Assert.All(sector.Sidedefs, sd => Assert.True(sd.Linedef.IsSelected));
    }

    [Fact]
    public void ToggleSelect_Sector_DeselectingItClearsItsBorderingLinedefs()
    {
        var map = new MapData();
        var (sector, _) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));
        map.ToggleSelect(sector);

        map.ToggleSelect(sector);

        Assert.All(sector.Sidedefs, sd => Assert.False(sd.Linedef.IsSelected));
    }

    [Fact]
    public void ToggleSelect_Sector_SharedLinedefStaysSelectedWhileEitherBorderingSectorIsSelected()
    {
        var map = new MapData();
        var v00 = map.CreateVertex(new Vector2(0, 0));
        var v01 = map.CreateVertex(new Vector2(0, 64));
        var v11 = map.CreateVertex(new Vector2(64, 64));
        var v10 = map.CreateVertex(new Vector2(64, 0));
        var v21 = map.CreateVertex(new Vector2(128, 64));
        var v20 = map.CreateVertex(new Vector2(128, 0));

        var sectorA = map.CreateSector(0, 128);
        var sectorB = map.CreateSector(0, 128);

        map.CreateLinedef(v00, v01, front: sectorA, back: null);
        map.CreateLinedef(v01, v11, front: sectorA, back: null);
        var shared = map.CreateLinedef(v11, v10, front: sectorA, back: sectorB);
        map.CreateLinedef(v10, v00, front: sectorA, back: null);

        map.CreateLinedef(v11, v21, front: sectorB, back: null);
        map.CreateLinedef(v21, v20, front: sectorB, back: null);
        map.CreateLinedef(v20, v10, front: sectorB, back: null);

        map.ToggleSelect(sectorA);
        map.ToggleSelect(sectorB);
        Assert.True(shared.IsSelected);

        // Deselecting just one side must leave the shared edge selected,
        // since the other bordering sector is still selected.
        map.ToggleSelect(sectorA);
        Assert.True(shared.IsSelected);

        map.ToggleSelect(sectorB);
        Assert.False(shared.IsSelected);
    }

    [Fact]
    public void SelectOnly_Sector_ResyncsBorderingLinedefsAndClearsThePreviousSectorsBoundary()
    {
        var map = new MapData();
        var (sectorA, _) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));
        var (sectorB, _) = map.CreateClosedSector(0, 128,
            new Vector2(200, 0), new Vector2(200, 64), new Vector2(264, 64), new Vector2(264, 0));
        map.ToggleSelect(sectorA);

        map.SelectOnly(sectorB);

        Assert.All(sectorA.Sidedefs, sd => Assert.False(sd.Linedef.IsSelected));
        Assert.All(sectorB.Sidedefs, sd => Assert.True(sd.Linedef.IsSelected));
    }

    [Fact]
    public void ConvertGeometrySelection_ToSectors_ThenToggleSectorOff_ClearsStaleBorderLinedefSelection()
    {
        // Regression test for a real reported bug: selecting linedefs
        // forming a sector, switching to Sectors mode, then toggling the
        // sector off left its border linedefs stuck showing as selected -
        // because nothing resynced them until this fix.
        var map = new MapData();
        var (sector, _) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));
        foreach (var sidedef in sector.Sidedefs) map.ToggleSelect(sidedef.Linedef);
        map.ConvertGeometrySelection(GeometrySelectionType.Sectors);

        map.ToggleSelect(sector);

        Assert.False(sector.IsSelected);
        Assert.All(sector.Sidedefs, sd => Assert.False(sd.Linedef.IsSelected));
    }

    [Fact]
    public void MarqueeSelectVertices_SelectMode_ReplacesSelectionWithRectangleContents()
    {
        var map = new MapData();
        var inside = map.CreateVertex(new Vector2(10, 10));
        var outside = map.CreateVertex(new Vector2(100, 100));
        map.SelectOnly(outside);

        map.MarqueeSelectVertices(new Vector2(0, 0), new Vector2(20, 20), MarqueeSelectionMode.Select);

        Assert.True(inside.IsSelected);
        Assert.False(outside.IsSelected);
    }

    [Fact]
    public void MarqueeSelectVertices_AddMode_UnionsWithoutClearingPriorSelection()
    {
        var map = new MapData();
        var inside = map.CreateVertex(new Vector2(10, 10));
        var alreadySelected = map.CreateVertex(new Vector2(100, 100));
        map.SelectOnly(alreadySelected);

        map.MarqueeSelectVertices(new Vector2(0, 0), new Vector2(20, 20), MarqueeSelectionMode.Add);

        Assert.True(inside.IsSelected);
        Assert.True(alreadySelected.IsSelected);
    }

    [Fact]
    public void MarqueeSelectVertices_SubtractMode_RemovesOnlyRectangleContents()
    {
        var map = new MapData();
        var inside = map.CreateVertex(new Vector2(10, 10));
        var outside = map.CreateVertex(new Vector2(100, 100));
        map.SelectOnly(inside);
        map.ToggleSelect(outside);

        map.MarqueeSelectVertices(new Vector2(0, 0), new Vector2(20, 20), MarqueeSelectionMode.Subtract);

        Assert.False(inside.IsSelected);
        Assert.True(outside.IsSelected);
    }

    [Fact]
    public void MarqueeSelectVertices_IntersectMode_KeepsOnlySelectedAndInsideRectangle()
    {
        var map = new MapData();
        var insideSelected = map.CreateVertex(new Vector2(10, 10));
        var insideUnselected = map.CreateVertex(new Vector2(11, 11));
        var outsideSelected = map.CreateVertex(new Vector2(100, 100));
        map.SelectOnly(insideSelected);
        map.ToggleSelect(outsideSelected);

        map.MarqueeSelectVertices(new Vector2(0, 0), new Vector2(20, 20), MarqueeSelectionMode.Intersect);

        Assert.True(insideSelected.IsSelected);
        Assert.False(insideUnselected.IsSelected);
        Assert.False(outsideSelected.IsSelected);
    }

    [Fact]
    public void MarqueeSelectLinedefs_Default_RequiresBothEndpointsInside()
    {
        var map = new MapData();
        var v1 = map.CreateVertex(new Vector2(0, 0));
        var v2 = map.CreateVertex(new Vector2(10, 0));
        var v3 = map.CreateVertex(new Vector2(100, 0));
        var sector = map.CreateSector(0, 128);
        var bothInside = map.CreateLinedef(v1, v2, front: sector, back: null);
        var oneOutside = map.CreateLinedef(v2, v3, front: sector, back: null);

        map.MarqueeSelectLinedefs(new Vector2(-5, -5), new Vector2(15, 5), MarqueeSelectionMode.Select, touching: false);

        Assert.True(bothInside.IsSelected);
        Assert.False(oneOutside.IsSelected);
    }

    [Fact]
    public void MarqueeSelectLinedefs_Touching_SelectsLinedefWithOnlyOneEndpointInside()
    {
        var map = new MapData();
        var v1 = map.CreateVertex(new Vector2(0, 0));
        var v2 = map.CreateVertex(new Vector2(10, 0));
        var v3 = map.CreateVertex(new Vector2(100, 0));
        var sector = map.CreateSector(0, 128);
        var oneOutside = map.CreateLinedef(v2, v3, front: sector, back: null);

        map.MarqueeSelectLinedefs(new Vector2(-5, -5), new Vector2(15, 5), MarqueeSelectionMode.Select, touching: true);

        Assert.True(oneOutside.IsSelected);
    }

    [Fact]
    public void MarqueeSelectLinedefs_Touching_SelectsLinedefCrossingRectangleWithBothEndpointsOutside()
    {
        var map = new MapData();
        var v1 = map.CreateVertex(new Vector2(-100, 0));
        var v2 = map.CreateVertex(new Vector2(100, 0));
        var sector = map.CreateSector(0, 128);
        var crossing = map.CreateLinedef(v1, v2, front: sector, back: null);

        map.MarqueeSelectLinedefs(new Vector2(-10, -10), new Vector2(10, 10), MarqueeSelectionMode.Select, touching: true);

        Assert.True(crossing.IsSelected);
    }

    [Fact]
    public void MarqueeSelectLinedefs_Touching_DoesNotSelectALinedefEntirelyOutsideAndNotCrossing()
    {
        var map = new MapData();
        var v1 = map.CreateVertex(new Vector2(50, 50));
        var v2 = map.CreateVertex(new Vector2(60, 60));
        var sector = map.CreateSector(0, 128);
        var farAway = map.CreateLinedef(v1, v2, front: sector, back: null);

        map.MarqueeSelectLinedefs(new Vector2(-10, -10), new Vector2(10, 10), MarqueeSelectionMode.Select, touching: true);

        Assert.False(farAway.IsSelected);
    }

    [Fact]
    public void MarqueeSelectSectors_Default_RequiresEveryVertexInside()
    {
        var map = new MapData();
        var (sector, _) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));

        map.MarqueeSelectSectors(new Vector2(-10, -10), new Vector2(100, 100), MarqueeSelectionMode.Select, touching: false);

        Assert.True(sector.IsSelected);
    }

    [Fact]
    public void MarqueeSelectSectors_Default_OneVertexOutsideDisqualifies()
    {
        var map = new MapData();
        var (sector, _) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));

        map.MarqueeSelectSectors(new Vector2(-10, -10), new Vector2(50, 50), MarqueeSelectionMode.Select, touching: false);

        Assert.False(sector.IsSelected);
    }

    [Fact]
    public void MarqueeSelectSectors_ResyncsBorderingLinedefSelectionAfterward()
    {
        var map = new MapData();
        var (sector, _) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));

        map.MarqueeSelectSectors(new Vector2(-10, -10), new Vector2(100, 100), MarqueeSelectionMode.Select, touching: false);

        Assert.All(sector.Sidedefs, sd => Assert.True(sd.Linedef.IsSelected));
    }

    [Fact]
    public void MarqueeSelectSectors_Touching_SelectsSectorWithOnlyPartialOverlap()
    {
        var map = new MapData();
        var (sector, _) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));

        map.MarqueeSelectSectors(new Vector2(-10, -10), new Vector2(10, 10), MarqueeSelectionMode.Select, touching: true);

        Assert.True(sector.IsSelected);
    }

    [Fact]
    public void MarqueeSelectSectors_Touching_DoesNotSelectASectorWithNoOverlapAtAll()
    {
        var map = new MapData();
        var (sector, _) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));

        map.MarqueeSelectSectors(new Vector2(500, 500), new Vector2(600, 600), MarqueeSelectionMode.Select, touching: true);

        Assert.False(sector.IsSelected);
    }

    [Fact]
    public void MarqueeSelectThings_SelectsByPositionInsideRectangle()
    {
        var map = new MapData();
        var inside = map.CreateThing(new Vector2(10, 10), type: 1);
        var outside = map.CreateThing(new Vector2(100, 100), type: 1);

        map.MarqueeSelectThings(new Vector2(0, 0), new Vector2(20, 20), MarqueeSelectionMode.Select);

        Assert.True(inside.IsSelected);
        Assert.False(outside.IsSelected);
    }
}
