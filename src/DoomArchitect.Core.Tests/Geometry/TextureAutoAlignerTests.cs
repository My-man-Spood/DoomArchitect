using System.Linq;
using System.Numerics;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Tests.Geometry;

public class TextureAutoAlignerTests
{
    [Fact]
    public void Align_StraightChainOfSameTexturedWalls_AccumulatesXByLengthAndWraps()
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

        var results = TextureAutoAligner.Align(
            l1.Front!, WallPartKind.Middle, alignX: true, alignY: false, _ => 64, _ => 128);

        Assert.Equal(3, results.Count);
        Assert.Equal(0, results.Single(r => r.Side == l1.Front).OffsetX);
        Assert.Equal(40, results.Single(r => r.Side == l2.Front).OffsetX);
        Assert.Equal(16, results.Single(r => r.Side == l3.Front).OffsetX); // 80 % 64
    }

    [Fact]
    public void Align_TextureNameChangesPartway_StopsPropagationThere()
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

        var results = TextureAutoAligner.Align(
            l1.Front!, WallPartKind.Middle, alignX: true, alignY: false, _ => 64, _ => 128);

        var result = Assert.Single(results);
        Assert.Equal(l1.Front, result.Side);
    }

    [Fact]
    public void Align_ClosedLoopOfSameTexturedWalls_TerminatesAndAlignsEachOnce()
    {
        var map = new MapData();
        var v0 = map.CreateVertex(new Vector2(0, 0));
        var v1 = map.CreateVertex(new Vector2(40, 0));
        var v2 = map.CreateVertex(new Vector2(40, 40));
        var v3 = map.CreateVertex(new Vector2(0, 40));
        var sector = map.CreateSector(0, 128);

        var l1 = map.CreateLinedef(v0, v1, sector, null);
        var l2 = map.CreateLinedef(v1, v2, sector, null);
        var l3 = map.CreateLinedef(v2, v3, sector, null);
        var l4 = map.CreateLinedef(v3, v0, sector, null);
        foreach (var l in new[] { l1, l2, l3, l4 }) l.Front!.MiddleTexture = "WALL1";

        var results = TextureAutoAligner.Align(
            l1.Front!, WallPartKind.Middle, alignX: true, alignY: false, _ => 64, _ => 128);

        Assert.Equal(4, results.Count);
        Assert.Equal(4, results.Select(r => r.Side).Distinct().Count());
    }

    [Fact]
    public void Align_UpperWallsAtDifferentHeights_SolvesYToMatchWorldTextureTop()
    {
        var map = new MapData();
        var v0 = map.CreateVertex(new Vector2(0, 0));
        var v1 = map.CreateVertex(new Vector2(40, 0));
        var v2 = map.CreateVertex(new Vector2(80, 0));
        var front = map.CreateSector(0, 200);
        var back1 = map.CreateSector(0, 100);
        var back2 = map.CreateSector(0, 150);

        var l1 = map.CreateLinedef(v0, v1, front, back1);
        var l2 = map.CreateLinedef(v1, v2, front, back2);
        l1.Front!.UpperTexture = "BROWN1";
        l2.Front!.UpperTexture = "BROWN1";

        var results = TextureAutoAligner.Align(
            l1.Front!, WallPartKind.Upper, alignX: false, alignY: true, _ => 64, _ => 128);

        Assert.Equal(2, results.Count);
        Assert.Equal(0, results.Single(r => r.Side == l1.Front).OffsetY); // the start, unchanged
        Assert.Equal(-50, results.Single(r => r.Side == l2.Front).OffsetY);
    }

    [Fact]
    public void Align_StartPartIsMaskedMiddle_SkipsYAlignmentEntirely()
    {
        var map = new MapData();
        var v0 = map.CreateVertex(new Vector2(0, 0));
        var v1 = map.CreateVertex(new Vector2(40, 0));
        var front = map.CreateSector(0, 128);
        var back = map.CreateSector(0, 128);
        var linedef = map.CreateLinedef(v0, v1, front, back);
        linedef.Front!.MiddleTexture = "MIDBARS1";

        var results = TextureAutoAligner.Align(
            linedef.Front!, WallPartKind.Middle, alignX: true, alignY: true, _ => 64, _ => 40);

        var result = Assert.Single(results);
        Assert.NotNull(result.OffsetX);
        Assert.Null(result.OffsetY);
    }

    [Fact]
    public void Align_NoMiddleTextureOnStart_ReturnsNoResults()
    {
        var map = new MapData();
        var v0 = map.CreateVertex(new Vector2(0, 0));
        var v1 = map.CreateVertex(new Vector2(40, 0));
        var sector = map.CreateSector(0, 128);
        var linedef = map.CreateLinedef(v0, v1, sector, null);

        var results = TextureAutoAligner.Align(
            linedef.Front!, WallPartKind.Middle, alignX: true, alignY: false, _ => 64, _ => 128);

        Assert.Empty(results);
    }
}
