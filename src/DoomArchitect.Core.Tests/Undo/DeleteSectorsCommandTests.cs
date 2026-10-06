using System.Linq;
using System.Numerics;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Tests.Map;
using DoomArchitect.Core.Undo;

namespace DoomArchitect.Core.Tests.Undo;

public class DeleteSectorsCommandTests
{
    /// <summary>
    /// Two 10x10 sectors sharing one vertical wall (Front = left, Back =
    /// right) - the minimal shape that exercises the real "a survivor gets
    /// flipped one-sided" cascade every other test here depends on.
    /// </summary>
    private static (MapData Map, Sector Left, Sector Right, Linedef Shared) BuildTwoAdjacentSectors()
    {
        var map = new MapData();
        var left = map.CreateSector(0, 128);
        var right = map.CreateSector(0, 128);

        var sharedBottom = map.CreateVertex(new Vector2(0, 0));
        var sharedTop = map.CreateVertex(new Vector2(0, 10));
        var leftBottom = map.CreateVertex(new Vector2(-10, 0));
        var leftTop = map.CreateVertex(new Vector2(-10, 10));
        var rightBottom = map.CreateVertex(new Vector2(10, 0));
        var rightTop = map.CreateVertex(new Vector2(10, 10));

        var shared = map.CreateLinedef(sharedBottom, sharedTop, left, right);
        map.CreateLinedef(sharedTop, leftTop, left, null);
        map.CreateLinedef(leftTop, leftBottom, left, null);
        map.CreateLinedef(leftBottom, sharedBottom, left, null);
        map.CreateLinedef(rightBottom, rightTop, right, null);
        map.CreateLinedef(sharedTop, rightTop, null, right); // arbitrary side, just needs to attach to right
        map.CreateLinedef(rightBottom, sharedBottom, right, null);

        return (map, left, right, shared);
    }

