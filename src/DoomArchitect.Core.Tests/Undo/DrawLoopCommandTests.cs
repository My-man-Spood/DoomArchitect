using System.Linq;
using System.Numerics;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Tests.Map;
using DoomArchitect.Core.Undo;

namespace DoomArchitect.Core.Tests.Undo;

public class DrawLoopCommandTests
{
    private static readonly Vector2[] ClockwiseSquare =
    {
        new(0, 0), new(0, 64), new(64, 64), new(64, 0),
    };

    private static DrawLoopCommand StandaloneCommand(MapData map, IReadOnlyList<Vector2> positions) =>
        new(map, positions.Select(DrawPoint.AtNewPosition).ToList());

    [Fact]
    public void Do_StandaloneLoop_CreatesOneSelfContainedSectorMatchingPhase1Defaults()
    {
        var map = new MapData();
        var command = StandaloneCommand(map, ClockwiseSquare);

        command.Do();

        var sector = Assert.Single(map.Sectors);
        Assert.Equal(4, sector.Sidedefs.Count);
        Assert.Equal(DrawLoopCommand.DefaultFloorTexture, sector.FloorTexture);
        Assert.Equal(DrawLoopCommand.DefaultCeilingTexture, sector.CeilingTexture);
        Assert.Equal(DrawLoopCommand.DefaultBrightness, sector.Brightness);
        Assert.All(map.Linedefs, l =>
        {
            Assert.Same(sector, l.Front?.Sector);
            Assert.Null(l.Back);
            Assert.Equal(DrawLoopCommand.DefaultWallTexture, l.Front!.MiddleTexture);
        });
    }

    [Fact]
    public void Undo_StandaloneLoop_RemovesEverythingItCreated()
    {
        var map = new MapData();
        var command = StandaloneCommand(map, ClockwiseSquare);
        command.Do();

        command.Undo();

        Assert.Empty(map.Sectors);
        Assert.Empty(map.Vertices);
        Assert.Empty(map.Linedefs);
    }

    [Fact]
    public void Redo_StandaloneLoop_AfterUndo_ProducesAnEquivalentSectorAgain()
    {
        var map = new MapData();
        var command = StandaloneCommand(map, ClockwiseSquare);
        command.Do();
        command.Undo();

        command.Do();

        var sector = Assert.Single(map.Sectors);
        var loop = Assert.Single(SectorTracer.Trace(sector));
        Assert.Equal(4, loop.Vertices.Count);
        Assert.All(ClockwiseSquare, p => Assert.Contains(loop.Vertices, v => v.Position == p));
    }

