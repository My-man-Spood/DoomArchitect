using System.Numerics;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Tests.Geometry;

public class MapRaycasterTests
{
    [Fact]
    public void FindTarget_LookingStraightDownFromInsideTheRoom_HitsTheFloor()
    {
        var map = new MapData();
        var sector = BuildSquareSector(map, 0, 0, 256, floor: 0, ceiling: 128);
        var raycaster = BuildRaycaster(map);

        var target = raycaster.FindTarget(new Vector3(128, 128, 64), new Vector3(0, 0, -1));

        Assert.NotNull(target);
        Assert.Equal(TargetSurfaceKind.Floor, target.Value.Kind);
        Assert.Equal(sector, target.Value.Sector);
        Assert.Equal(64, target.Value.Distance, precision: 4);
    }

    [Fact]
    public void FindTarget_LookingStraightUpFromInsideTheRoom_HitsTheCeiling()
    {
        var map = new MapData();
        var sector = BuildSquareSector(map, 0, 0, 256, floor: 0, ceiling: 128);
        var raycaster = BuildRaycaster(map);

        var target = raycaster.FindTarget(new Vector3(128, 128, 64), new Vector3(0, 0, 1));

        Assert.NotNull(target);
        Assert.Equal(TargetSurfaceKind.Ceiling, target.Value.Kind);
        Assert.Equal(sector, target.Value.Sector);
        Assert.Equal(64, target.Value.Distance, precision: 4);
    }

    [Fact]
    public void FindTarget_LookingAtAOneSidedWall_HitsIt()
    {
        var map = new MapData();
        var sector = BuildSquareSector(map, 0, 0, 256, floor: 0, ceiling: 128);
        var raycaster = BuildRaycaster(map);

        // From inside the room, looking toward the far (+X) wall at x=256.
        var target = raycaster.FindTarget(new Vector3(10, 128, 64), new Vector3(1, 0, 0));

        Assert.NotNull(target);
        Assert.Equal(TargetSurfaceKind.Wall, target.Value.Kind);
        Assert.Equal(sector, target.Value.Sector);
        Assert.Equal(246, target.Value.Distance, precision: 4);
    }

    [Fact]
    public void FindTarget_LookingAtAMaskedMiddleWall_HitsItGivenAHeightLookup()
    {
        var map = new MapData();
        var v0 = map.CreateVertex(new Vector2(0, 0));
        var v1 = map.CreateVertex(new Vector2(0, 100));
        var front = map.CreateSector(0, 128);
        var back = map.CreateSector(0, 128);
        var linedef = map.CreateLinedef(v0, v1, front, back);
        linedef.Front!.MiddleTexture = "MIDBARS1";

        var raycaster = BuildRaycaster(map, _ => 40); // opening [0,128] -> texture spans [88,128]

        var target = raycaster.FindTarget(new Vector3(-50, 50, 100), new Vector3(1, 0, 0));

        Assert.NotNull(target);
        Assert.Equal(TargetSurfaceKind.Wall, target.Value.Kind);
        Assert.Equal("MIDBARS1", target.Value.WallSegment!.Value.Texture);
        Assert.Equal(50, target.Value.Distance, precision: 4);
    }

    [Fact]
    public void FindTarget_MaskedMiddleWallWithNoHeightLookupProvided_IsNotHit()
    {
        var map = new MapData();
        var v0 = map.CreateVertex(new Vector2(0, 0));
        var v1 = map.CreateVertex(new Vector2(0, 100));
        var front = map.CreateSector(0, 128);
        var back = map.CreateSector(0, 128);
        var linedef = map.CreateLinedef(v0, v1, front, back);
        linedef.Front!.MiddleTexture = "MIDBARS1";

        var raycaster = BuildRaycaster(map); // no height lookup - masked middle skipped entirely

        var target = raycaster.FindTarget(new Vector3(-50, 50, 100), new Vector3(1, 0, 0));

        Assert.Null(target);
    }

