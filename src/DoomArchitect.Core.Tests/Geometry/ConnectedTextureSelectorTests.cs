using System.Numerics;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Tests.Geometry;

public class ConnectedTextureSelectorTests
{
    [Fact]
    public void FindConnectedWalls_StraightChainOfSameTexturedWalls_FindsAllThree()
    {
        var map = new MapData();
        var v0 = map.CreateVertex(new Vector2(0, 0));
        var v1 = map.CreateVertex(new Vector2(40, 0));
        var v2 = map.CreateVertex(new Vector2(80, 0));
        var v3 = map.CreateVertex(new Vector2(120, 0));
        var sector = map.CreateSector(0, 128);

        var l1 = map.CreateLinedef(v0, v1, sector, null);
        var l2 = map.CreateLinedef(v1, v2, sector, null);
        var l3 = map.CreateLinedef(v2, v3, sector, null);
        l1.Front!.MiddleTexture = "WALL1";
        l2.Front!.MiddleTexture = "WALL1";
        l3.Front!.MiddleTexture = "WALL1";

        var result = ConnectedTextureSelector.FindConnectedWalls(l1.Front!, WallPartKind.Middle, _ => 128);

        Assert.Equal(3, result.Count);
        Assert.Contains(l1.Front, result);
        Assert.Contains(l2.Front, result);
        Assert.Contains(l3.Front, result);
    }

    [Fact]
    public void FindConnectedWalls_TextureNameChangesPartway_StopsThere()
    {
        var map = new MapData();
        var v0 = map.CreateVertex(new Vector2(0, 0));
        var v1 = map.CreateVertex(new Vector2(40, 0));
        var v2 = map.CreateVertex(new Vector2(80, 0));
        var sector = map.CreateSector(0, 128);

        var l1 = map.CreateLinedef(v0, v1, sector, null);
        var l2 = map.CreateLinedef(v1, v2, sector, null);
        l1.Front!.MiddleTexture = "WALL1";
        l2.Front!.MiddleTexture = "OTHERTEX";

        var result = ConnectedTextureSelector.FindConnectedWalls(l1.Front!, WallPartKind.Middle, _ => 128);

        var side = Assert.Single(result);
        Assert.Equal(l1.Front, side);
    }

    [Fact]
    public void FindConnectedWalls_DifferentPartRole_DoesNotCrossOver()
    {
        var map = new MapData();
        var v0 = map.CreateVertex(new Vector2(0, 0));
        var v1 = map.CreateVertex(new Vector2(40, 0));
        var v2 = map.CreateVertex(new Vector2(80, 0));
        var lowSector = map.CreateSector(0, 128);
        var highSector = map.CreateSector(64, 192);

        var l1 = map.CreateLinedef(v0, v1, lowSector, null);
        // A two-sided step wall - its Upper carries the same texture
        // name as l1's own Middle, but it's a different part role, so
        // it must never be reached from a Middle-only walk.
        var l2 = map.CreateLinedef(v1, v2, highSector, lowSector);
        l1.Front!.MiddleTexture = "WALL1";
        l2.Front!.UpperTexture = "WALL1";

        var result = ConnectedTextureSelector.FindConnectedWalls(l1.Front!, WallPartKind.Middle, _ => 128);

        var side = Assert.Single(result);
        Assert.Equal(l1.Front, side);
    }

    [Fact]
    public void FindConnectedSectors_SameFloorTexture_FindsBothSectors()
    {
        var map = new MapData();
        var a = map.CreateSector(0, 128);
        var b = map.CreateSector(0, 128);
        a.FloorTexture = "FLOOR1";
        b.FloorTexture = "FLOOR1";

        var v0 = map.CreateVertex(new Vector2(0, 0));
        var v1 = map.CreateVertex(new Vector2(0, 64));
        map.CreateLinedef(v0, v1, a, b);

        var result = ConnectedTextureSelector.FindConnectedSectors(a, isFloor: true, matchTexture: true, matchHeight: false);

        Assert.Equal(2, result.Count);
        Assert.Contains(a, result);
        Assert.Contains(b, result);
    }

