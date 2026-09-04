using System.Numerics;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Tests.Map;

namespace DoomArchitect.Core.Tests.Geometry;

public class SectorTracerTests
{
    [Fact]
    public void Trace_SectorWithSelfReferencingLinedef_IgnoresIt()
    {
        var map = new MapData();
        var (sector, boxVertices) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));

        // A self-referencing line (front and back both point at the same
        // sector - used for fake glass/deep water tricks) contributes no
        // boundary of its own and must be ignored entirely.
        map.CreateLinedef(boxVertices[0], boxVertices[2], front: sector, back: sector);

        var loops = SectorTracer.Trace(sector);

        var loop = Assert.Single(loops);
        Assert.Equal(4, loop.Vertices.Count);
    }

    [Fact]
    public void Trace_SimpleBoxSector_ReturnsOneLoopWithFourVertices()
    {
        var map = new MapData();
        var (sector, boxVertices) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));

        var loops = SectorTracer.Trace(sector);

        var loop = Assert.Single(loops);
        Assert.Equal(4, loop.Vertices.Count);
        Assert.All(boxVertices, v => Assert.Contains(v, loop.Vertices));
    }

    [Fact]
    public void Trace_SimpleBoxSector_LoopIsClockwise()
    {
        var map = new MapData();
        var (sector, _) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));

        var loop = Assert.Single(SectorTracer.Trace(sector));

        Assert.True(loop.IsClockwise);
    }

    [Fact]
    public void Trace_SectorWithHole_ReturnsOuterAndInnerLoopsWithOppositeWinding()
    {
        var map = new MapData();
        var (sector, _) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 100), new Vector2(100, 100), new Vector2(100, 0));

        map.CreateClosedBoundary(sector,
            new Vector2(40, 40), new Vector2(60, 40), new Vector2(60, 60), new Vector2(40, 60));

        var loops = SectorTracer.Trace(sector);

        Assert.Equal(2, loops.Count);
        Assert.Contains(loops, l => l.IsClockwise);
        Assert.Contains(loops, l => !l.IsClockwise);
    }

    [Fact]
    public void Trace_DeadEndThatContinuesStraightAhead_BacktracksToTheRealLoop()
    {
        var map = new MapData();
        var (sector, boxVertices) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));

        // Continuing straight ahead (0 degree turn) from a mid-trace box
        // corner ranks as the most preferable candidate under any
        // reasonable turn-angle ordering - well ahead of the box's own
        // 90 degree turn. Only backtracking after this dead-ends recovers
        // the actual box loop; a purely greedy single-pick trace would
        // fail here.
        var deadEnd = map.CreateVertex(new Vector2(0, 128));
        map.CreateLinedef(boxVertices[1], deadEnd, front: sector, back: null);

        var loops = SectorTracer.Trace(sector);

        var loop = Assert.Single(loops);
        Assert.Equal(4, loop.Vertices.Count);
    }

    [Fact]
    public void Trace_IgnoresDanglingOneSidedLinedef()
    {
        var map = new MapData();
        var (sector, boxVertices) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));

        var stub = map.CreateVertex(new Vector2(100, 100));
        map.CreateLinedef(boxVertices[2], stub, front: sector, back: null);

        var loops = SectorTracer.Trace(sector);

        var loop = Assert.Single(loops);
        Assert.Equal(4, loop.Vertices.Count);
    }

    [Fact]
    public void Trace_TwoTrianglesSharingOneVertex_ReturnsTwoSeparateLoops()
    {
        var map = new MapData();
        var sector = map.CreateSector(0, 128);

        var shared = map.CreateVertex(new Vector2(0, 0));
        var aLeft = map.CreateVertex(new Vector2(-10, 0));
        var aTop = map.CreateVertex(new Vector2(0, 10));
        var bRight = map.CreateVertex(new Vector2(10, 0));
        var bBottom = map.CreateVertex(new Vector2(0, -10));

        map.CreateLinedef(shared, aLeft, front: sector, back: null);
        map.CreateLinedef(aLeft, aTop, front: sector, back: null);
        map.CreateLinedef(aTop, shared, front: sector, back: null);

        map.CreateLinedef(shared, bRight, front: sector, back: null);
        map.CreateLinedef(bRight, bBottom, front: sector, back: null);
        map.CreateLinedef(bBottom, shared, front: sector, back: null);

        var loops = SectorTracer.Trace(sector);

        Assert.Equal(2, loops.Count);
        Assert.All(loops, l => Assert.Equal(3, l.Vertices.Count));
        Assert.All(loops, l => Assert.True(l.IsClockwise));
    }

    [Fact]
    public void Trace_TwelveThinPetalsSharingOneVertex_ReturnsTwelveSeparateLoops()
    {
        var map = new MapData();
        var sector = map.CreateSector(0, 128);
        var center = map.CreateVertex(new Vector2(0, 0));

        const int petalCount = 12;
        const float petalWidthDegrees = 20f;
        const float spacingDegrees = 360f / petalCount;

        for (var i = 0; i < petalCount; i++)
        {
            var lowAngle = i * spacingDegrees * MathF.PI / 180f;
            var highAngle = (i * spacingDegrees + petalWidthDegrees) * MathF.PI / 180f;

            var low = map.CreateVertex(new Vector2(MathF.Cos(lowAngle), MathF.Sin(lowAngle)) * 10f);
            var high = map.CreateVertex(new Vector2(MathF.Cos(highAngle), MathF.Sin(highAngle)) * 10f);

            // Wound clockwise: center -> high angle -> low angle -> center.
            map.CreateLinedef(center, high, front: sector, back: null);
            map.CreateLinedef(high, low, front: sector, back: null);
            map.CreateLinedef(low, center, front: sector, back: null);
        }

        var loops = SectorTracer.Trace(sector);

        Assert.Equal(petalCount, loops.Count);
        Assert.All(loops, l => Assert.Equal(3, l.Vertices.Count));
        Assert.All(loops, l => Assert.True(l.IsClockwise));
    }

    [Fact]
    public void Trace_BranchWithBackSidedefCandidate_ClosesTheCorrectLoop()
    {
        var map = new MapData();
        var sector = map.CreateSector(0, 128);
        var otherSector = map.CreateSector(0, 128);

        var shared = map.CreateVertex(new Vector2(0, 0));
        var aLeft = map.CreateVertex(new Vector2(-10, 0));
        var aTop = map.CreateVertex(new Vector2(0, 10));
        var bRight = map.CreateVertex(new Vector2(10, 0));
        var bBottom = map.CreateVertex(new Vector2(0, -10));

        map.CreateLinedef(shared, aLeft, front: sector, back: null);
        map.CreateLinedef(aLeft, aTop, front: sector, back: null);
        map.CreateLinedef(aTop, shared, front: sector, back: null);

        // Two-sided: our sector is on the Back here, not the Front, so
        // this is the one candidate at "shared" that isn't a one-sided
        // Front edge - it must still be recognized as the correct closer.
        map.CreateLinedef(bRight, shared, front: otherSector, back: sector);
        map.CreateLinedef(bRight, bBottom, front: sector, back: null);
        map.CreateLinedef(bBottom, shared, front: sector, back: null);

        var loops = SectorTracer.Trace(sector);

        Assert.Equal(2, loops.Count);
        Assert.All(loops, l => Assert.Equal(3, l.Vertices.Count));
    }
}