    /// <summary>
    /// Regression test for a real bug: a two-sided masked middle's front
    /// and back segments build at the exact same world position (same
    /// "opening" bounds), so a ray hitting that spot ties at the identical
    /// distance for both - previously always resolved to the front
    /// (whichever <c>LinedefWallBuilder.BuildTwoSided</c> happens to add
    /// first), regardless of which face the viewer was actually standing
    /// in front of and looking at.
    /// </summary>
    [Fact]
    public void FindTarget_CoincidentFrontAndBackMaskedMiddle_PicksWhicheverSideTheViewerIsStandingIn()
    {
        var map = new MapData();

        // A shared wall at x=0 between two closed rooms, one on each side.
        // Walking the shared edge Start(v0)->End(v1) runs north, whose
        // real "right" (per WallMeshBuilder/LinedefOverlayHandler's own
        // already-shipped front convention) is east (+X) - so sectorA,
        // passed as the shared edge's Front sector below, has to actually
        // sit on the +X side for this test's own ground truth to be
        // correct, not just self-consistent.
        var v0 = map.CreateVertex(new Vector2(0, -50));
        var v1 = map.CreateVertex(new Vector2(0, 50));
        var vA0 = map.CreateVertex(new Vector2(100, -50));
        var vA1 = map.CreateVertex(new Vector2(100, 50));
        var vB0 = map.CreateVertex(new Vector2(-100, -50));
        var vB1 = map.CreateVertex(new Vector2(-100, 50));

        var sectorA = map.CreateSector(0, 128);
        var sectorB = map.CreateSector(0, 128);

        var shared = map.CreateLinedef(v0, v1, sectorA, sectorB);
        shared.Front!.MiddleTexture = "MIDBARS1";
        shared.Back!.MiddleTexture = "MIDBARS1";

        // Each sector's own outer loop must close back through the shared
        // edge walked in *its own* direction (front = Start->End, back =
        // End->Start - see SectorTracer's own remarks) - A's outer path
        // runs v1->v0 to match the shared edge's front direction (v0->v1),
        // B's runs v0->v1 to match its own back direction (v1->v0). Purely
        // topological - unaffected by which physical side vA*/vB* sit on.
        map.CreateLinedef(v1, vA1, sectorA, null);
        map.CreateLinedef(vA1, vA0, sectorA, null);
        map.CreateLinedef(vA0, v0, sectorA, null);

        map.CreateLinedef(v0, vB0, sectorB, null);
        map.CreateLinedef(vB0, vB1, sectorB, null);
        map.CreateLinedef(vB1, v1, sectorB, null);

        // Full-height opening on both sides (same floor/ceiling) - the
        // masked middle spans the whole 128 units, so front's and back's
        // segments are genuinely identical in extent, not just close.
        var raycaster = BuildRaycaster(map, _ => 128);

        var fromA = raycaster.FindTarget(new Vector3(50, 0, 64), new Vector3(-1, 0, 0));
        Assert.NotNull(fromA);
        Assert.Equal(shared.Front, fromA!.Value.WallSegment!.Value.Side);

        var fromB = raycaster.FindTarget(new Vector3(-50, 0, 64), new Vector3(1, 0, 0));
        Assert.NotNull(fromB);
        Assert.Equal(shared.Back, fromB!.Value.WallSegment!.Value.Side);
    }

    /// <summary>
    /// The real case that motivated switching the tie-break from sector
    /// identity to actual wall-facing geometry: a self-referencing-sector
    /// decoration (e.g. a flag or curtain hanging inside a single room -
    /// confirmed a real, common pattern in the user's own map, which has
    /// several) puts front *and* back on the exact same sector, where a
    /// sector-based tie-break can never distinguish them at all.
    /// </summary>
    [Fact]
    public void FindTarget_SelfReferencingSectorMaskedMiddle_StillPicksTheFacingSide()
    {
        var map = new MapData();

        var v0 = map.CreateVertex(new Vector2(-100, -50));
        var v1 = map.CreateVertex(new Vector2(100, -50));
        var v2 = map.CreateVertex(new Vector2(100, 50));
        var v3 = map.CreateVertex(new Vector2(-100, 50));
        var sector = map.CreateSector(0, 128);
        map.CreateLinedef(v0, v1, sector, null);
        map.CreateLinedef(v1, v2, sector, null);
        map.CreateLinedef(v2, v3, sector, null);
        map.CreateLinedef(v3, v0, sector, null);

        // A dividing wall floating inside the same room, both sides
        // belonging to that same sector.
        var vi0 = map.CreateVertex(new Vector2(0, -30));
        var vi1 = map.CreateVertex(new Vector2(0, 30));
        var divider = map.CreateLinedef(vi0, vi1, sector, sector);
        divider.Front!.MiddleTexture = "MIDBARS1";
        divider.Back!.MiddleTexture = "MIDBARS1";

        var raycaster = BuildRaycaster(map, _ => 128);

        // Walking the divider Start(vi0)->End(vi1) runs north, whose real
        // "right" (see the adjacent-rooms test above for the same
        // derivation against WallMeshBuilder/LinedefOverlayHandler) is
        // east (+X) - so the viewer standing east of the divider faces
        // its Front, not the one standing west of it.
        var fromLeft = raycaster.FindTarget(new Vector3(-50, 0, 64), new Vector3(1, 0, 0));
        Assert.NotNull(fromLeft);
        Assert.Equal(divider.Back, fromLeft!.Value.WallSegment!.Value.Side);

        var fromRight = raycaster.FindTarget(new Vector3(50, 0, 64), new Vector3(-1, 0, 0));
        Assert.NotNull(fromRight);
        Assert.Equal(divider.Front, fromRight!.Value.WallSegment!.Value.Side);
    }