    [Fact]
    public void FindConnectedSectors_DifferentFloorTexture_DoesNotCross()
    {
        var map = new MapData();
        var a = map.CreateSector(0, 128);
        var b = map.CreateSector(0, 128);
        a.FloorTexture = "FLOOR1";
        b.FloorTexture = "FLOOR2";

        var v0 = map.CreateVertex(new Vector2(0, 0));
        var v1 = map.CreateVertex(new Vector2(0, 64));
        map.CreateLinedef(v0, v1, a, b);

        var result = ConnectedTextureSelector.FindConnectedSectors(a, isFloor: true, matchTexture: true, matchHeight: false);

        var sector = Assert.Single(result);
        Assert.Equal(a, sector);
    }

    [Fact]
    public void FindConnectedSectors_CeilingMode_IgnoresFloorTextureDifference()
    {
        var map = new MapData();
        var a = map.CreateSector(0, 128);
        var b = map.CreateSector(0, 128);
        a.FloorTexture = "FLOOR1";
        b.FloorTexture = "FLOOR2";
        a.CeilingTexture = "CEIL1";
        b.CeilingTexture = "CEIL1";

        var v0 = map.CreateVertex(new Vector2(0, 0));
        var v1 = map.CreateVertex(new Vector2(0, 64));
        map.CreateLinedef(v0, v1, a, b);

        var result = ConnectedTextureSelector.FindConnectedSectors(a, isFloor: false, matchTexture: true, matchHeight: false);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void FindConnectedSectors_MatchHeightOnly_IgnoresTextureDifferenceButRequiresSameHeight()
    {
        var map = new MapData();
        var a = map.CreateSector(0, 128);
        var b = map.CreateSector(0, 128);
        var c = map.CreateSector(16, 128);
        a.FloorTexture = "FLOOR1";
        b.FloorTexture = "FLOOR2";
        c.FloorTexture = "FLOOR1";

        var v0 = map.CreateVertex(new Vector2(0, 0));
        var v1 = map.CreateVertex(new Vector2(0, 64));
        var v2 = map.CreateVertex(new Vector2(64, 0));
        var v3 = map.CreateVertex(new Vector2(64, 64));
        map.CreateLinedef(v0, v1, a, b);
        map.CreateLinedef(v2, v3, a, c);

        var result = ConnectedTextureSelector.FindConnectedSectors(a, isFloor: true, matchTexture: false, matchHeight: true);

        // b matches by height (0) despite a different texture; c has the
        // same texture but a different height (16), so it's excluded -
        // confirming this mode genuinely ignores texture, not just
        // happening to agree with it.
        Assert.Equal(2, result.Count);
        Assert.Contains(a, result);
        Assert.Contains(b, result);
    }

    [Fact]
    public void FindConnectedSectors_MatchBothTextureAndHeight_RequiresBoth()
    {
        var map = new MapData();
        var a = map.CreateSector(0, 128);
        var sameTextureDifferentHeight = map.CreateSector(16, 128);
        var sameHeightDifferentTexture = map.CreateSector(0, 128);
        a.FloorTexture = "FLOOR1";
        sameTextureDifferentHeight.FloorTexture = "FLOOR1";
        sameHeightDifferentTexture.FloorTexture = "FLOOR2";

        var v0 = map.CreateVertex(new Vector2(0, 0));
        var v1 = map.CreateVertex(new Vector2(0, 64));
        var v2 = map.CreateVertex(new Vector2(64, 0));
        var v3 = map.CreateVertex(new Vector2(64, 64));
        map.CreateLinedef(v0, v1, a, sameTextureDifferentHeight);
        map.CreateLinedef(v2, v3, a, sameHeightDifferentTexture);

        var result = ConnectedTextureSelector.FindConnectedSectors(a, isFloor: true, matchTexture: true, matchHeight: true);

        var sector = Assert.Single(result);
        Assert.Equal(a, sector);
    }
}
