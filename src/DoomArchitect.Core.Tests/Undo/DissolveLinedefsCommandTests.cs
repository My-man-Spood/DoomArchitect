using System.Linq;
using System.Numerics;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Undo;

namespace DoomArchitect.Core.Tests.Undo;

public class DissolveLinedefsCommandTests
{
    /// <summary>A small 10x10 sector (left) sharing a vertical wall with a much bigger 40x10 sector (right) - big enough of a size gap that the "keep the larger" tie-break is unambiguous.</summary>
    private static (MapData Map, Sector Small, Sector Big, Linedef Shared) BuildJoinCandidates()
    {
        var map = new MapData();
        var small = map.CreateSector(0, 128);
        var big = map.CreateSector(0, 128);

        var sharedBottom = map.CreateVertex(new Vector2(0, 0));
        var sharedTop = map.CreateVertex(new Vector2(0, 10));
        var smallOuterTop = map.CreateVertex(new Vector2(-10, 10));
        var smallOuterBottom = map.CreateVertex(new Vector2(-10, 0));
        var bigOuterBottom = map.CreateVertex(new Vector2(40, 0));
        var bigOuterTop = map.CreateVertex(new Vector2(40, 10));

        var shared = map.CreateLinedef(sharedBottom, sharedTop, small, big);
        map.CreateLinedef(sharedTop, smallOuterTop, small, null);
        map.CreateLinedef(smallOuterTop, smallOuterBottom, small, null);
        map.CreateLinedef(smallOuterBottom, sharedBottom, small, null);
        map.CreateLinedef(sharedBottom, bigOuterBottom, big, null);
        map.CreateLinedef(bigOuterBottom, bigOuterTop, big, null);
        map.CreateLinedef(bigOuterTop, sharedTop, big, null);

        return (map, small, big, shared);
    }

    [Fact]
    public void Do_TwoDifferentSectors_JoinsTheSmallerIntoTheLarger()
    {
        var (map, small, big, shared) = BuildJoinCandidates();
        var smallOwnWalls = small.Sidedefs.Select(s => s.Linedef).Where(l => l != shared).ToList();

        new DissolveLinedefsCommand(map, new[] { shared }).Do();

        Assert.DoesNotContain(small, map.Sectors);
        Assert.Contains(big, map.Sectors);
        Assert.DoesNotContain(shared, map.Linedefs);
        Assert.All(smallOwnWalls, w => Assert.True(w.Front?.Sector == big || w.Back?.Sector == big));
    }

    [Fact]
    public void Do_PlainOneSidedLinedef_JustRemovesIt()
    {
        var map = new MapData();
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(10, 0));
        var line = map.CreateLinedef(a, b, null, null);

        new DissolveLinedefsCommand(map, new[] { line }).Do();

        Assert.DoesNotContain(line, map.Linedefs);
    }

    [Fact]
    public void Undo_TwoDifferentSectors_RestoresBothSectorsAndTheWall()
    {
        var (map, small, big, shared) = BuildJoinCandidates();
        var originalSmallSidedefCount = small.Sidedefs.Count;
        var originalBigSidedefCount = big.Sidedefs.Count;

        var command = new DissolveLinedefsCommand(map, new[] { shared });
        command.Do();
        command.Undo();

        Assert.Contains(small, map.Sectors);
        Assert.Contains(shared, map.Linedefs);
        Assert.Equal(originalSmallSidedefCount, small.Sidedefs.Count);
        Assert.Equal(originalBigSidedefCount, big.Sidedefs.Count);
        Assert.Equal(small, shared.Front!.Sector);
        Assert.Equal(big, shared.Back!.Sector);
    }

    [Fact]
    public void Do_StandaloneTriangleTooSmallAfterDissolve_IsDisposedAndItsSurvivingWallOrphaned()
    {
        var map = new MapData();
        var triangle = map.CreateSector(0, 128);
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(10, 0));
        var c = map.CreateVertex(new Vector2(5, 10));

        var wallAB = map.CreateLinedef(a, b, triangle, null);
        var wallBC = map.CreateLinedef(b, c, triangle, null);
        var wallCA = map.CreateLinedef(c, a, triangle, null);

        new DissolveLinedefsCommand(map, new[] { wallAB, wallBC }).Do();

        Assert.DoesNotContain(triangle, map.Sectors);
        Assert.DoesNotContain(wallAB, map.Linedefs);
        Assert.DoesNotContain(wallBC, map.Linedefs);
        // The third wall survives (dissolve never touched it directly) but is now
        // fully orphaned - its own sector was disposed once it became too small
        // to be a real polygon, and no valid replacement boundary exists at that
        // point once two of the triangle's three sides are simply gone.
        Assert.Contains(wallCA, map.Linedefs);
        Assert.Null(wallCA.Front);
        Assert.Null(wallCA.Back);
    }

    [Fact]
    public void Undo_StandaloneTriangleTooSmallAfterDissolve_FullyRestoresIt()
    {
        var map = new MapData();
        var triangle = map.CreateSector(0, 128);
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(10, 0));
        var c = map.CreateVertex(new Vector2(5, 10));

        var wallAB = map.CreateLinedef(a, b, triangle, null);
        var wallBC = map.CreateLinedef(b, c, triangle, null);
        var wallCA = map.CreateLinedef(c, a, triangle, null);

        var command = new DissolveLinedefsCommand(map, new[] { wallAB, wallBC });
        command.Do();
        command.Undo();

        Assert.Contains(triangle, map.Sectors);
        Assert.Contains(wallAB, map.Linedefs);
        Assert.Contains(wallBC, map.Linedefs);
        Assert.Equal(3, triangle.Sidedefs.Count);
        Assert.Equal(triangle, wallCA.Front!.Sector);
    }
}