    [Fact]
    public void FindTarget_TwoWallsAlongTheSameRay_HitsTheNearerOne()
    {
        var map = new MapData();
        var nearSector = BuildSquareSector(map, 100, 0, 100, floor: 0, ceiling: 128, wallAt: WallSide.Near);
        var farSector = BuildSquareSector(map, 200, 0, 100, floor: 0, ceiling: 128, wallAt: WallSide.Near);

        var raycaster = BuildRaycaster(map);

        var target = raycaster.FindTarget(new Vector3(0, 50, 64), new Vector3(1, 0, 0));

        Assert.NotNull(target);
        Assert.Equal(nearSector, target.Value.Sector);
        Assert.Equal(100, target.Value.Distance, precision: 4);
    }

    [Fact]
    public void FindTarget_EmptyMap_ReturnsNull()
    {
        var map = new MapData();
        var raycaster = BuildRaycaster(map);

        var target = raycaster.FindTarget(Vector3.Zero, new Vector3(1, 0, 0));

        Assert.Null(target);
    }

    [Fact]
    public void FindTarget_LookingAtAThing_HitsItsPickBox()
    {
        var map = new MapData();
        var thing = map.CreateThing(new Vector2(100, 50), type: 1);
        var raycaster = BuildRaycaster(map, thingPickBoundsLookup: _ => new ThingPickBounds(Radius: 16, Height: 56, WorldZ: 0, Sector: null));

        var target = raycaster.FindTarget(new Vector3(0, 50, 20), new Vector3(1, 0, 0));

        Assert.NotNull(target);
        Assert.Equal(TargetSurfaceKind.Thing, target.Value.Kind);
        Assert.Equal(thing, target.Value.Thing);
        Assert.Equal(84, target.Value.Distance, precision: 4); // box's near face at x=84 (100-16)
    }

    [Fact]
    public void FindTarget_RayPassesAboveAThingsPickBox_MissesIt()
    {
        var map = new MapData();
        map.CreateThing(new Vector2(100, 50), type: 1);
        var raycaster = BuildRaycaster(map, thingPickBoundsLookup: _ => new ThingPickBounds(Radius: 16, Height: 56, WorldZ: 0, Sector: null));

        var target = raycaster.FindTarget(new Vector3(0, 50, 1000), new Vector3(1, 0, 0));

        Assert.Null(target);
    }

    [Fact]
    public void FindTarget_NoThingPickBoundsLookupProvided_SkipsThingsEntirely()
    {
        var map = new MapData();
        map.CreateThing(new Vector2(100, 50), type: 1);
        var raycaster = BuildRaycaster(map);

        var target = raycaster.FindTarget(new Vector3(0, 50, 20), new Vector3(1, 0, 0));

        Assert.Null(target);
    }

    [Fact]
    public void FindTarget_AThingInFrontOfAWall_HitsTheThingFirst()
    {
        var map = new MapData();
        BuildSquareSector(map, 100, 0, 100, floor: 0, ceiling: 128, wallAt: WallSide.Near);
        var thing = map.CreateThing(new Vector2(50, 50), type: 1);

        var raycaster = BuildRaycaster(map, thingPickBoundsLookup: _ => new ThingPickBounds(Radius: 16, Height: 56, WorldZ: 0, Sector: null));

        var target = raycaster.FindTarget(new Vector3(0, 50, 20), new Vector3(1, 0, 0));

        Assert.NotNull(target);
        Assert.Equal(TargetSurfaceKind.Thing, target.Value.Kind);
        Assert.Equal(thing, target.Value.Thing);
    }

    private enum WallSide { Near, Full }

    private static MapRaycaster BuildRaycaster(
        MapData map, Func<string, double>? middleTextureHeightLookup = null,
        Func<Thing, ThingPickBounds>? thingPickBoundsLookup = null)
    {
        var index = new UniformGridSpatialIndex();
        index.Rebuild(map);
        return new MapRaycaster(index, middleTextureHeightLookup, thingPickBoundsLookup);
    }

    private static Sector BuildSquareSector(
        MapData map, float x, float y, float size, double floor, double ceiling, WallSide wallAt = WallSide.Full)
    {
        var sector = map.CreateSector(floor, ceiling);
        var v0 = map.CreateVertex(new Vector2(x, y));
        var v1 = map.CreateVertex(new Vector2(x, y + size));
        var v2 = map.CreateVertex(new Vector2(x + size, y + size));
        var v3 = map.CreateVertex(new Vector2(x + size, y));

        if (wallAt == WallSide.Near)
        {
            // Only the near (-X facing) wall - enough to be hit by a ray
            // travelling in +X without needing a fully closed room.
            map.CreateLinedef(v0, v1, sector, null);
            return sector;
        }

        map.CreateLinedef(v0, v1, sector, null);
        map.CreateLinedef(v1, v2, sector, null);
        map.CreateLinedef(v2, v3, sector, null);
        map.CreateLinedef(v3, v0, sector, null);

        return sector;
    }
}