    /// <summary>
    /// The reported bug: a fully isolated sector's boundary walls become
    /// fully orphaned (both sides null, per the sector-delete cascade),
    /// and so do their vertices - they get removed too
    /// (<see cref="MapData.RemoveLinedef"/>'s own cascade, UDB's real
    /// <c>Vertex.DetachLinedefP</c>). This test used to assert
    /// <c>Assert.Equal(4, map.Vertices.Count)</c> with a comment claiming
    /// that matched UDB - confirmed wrong by testing real UDB directly.
    /// </summary>
    [Fact]
    public void Do_IsolatedSector_RemovesTheSectorEveryBoundaryWallAndTheirOrphanedVertices()
    {
        var map = new MapData();
        var (sector, vertices) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 10), new Vector2(10, 10), new Vector2(10, 0));
        var boundaryWalls = map.Linedefs.ToList();

        new DeleteSectorsCommand(map, new[] { sector }).Do();

        Assert.DoesNotContain(sector, map.Sectors);
        Assert.Empty(map.Linedefs);
        Assert.All(boundaryWalls, w => Assert.DoesNotContain(w, map.Linedefs));
        Assert.Empty(map.Vertices);
        Assert.All(vertices, v => Assert.DoesNotContain(v, map.Vertices));
    }

    [Fact]
    public void Do_OneOfTwoAdjacentSectors_SharedWallSurvivesFlippedOneSided()
    {
        var (map, left, right, shared) = BuildTwoAdjacentSectors();
        var originalRightSidedef = shared.Back;
        var originalStart = shared.Start;
        var originalEnd = shared.End;

        new DeleteSectorsCommand(map, new[] { left }).Do();

        Assert.DoesNotContain(left, map.Sectors);
        Assert.Contains(shared, map.Linedefs);
        Assert.Null(shared.Back);
        Assert.NotNull(shared.Front);
        Assert.Same(originalRightSidedef, shared.Front);
        Assert.Equal(right, shared.Front!.Sector);
        // FlipBackwardLinedefs also swaps the endpoints to keep Front "on the right".
        Assert.Equal(originalEnd, shared.Start);
        Assert.Equal(originalStart, shared.End);
    }

    [Fact]
    public void Do_OneOfTwoAdjacentSectors_CopiesASurvivingTextureIntoTheNowEmptyMiddle()
    {
        var (map, left, right, shared) = BuildTwoAdjacentSectors();
        shared.Back!.UpperTexture = "BROWN1";
        shared.Back!.LowerTexture = "STARTAN2";
        shared.Back!.MiddleTexture = "-";

        new DeleteSectorsCommand(map, new[] { left }).Do();

        Assert.Equal("BROWN1", shared.Front!.MiddleTexture);
        Assert.Equal("-", shared.Front!.UpperTexture);
        Assert.Equal("-", shared.Front!.LowerTexture);
    }

    [Fact]
    public void Do_OneOfTwoAdjacentSectors_LeavesAnAlreadyPopulatedMiddleAlone()
    {
        var (map, left, right, shared) = BuildTwoAdjacentSectors();
        shared.Back!.UpperTexture = "BROWN1";
        shared.Back!.MiddleTexture = "MIDBARS3";

        new DeleteSectorsCommand(map, new[] { left }).Do();

        Assert.Equal("MIDBARS3", shared.Front!.MiddleTexture);
        Assert.Equal("-", shared.Front!.UpperTexture);
    }

    [Fact]
    public void Do_OnlyTheDeletedSectorsOwnOtherWalls_AreFullyRemoved()
    {
        var (map, left, right, shared) = BuildTwoAdjacentSectors();
        var rightOwnWalls = right.Sidedefs.Select(s => s.Linedef).Where(l => l != shared).ToList();

        new DeleteSectorsCommand(map, new[] { left }).Do();

        Assert.All(rightOwnWalls, w => Assert.Contains(w, map.Linedefs));
        Assert.Contains(right, map.Sectors);
    }

    [Fact]
    public void Do_BothAdjacentSectorsSelected_SharedWallIsFullyRemoved()
    {
        var (map, left, right, shared) = BuildTwoAdjacentSectors();

        new DeleteSectorsCommand(map, new[] { left, right }).Do();

        Assert.DoesNotContain(left, map.Sectors);
        Assert.DoesNotContain(right, map.Sectors);
        Assert.DoesNotContain(shared, map.Linedefs);
    }

    [Fact]
    public void Undo_OneOfTwoAdjacentSectors_RestoresSectorSidedefsAndTextures()
    {
        var (map, left, right, shared) = BuildTwoAdjacentSectors();
        shared.Back!.UpperTexture = "BROWN1";
        shared.Back!.MiddleTexture = "-";
        var originalFront = shared.Front;
        var originalBack = shared.Back;
        var originalStart = shared.Start;
        var originalEnd = shared.End;
        var originalUpper = originalBack!.UpperTexture;
        var originalMiddle = originalBack.MiddleTexture;
        var originalLower = originalBack.LowerTexture;
        var originalLeftSidedefCount = left.Sidedefs.Count;

        var command = new DeleteSectorsCommand(map, new[] { left });
        command.Do();
        command.Undo();

        Assert.Contains(left, map.Sectors);
        Assert.Equal(originalLeftSidedefCount, left.Sidedefs.Count);
        Assert.Same(originalFront, shared.Front);
        Assert.Same(originalBack, shared.Back);
        Assert.Equal(originalStart, shared.Start);
        Assert.Equal(originalEnd, shared.End);
        Assert.Equal(originalUpper, originalBack.UpperTexture);
        Assert.Equal(originalMiddle, originalBack.MiddleTexture);
        Assert.Equal(originalLower, originalBack.LowerTexture);
    }

    [Fact]
    public void Undo_IsolatedSector_RestoresSectorEveryBoundaryWallAndTheirVertices()
    {
        var map = new MapData();
        var (sector, vertices) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 10), new Vector2(10, 10), new Vector2(10, 0));
        var boundaryWalls = map.Linedefs.ToList();

        var command = new DeleteSectorsCommand(map, new[] { sector });
        command.Do();
        command.Undo();

        Assert.Contains(sector, map.Sectors);
        Assert.All(boundaryWalls, w => Assert.Contains(w, map.Linedefs));
        Assert.Equal(boundaryWalls.Count, sector.Sidedefs.Count);
        Assert.All(vertices, v => Assert.Contains(v, map.Vertices));
        Assert.Equal(vertices.Length, map.Vertices.Count);
    }
}
