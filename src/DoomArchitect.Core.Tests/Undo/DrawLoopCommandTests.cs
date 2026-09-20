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

        // Net +3 linedefs: 4 new edges drawn (D->A duplicate, A->new1,
        // new1->new2, new2->D), one of which (the D->A duplicate) merges
        // back into a single surviving wall with the original bottomWall
        // via JoinOverlappingLines.
        Assert.Equal(linedefCountBefore + 3, map.Linedefs.Count);

        // Whichever single linedef now connects A and D - UDB's own real
        // JoinOverlappingLines/Linedef.Join merges the newly drawn
        // coincident edge and the original bottomWall into one survivor
        // (the newly drawn one, per UDB's own real "the line being
        // iterated survives" semantics - not necessarily bottomWall's
        // own original object identity, which this test deliberately
        // doesn't assume anymore).
        var sharedWall = map.Linedefs.Single(l =>
            (l.Start == d && l.End == a) || (l.Start == a && l.End == d));

        Assert.NotNull(sharedWall.Front);
        Assert.NotNull(sharedWall.Back);
        var newSector = map.Sectors.Single(s => s != originalSector);
        var sectors = new[] { sharedWall.Front!.Sector, sharedWall.Back!.Sector };
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
        Assert.Equal("-", sharedWall.Front!.MiddleTexture);
        Assert.Equal("-", sharedWall.Back!.MiddleTexture);
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

    /// <summary>
    /// The user's own real complaint that motivated the draw-then-stitch
    /// rewrite: a drawn edge that genuinely *crosses* an existing wall in
    /// its middle - not landing on a vertex, not snapped via
    /// <see cref="DrawPoint.OnLinedef"/>, just two plain
    /// <see cref="DrawPoint.AtNewPosition"/> points whose straight line
    /// between them happens to cross the wall. UDB's own real per-segment
    /// crossing pre-pass (<see cref="GeometryStitcher.SplitAgainstExistingLines"/>)
    /// is what has to catch this with zero help from the UI layer's own
    /// click-to-vertex/linedef snapping.
    /// </summary>
    [Fact]
    public void Do_DrawnLoopStraddlesAnExistingWallWithNoExplicitSnapping_SplitsItAtBothCrossings()
    {
        var map = new MapData();
        var (originalSector, box) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 100), new Vector2(100, 100), new Vector2(100, 0));
        originalSector.FloorTexture = "MYFLOOR";
        originalSector.CeilingTexture = "MYCEIL";
        var d = box[3]; // (100, 0)
        var a = box[0]; // (0, 0)
        var bottomWall = map.Linedefs.Single(l => l.Start == d && l.End == a);

        // A small rectangle straddling the bottom wall (y=0), from
        // y=-30 (void, below the box) to y=30 (inside the box's own
        // interior) - every point here is a plain new position, nothing
        // explicitly snapped onto bottomWall at all.
        var points = new[]
        {
            DrawPoint.AtNewPosition(new Vector2(40, -30)),
            DrawPoint.AtNewPosition(new Vector2(60, -30)),
            DrawPoint.AtNewPosition(new Vector2(60, 30)),
            DrawPoint.AtNewPosition(new Vector2(40, 30)),
        };
        var command = new DrawLoopCommand(map, points);

        command.Do();

        // bottomWall no longer spans the full [0, 100] range on its own -
        // it (and/or whatever survived joining with it) got split at both
        // x=40 and x=60.
        var bottomPieces = map.Linedefs
            .Where(l => l.Start.Position.Y == 0 && l.End.Position.Y == 0)
            .ToList();
        Assert.Equal(3, bottomPieces.Count);
        var xRanges = bottomPieces
            .Select(l => (Min: System.Math.Min(l.Start.Position.X, l.End.Position.X),
                Max: System.Math.Max(l.Start.Position.X, l.End.Position.X)))
            .OrderBy(r => r.Min)
            .ToList();
        Assert.Equal((0f, 40f), xRanges[0]);
        Assert.Equal((40f, 60f), xRanges[1]);
        Assert.Equal((60f, 100f), xRanges[2]);

        // The straddling rectangle's own top edge (y=30, entirely inside
        // the original box) never crosses any existing wall on its own,
        // but it still had to resolve its exterior side by joining
        // directly onto the original sector - the middle bottom piece
        // (now genuinely two-sided) is the clearest proof the whole
        // pipeline actually connected the new geometry to the old.
        var middlePiece = bottomPieces.Single(l =>
            System.Math.Min(l.Start.Position.X, l.End.Position.X) == 40f);
        Assert.NotNull(middlePiece.Front);
        Assert.NotNull(middlePiece.Back);
        var sectors = new[] { middlePiece.Front!.Sector, middlePiece.Back!.Sector };
        Assert.Contains(originalSector, sectors);
    }

    /// <summary>
    /// The exact "drawing over a vertex" case the user named directly:
    /// an existing T-junction vertex sitting on a wall, and a brand-new
    /// drawn line whose own path runs straight through that vertex's
    /// position without the user ever having explicitly clicked it (no
    /// <see cref="DrawPoint.AtExistingVertex"/> at all) - <see cref="GeometryStitcher.SplitLinesByVertices"/>
    /// (existing lines by new vertices direction) is what has to pick
    /// this up on its own, once the crossing pre-pass creates a new
    /// vertex exactly there.
    /// </summary>
    [Fact]
    public void Do_NewEdgePassesThroughAnExistingTJunctionVertexWithNoExplicitSnapping_SplitsBothWallsThere()
    {
        var map = new MapData();
        var (originalSector, box) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 100), new Vector2(100, 100), new Vector2(100, 0));
        var d = box[3]; // (100, 0)
        var a = box[0]; // (0, 0)
        var bottomWall = map.Linedefs.Single(l => l.Start == d && l.End == a);

        // An existing T-junction vertex at (50, 0), already part of the
        // map's own real geometry (splits the bottom wall in two) before
        // this loop is drawn at all.
        var junction = map.CreateVertex(new Vector2(50, 0));
        map.SplitLinedef(bottomWall, junction);
        var linedefCountBeforeDraw = map.Linedefs.Count;

        // Straddles the wall the same way as the test above, but
        // positioned so the two side edges cross exactly at the existing
        // junction vertex's own x position instead of some unrelated
        // point.
        var points = new[]
        {
            DrawPoint.AtNewPosition(new Vector2(30, -30)),
            DrawPoint.AtNewPosition(new Vector2(50, -30)),
            DrawPoint.AtNewPosition(new Vector2(50, 30)),
            DrawPoint.AtNewPosition(new Vector2(30, 30)),
        };
        var command = new DrawLoopCommand(map, points);

        command.Do();

        // The junction vertex itself was picked up (not duplicated) -
        // it's still the one and only vertex at (50, 0).
        var verticesAtJunctionPosition = map.Vertices.Count(v => v.Position == new Vector2(50, 0));
        Assert.Equal(1, verticesAtJunctionPosition);
        Assert.True(map.Linedefs.Count > linedefCountBeforeDraw);

        // Every linedef touching the junction vertex is a real, non-
        // degenerate segment (no zero-length lines left behind by a
        // mishandled split).
        Assert.All(junction.Linedefs, l => Assert.NotEqual(l.Start.Position, l.End.Position));
    }

    [Fact]
    public void Undo_DrawnLoopStraddlesAnExistingWallWithNoExplicitSnapping_FullyRestoresTheOriginalWall()
    {
        var map = new MapData();
        var (originalSector, box) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 100), new Vector2(100, 100), new Vector2(100, 0));
        var d = box[3];
        var a = box[0];
        var bottomWall = map.Linedefs.Single(l => l.Start == d && l.End == a);
        var vertexCountBefore = map.Vertices.Count;
        var linedefCountBefore = map.Linedefs.Count;
        var sectorCountBefore = map.Sectors.Count;

        var points = new[]
        {
            DrawPoint.AtNewPosition(new Vector2(40, -30)),
            DrawPoint.AtNewPosition(new Vector2(60, -30)),
            DrawPoint.AtNewPosition(new Vector2(60, 30)),
            DrawPoint.AtNewPosition(new Vector2(40, 30)),
        };
        var command = new DrawLoopCommand(map, points);
        command.Do();

        command.Undo();

        Assert.Equal(vertexCountBefore, map.Vertices.Count);
        Assert.Equal(linedefCountBefore, map.Linedefs.Count);
        Assert.Equal(sectorCountBefore, map.Sectors.Count);
        Assert.Same(bottomWall, map.Linedefs.SingleOrDefault(l => l.Start == d && l.End == a));
        Assert.Null(bottomWall.Back);
        var loop = Assert.Single(SectorTracer.Trace(originalSector));
        Assert.Equal(4, loop.Vertices.Count);
    }

    /// <summary>
    /// Phase 3: a genuinely open (non-closed) polyline - UDB's own real
    /// <c>Tools.DrawLines</c> supports drawing a raw, unclosed line with
    /// no sector-fill attempt at all, unlike Phase 1/2's own
    /// always-wraps-to-the-first-point behavior. A single 2-point segment
    /// touching nothing existing at all never resolves any side on either
    /// interior/exterior trace (a dangling line is a dead end both ways),
    /// so <c>sidesCreated</c> stays false and UDB's own real cleanup rule
    /// (only remove sideless leftovers once *something* in the draw did
    /// get a real sector) correctly leaves it in the map rather than
    /// deleting it.
    /// </summary>
    [Fact]
    public void Do_OpenTwoPointPolylineTouchingNothing_LeavesARawSidelessLinedefInTheMap()
    {
        var map = new MapData();
        var points = new[]
        {
            DrawPoint.AtNewPosition(new Vector2(0, 0)),
            DrawPoint.AtNewPosition(new Vector2(64, 0)),
        };
        var command = new DrawLoopCommand(map, points, closeLoop: false);

        command.Do();

        var linedef = Assert.Single(map.Linedefs);
        Assert.Null(linedef.Front);
        Assert.Null(linedef.Back);
        Assert.Empty(map.Sectors);
    }

    [Fact]
    public void Undo_OpenTwoPointPolylineTouchingNothing_RemovesEverythingItCreated()
    {
        var map = new MapData();
        var points = new[]
        {
            DrawPoint.AtNewPosition(new Vector2(0, 0)),
            DrawPoint.AtNewPosition(new Vector2(64, 0)),
        };
        var command = new DrawLoopCommand(map, points, closeLoop: false);
        command.Do();

        command.Undo();

        Assert.Empty(map.Linedefs);
        Assert.Empty(map.Vertices);
    }

    /// <summary>
    /// An open polyline whose two ends both stitch onto the same existing
    /// sector's own walls splits it in two - the common real "divide this
    /// room with one new wall" operation, and the case UDB's own real
    /// "splitting only" check exists for in the first place (this segment's
    /// own center point lands inside the original sector's already-
    /// occupied interior). Both halves border the original sector's own
    /// untouched walls on their own exterior trace, so this doesn't
    /// decisively exercise the "never conjure a sector out of the void
    /// while splitting-only" suppression on its own (see
    /// <see cref="DrawLoopCommand"/>'s own remarks on that gate) - it does
    /// confirm the surrounding open-polyline resolution machinery
    /// (interior/exterior trace, sideless cleanup) works end-to-end for a
    /// real sector split.
    /// </summary>
    [Fact]
    public void Do_OpenPolylineSplittingAnExistingSector_CreatesATwoSidedDividingWallAndTwoSectors()
    {
        var map = new MapData();
        var (originalSector, box) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 100), new Vector2(100, 100), new Vector2(100, 0));
        originalSector.FloorTexture = "MYFLOOR";
        var leftWall = map.Linedefs.Single(l =>
            (l.Start == box[0] && l.End == box[1]) || (l.Start == box[1] && l.End == box[0]));
        var rightWall = map.Linedefs.Single(l =>
            (l.Start == box[2] && l.End == box[3]) || (l.Start == box[3] && l.End == box[2]));

        var points = new[]
        {
            DrawPoint.OnLinedef(leftWall, new Vector2(0, 50)),
            DrawPoint.OnLinedef(rightWall, new Vector2(100, 50)),
        };
        var command = new DrawLoopCommand(map, points, closeLoop: false);

        command.Do();

        var dividingWall = map.Linedefs.Single(l =>
            (l.Start.Position == new Vector2(0, 50) && l.End.Position == new Vector2(100, 50))
            || (l.Start.Position == new Vector2(100, 50) && l.End.Position == new Vector2(0, 50)));
        Assert.NotNull(dividingWall.Front);
        Assert.NotNull(dividingWall.Back);

        Assert.Equal(2, map.Sectors.Count);
        var newSector = map.Sectors.Single(s => s != originalSector);
        Assert.Equal("MYFLOOR", newSector.FloorTexture); // inherited from the split original
    }

    [Fact]
    public void Undo_OpenPolylineSplittingAnExistingSector_FullyRestoresTheOriginalSector()
    {
        var map = new MapData();
        var (originalSector, box) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 100), new Vector2(100, 100), new Vector2(100, 0));
        var leftWall = map.Linedefs.Single(l =>
            (l.Start == box[0] && l.End == box[1]) || (l.Start == box[1] && l.End == box[0]));
        var rightWall = map.Linedefs.Single(l =>
            (l.Start == box[2] && l.End == box[3]) || (l.Start == box[3] && l.End == box[2]));
        var vertexCountBefore = map.Vertices.Count;
        var linedefCountBefore = map.Linedefs.Count;

        var points = new[]
        {
            DrawPoint.OnLinedef(leftWall, new Vector2(0, 50)),
            DrawPoint.OnLinedef(rightWall, new Vector2(100, 50)),
        };
        var command = new DrawLoopCommand(map, points, closeLoop: false);
        command.Do();

        command.Undo();

        Assert.Equal(vertexCountBefore, map.Vertices.Count);
        Assert.Equal(linedefCountBefore, map.Linedefs.Count);
        Assert.Single(map.Sectors);
        var loop = Assert.Single(SectorTracer.Trace(originalSector));
        Assert.Equal(4, loop.Vertices.Count);
    }

    [Fact]
    public void Undo_NewEdgePassesThroughAnExistingTJunctionVertexWithNoExplicitSnapping_FullyRestoresTheOriginalGeometry()
    {
        var map = new MapData();
        var (originalSector, box) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 100), new Vector2(100, 100), new Vector2(100, 0));
        var d = box[3];
        var a = box[0];
        var bottomWall = map.Linedefs.Single(l => l.Start == d && l.End == a);

        var junction = map.CreateVertex(new Vector2(50, 0));
        map.SplitLinedef(bottomWall, junction);
        var vertexCountBefore = map.Vertices.Count;
        var linedefCountBefore = map.Linedefs.Count;

        var points = new[]
        {
            DrawPoint.AtNewPosition(new Vector2(30, -30)),
            DrawPoint.AtNewPosition(new Vector2(50, -30)),
            DrawPoint.AtNewPosition(new Vector2(50, 30)),
            DrawPoint.AtNewPosition(new Vector2(30, 30)),
        };
        var command = new DrawLoopCommand(map, points);
        command.Do();

        command.Undo();

        Assert.Equal(vertexCountBefore, map.Vertices.Count);
        Assert.Equal(linedefCountBefore, map.Linedefs.Count);
        Assert.Contains(junction, map.Vertices);
        var loop = Assert.Single(SectorTracer.Trace(originalSector));
        Assert.Equal(5, loop.Vertices.Count); // 4 box corners + the junction vertex
    }

    /// <summary>
    /// Phase 3: <c>SplitOuterSectors</c> - a real, if unusual, pre-existing
    /// map state (one sector spanning two entirely disconnected islands -
    /// <see cref="MapDataTestExtensions.CreateClosedBoundary"/>'s own real
    /// use is normally a *hole*, but attaching a second same-winding outer
    /// loop instead produces exactly this) shouldn't get touched by a
    /// draw operation that has nothing to do with it: splitting the FIRST
    /// island alone with a plain diagonal correctly carves out its own new
    /// interior triangle (the ordinary <c>ResolveInteriorSide</c> path,
    /// unrelated to <c>SplitOuterSectors</c>), while the original sector -
    /// still spanning the untouched second island plus this split's own
    /// exterior remnant - is deliberately left alone, since none of the
    /// second island's own sides were themselves drawn by this operation
    /// (matching UDB's own real "only split what this draw actually
    /// touched" rule).
    /// </summary>
    [Fact]
    public void Do_DiagonalSplitInsideOnlyOneIslandOfAPreExistingMultiIslandSector_LeavesTheUntouchedIslandAlone()
    {
        var map = new MapData();
        var (sector, box1) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0));
        map.CreateClosedBoundary(sector, new Vector2(200, 0), new Vector2(200, 64), new Vector2(264, 64), new Vector2(264, 0));

        var a = box1[0]; // (0, 0)
        var c = box1[2]; // (64, 64)

        var points = new[] { DrawPoint.AtExistingVertex(a), DrawPoint.AtExistingVertex(c) };
        var command = new DrawLoopCommand(map, points, closeLoop: false);

        command.Do();

        Assert.Equal(2, map.Sectors.Count); // the original (still multi-island) sector, plus one new interior triangle
        Assert.Contains(sector, map.Sectors);
    }
}
