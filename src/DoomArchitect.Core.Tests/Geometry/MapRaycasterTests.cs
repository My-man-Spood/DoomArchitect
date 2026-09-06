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

    private enum WallSide { Near, Full }

    private static MapRaycaster BuildRaycaster(MapData map, Func<string, double>? middleTextureHeightLookup = null)
    {
        var index = new UniformGridSpatialIndex();
        index.Rebuild(map);
        return new MapRaycaster(index, middleTextureHeightLookup);
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