    [Fact]
    public void Do_LoopSnappedOntoAnExistingVertex_ReusesItRatherThanCreatingADuplicate()
    {
        var map = new MapData();
        var (_, existingVertices) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));
        var sharedCorner = existingVertices[0]; // (0, 0)
        var vertexCountBefore = map.Vertices.Count;

        var points = new[]
        {
            DrawPoint.AtExistingVertex(sharedCorner),
            DrawPoint.AtNewPosition(new Vector2(-64, 0)),
            DrawPoint.AtNewPosition(new Vector2(-64, -64)),
            DrawPoint.AtNewPosition(new Vector2(0, -64)),
        };
        var command = new DrawLoopCommand(map, points);

        command.Do();

        // Only the 3 genuinely new points were created - the shared
        // corner was reused, not duplicated.
        Assert.Equal(vertexCountBefore + 3, map.Vertices.Count);
        // The 2 original box edges touching this corner, plus the 2 new
        // edges connecting it into the newly drawn loop (one to the next
        // point, one from the loop's own closing wraparound edge).
        Assert.Equal(4, sharedCorner.Linedefs.Count);
    }

    [Fact]
    public void Undo_LoopSnappedOntoAnExistingVertex_LeavesTheOriginalSectorFullyIntact()
    {
        var map = new MapData();
        var (originalSector, existingVertices) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));
        var sharedCorner = existingVertices[0];
        var originalLinedefCount = map.Linedefs.Count;

        var points = new[]
        {
            DrawPoint.AtExistingVertex(sharedCorner),
            DrawPoint.AtNewPosition(new Vector2(-64, 0)),
            DrawPoint.AtNewPosition(new Vector2(-64, -64)),
            DrawPoint.AtNewPosition(new Vector2(0, -64)),
        };
        var command = new DrawLoopCommand(map, points);
        command.Do();

        command.Undo();

        Assert.Contains(originalSector, map.Sectors);
        Assert.Equal(originalLinedefCount, map.Linedefs.Count);
        Assert.Contains(sharedCorner, map.Vertices);
        var loop = Assert.Single(SectorTracer.Trace(originalSector));
        Assert.Equal(4, loop.Vertices.Count);
    }

    [Fact]
    public void Do_LoopBulgingOutFromASplitPointOnAnExistingWall_SplitsTheWallIntoThreePieces()
    {
        var map = new MapData();
        var (_, box) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 100), new Vector2(100, 100), new Vector2(100, 0));
        var d = box[3]; // (100, 0)
        var a = box[0]; // (0, 0)
        var bottomWall = map.Linedefs.Single(l => l.Start == d && l.End == a);

        var points = new[]
        {
            DrawPoint.OnLinedef(bottomWall, new Vector2(50, 0)),
            DrawPoint.AtNewPosition(new Vector2(70, -30)),
            DrawPoint.AtNewPosition(new Vector2(30, -30)),
        };
        var command = new DrawLoopCommand(map, points);

        command.Do();

        // The original D->A wall is now two remnants (split once at the
        // one new point on it) plus the 3 brand-new triangle edges.
        var remnants = map.Linedefs.Where(l =>
            (l.Start.Position == d.Position || l.End.Position == d.Position
                || l.Start.Position == a.Position || l.End.Position == a.Position)
            && l.Start.Position.Y == 0 && l.End.Position.Y == 0).ToList();
        Assert.Equal(2, remnants.Count);

        // A brand-new, self-contained triangle sector was created for the bulge.
        Assert.Equal(2, map.Sectors.Count);
    }

    [Fact]
    public void Undo_LoopBulgingOutFromASplitPointOnAnExistingWall_FullyRestoresTheOriginalWall()
    {
        var map = new MapData();
        var (originalSector, box) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 100), new Vector2(100, 100), new Vector2(100, 0));
        var d = box[3];
        var a = box[0];
        var bottomWall = map.Linedefs.Single(l => l.Start == d && l.End == a);
        var vertexCountBefore = map.Vertices.Count;
        var linedefCountBefore = map.Linedefs.Count;

        var points = new[]
        {
            DrawPoint.OnLinedef(bottomWall, new Vector2(50, 0)),
            DrawPoint.AtNewPosition(new Vector2(70, -30)),
            DrawPoint.AtNewPosition(new Vector2(30, -30)),
        };
        var command = new DrawLoopCommand(map, points);
        command.Do();

        command.Undo();

        Assert.Equal(vertexCountBefore, map.Vertices.Count);
        Assert.Equal(linedefCountBefore, map.Linedefs.Count);
        Assert.Single(map.Sectors);
        Assert.Same(bottomWall, map.Linedefs.SingleOrDefault(l => l.Start == d && l.End == a));
        var loop = Assert.Single(SectorTracer.Trace(originalSector));
        Assert.Equal(4, loop.Vertices.Count);
    }

    /// <summary>
    /// A triangle bulging out from a *single* split point only ever
    /// touches the original box at that one vertex - it shares no actual
    /// edge with it, so there is nothing for it to inherit properties
    /// from or join onto (real UDB has this exact same limitation for a
    /// point-only touch). This is the correct, expected outcome, not the
    /// bug the user reported - see
    /// <see cref="Do_LoopSharingAWholeExistingWallByBothEndpoints_ReusesItAsATwoSidedWall"/>
    /// for the scenario that actually was broken (and is now fixed): two
    /// consecutive drawn points landing on the SAME existing edge, which
    /// should reuse that edge as a real, two-sided, property-inheriting
    /// wall rather than duplicate it.
    /// </summary>
    [Fact]
    public void Do_LoopBulgingOutFromASplitPointOnAnExistingWall_TouchesAtAPointOnlyWithNoSharedWall()
    {
        var map = new MapData();
        var (originalSector, box) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 100), new Vector2(100, 100), new Vector2(100, 0));
        originalSector.FloorTexture = "MYFLOOR";
        originalSector.CeilingTexture = "MYCEIL";
        originalSector.Brightness = 111;
        var d = box[3];
        var a = box[0];
        var bottomWall = map.Linedefs.Single(l => l.Start == d && l.End == a);

        var points = new[]
        {
            DrawPoint.OnLinedef(bottomWall, new Vector2(50, 0)),
            DrawPoint.AtNewPosition(new Vector2(70, -30)),
            DrawPoint.AtNewPosition(new Vector2(30, -30)),
        };
        var command = new DrawLoopCommand(map, points);

        command.Do();

        var newSector = map.Sectors.Single(s => s != originalSector);
        Assert.Equal(DrawLoopCommand.DefaultFloorTexture, newSector.FloorTexture);
        Assert.Equal(DrawLoopCommand.DefaultCeilingTexture, newSector.CeilingTexture);
        Assert.Equal(DrawLoopCommand.DefaultBrightness, newSector.Brightness);

        Assert.DoesNotContain(map.Linedefs, l => l.Front != null && l.Back != null);
    }

    /// <summary>
    /// The exact scenario the user reported broken: a new loop whose two
    /// consecutive points are *both* already-existing vertices of the
    /// same old wall, rather than a single split point. Before the
    /// reuse fix, this always created a brand-new, coincident duplicate
    /// linedef right on top of the old one instead of picking the old
    /// one up as a genuinely shared, two-sided wall.
    /// </summary>
    [Fact]
    public void Do_LoopSharingAWholeExistingWallByBothEndpoints_ReusesItAsATwoSidedWall()
    {
        var map = new MapData();
        var (originalSector, box) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 100), new Vector2(100, 100), new Vector2(100, 0));
        originalSector.FloorTexture = "MYFLOOR";
        originalSector.CeilingTexture = "MYCEIL";
        originalSector.Brightness = 111;
        var a = box[0]; // (0, 0)
        var d = box[3]; // (100, 0)
        var bottomWall = map.Linedefs.Single(l =>
            (l.Start == d && l.End == a) || (l.Start == a && l.End == d));
        bottomWall.Front!.MiddleTexture = "BIGDOOR2"; // a real solid texture, as a drawn one-sided wall would actually have
        var linedefCountBefore = map.Linedefs.Count;

        var points = new[]
        {
            DrawPoint.AtExistingVertex(d),
            DrawPoint.AtExistingVertex(a),
            DrawPoint.AtNewPosition(new Vector2(0, -50)),
            DrawPoint.AtNewPosition(new Vector2(100, -50)),
        };
        var command = new DrawLoopCommand(map, points);

        command.Do();

        // Only the 3 genuinely new edges were created - the shared D-A
        // wall was reused, not duplicated.
        Assert.Equal(linedefCountBefore + 3, map.Linedefs.Count);
        Assert.Same(bottomWall, map.Linedefs.SingleOrDefault(l =>
            (l.Start == d && l.End == a) || (l.Start == a && l.End == d)));

        Assert.NotNull(bottomWall.Front);
        Assert.NotNull(bottomWall.Back);
        var newSector = map.Sectors.Single(s => s != originalSector);
        var sectors = new[] { bottomWall.Front!.Sector, bottomWall.Back!.Sector };
        Assert.Contains(originalSector, sectors);
        Assert.Contains(newSector, sectors);

        Assert.Equal("MYFLOOR", newSector.FloorTexture);
        Assert.Equal("MYCEIL", newSector.CeilingTexture);
        Assert.Equal(111, newSector.Brightness);

        // Now genuinely two-sided - neither face's middle texture means
        // anything anymore, matching UDB's own real cleanup: the
        // pre-existing front's own old solid texture must be cleared,
        // not just the freshly created back side left with a leftover
        // default one.
        Assert.Equal("-", bottomWall.Front!.MiddleTexture);
        Assert.Equal("-", bottomWall.Back!.MiddleTexture);
    }

    /// <summary>
    /// The exact hard case the user asked for: a 128-unit sector's wall,
    /// with a new 60-unit sector sharing only the *middle* portion of it -
    /// two separate split points on the very same original wall, not just
    /// one. This needs two sequential splits of the same original
    /// <see cref="Linedef"/>, processed in the correct along-the-wall
    /// order regardless of the order the two points were drawn in -
    /// exercising a real, distinct bug from the single-split-point and
    /// whole-wall-reuse cases above.
    /// </summary>
    [Fact]
    public void Do_LoopSharingTheMiddlePortionOfAWiderExistingWall_SplitsItIntoThreeAndReusesTheMiddleSegment()
    {
        var map = new MapData();
        var (originalSector, box) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 128), new Vector2(128, 128), new Vector2(128, 0));
        originalSector.FloorTexture = "MYFLOOR";
        originalSector.CeilingTexture = "MYCEIL";
        originalSector.Brightness = 111;
        var d = box[3]; // (128, 0)
        var a = box[0]; // (0, 0)
        var bottomWall = map.Linedefs.Single(l => l.Start == d && l.End == a);
        var linedefCountBefore = map.Linedefs.Count;

        var points = new[]
        {
            DrawPoint.OnLinedef(bottomWall, new Vector2(94, 0)),
            DrawPoint.AtNewPosition(new Vector2(94, -60)),
            DrawPoint.AtNewPosition(new Vector2(34, -60)),
            DrawPoint.OnLinedef(bottomWall, new Vector2(34, 0)),
        };
        var command = new DrawLoopCommand(map, points);

        command.Do();

        // The original wall is now three clean, non-overlapping pieces
        // covering the full [0, 128] span exactly once - not a corrupted
        // overlap from the second split targeting an already-shrunk
        // segment.
        var bottomPieces = map.Linedefs
            .Where(l => l.Start.Position.Y == 0 && l.End.Position.Y == 0)
            .ToList();
        Assert.Equal(3, bottomPieces.Count);
        var xRanges = bottomPieces
            .Select(l => (Min: System.Math.Min(l.Start.Position.X, l.End.Position.X),
                Max: System.Math.Max(l.Start.Position.X, l.End.Position.X)))
            .OrderBy(r => r.Min)
            .ToList();
        Assert.Equal((0f, 34f), xRanges[0]);
        Assert.Equal((34f, 94f), xRanges[1]);
        Assert.Equal((94f, 128f), xRanges[2]);
        Assert.Equal(linedefCountBefore + 2 + 3, map.Linedefs.Count); // 2 extra wall pieces + 3 new triangle... square edges

        var middlePiece = map.Linedefs.Single(l =>
            (l.Start.Position == new Vector2(34, 0) && l.End.Position == new Vector2(94, 0))
            || (l.Start.Position == new Vector2(94, 0) && l.End.Position == new Vector2(34, 0)));

        Assert.NotNull(middlePiece.Front);
        Assert.NotNull(middlePiece.Back);
        var newSector = map.Sectors.Single(s => s != originalSector);
        var sectors = new[] { middlePiece.Front!.Sector, middlePiece.Back!.Sector };
        Assert.Contains(originalSector, sectors);
        Assert.Contains(newSector, sectors);

        Assert.Equal("MYFLOOR", newSector.FloorTexture);
        Assert.Equal("MYCEIL", newSector.CeilingTexture);
        Assert.Equal(111, newSector.Brightness);

        // The two flanking remnants of the original wall must stay
        // exactly as they were - one-sided, still belonging only to the
        // original sector.
        var flanking = bottomPieces.Where(l => l != middlePiece).ToList();
        Assert.All(flanking, l =>
        {
            Assert.NotNull(l.Front);
            Assert.Null(l.Back);
            Assert.Same(originalSector, l.Front!.Sector);
        });
    }

    [Fact]
    public void Undo_LoopSharingAWholeExistingWallByBothEndpoints_FullyRestoresTheOriginalWall()
    {
        var map = new MapData();
        var (originalSector, box) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 100), new Vector2(100, 100), new Vector2(100, 0));
        var a = box[0];
        var d = box[3];
        var bottomWall = map.Linedefs.Single(l =>
            (l.Start == d && l.End == a) || (l.Start == a && l.End == d));
        var vertexCountBefore = map.Vertices.Count;
        var linedefCountBefore = map.Linedefs.Count;

        var points = new[]
        {
            DrawPoint.AtExistingVertex(d),
            DrawPoint.AtExistingVertex(a),
            DrawPoint.AtNewPosition(new Vector2(0, -50)),
            DrawPoint.AtNewPosition(new Vector2(100, -50)),
        };
        var command = new DrawLoopCommand(map, points);
        command.Do();

        command.Undo();

        Assert.Equal(vertexCountBefore, map.Vertices.Count);
        Assert.Equal(linedefCountBefore, map.Linedefs.Count);
        Assert.Single(map.Sectors);
        Assert.Null(bottomWall.Back);
        Assert.NotNull(bottomWall.Front);
        Assert.Same(originalSector, bottomWall.Front!.Sector);
        var loop = Assert.Single(SectorTracer.Trace(originalSector));
        Assert.Equal(4, loop.Vertices.Count);
    }
}
