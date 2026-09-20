using System.Linq;
using System.Numerics;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Undo;

/// <summary>
/// One drawn point, resolved before <see cref="DrawLoopCommand"/> ever
/// touches the map: a brand-new position, a snap onto an already-existing
/// <see cref="Vertex"/>, or a point on an existing <see cref="Linedef"/>
/// that needs splitting there. Exactly one of <see cref="ExistingVertex"/>/
/// <see cref="SplitLinedef"/> is set, or neither (a plain new vertex).
/// </summary>
public readonly struct DrawPoint
{
    public static DrawPoint AtNewPosition(Vector2 position) => new(position, null, null);

    public static DrawPoint AtExistingVertex(Vertex vertex) => new(vertex.Position, vertex, null);

    public static DrawPoint OnLinedef(Linedef linedef, Vector2 position) => new(position, null, linedef);

    private DrawPoint(Vector2 position, Vertex? existingVertex, Linedef? splitLinedef)
    {
        Position = position;
        ExistingVertex = existingVertex;
        SplitLinedef = splitLinedef;
    }

    public Vector2 Position { get; }
    public Vertex? ExistingVertex { get; }
    public Linedef? SplitLinedef { get; }
}

/// <summary>
/// Draw mode's real command: builds a closed loop and - per edge -
/// either inherits an existing sector's properties into a brand-new one
/// bordering it (UDB's real <c>MakeSector</c>) or joins directly onto an
/// existing sector with no new sector at all (UDB's real <c>JoinSector</c>),
/// instead of Phase 1's always-standalone sector.
///
/// <c>Do()</c> follows UDB's own real <c>Tools.DrawLines</c> architecture
/// directly (re-derived from its source, not approximated): every
/// consecutive pair of resolved points always creates a brand-new
/// <see cref="Linedef"/> - there is no look-ahead reuse-detection here at
/// all (an earlier version of this command tried that, via a
/// <c>CreateOrReuseEdge</c> that only ever covered the one specific case
/// it was built for and was fundamentally unreliable for everything
/// else - drawing over an existing vertex, crossing an existing wall
/// without landing exactly on a split point, etc.). Instead, every new
/// edge is stitched against existing (and the loop's own other new)
/// geometry by a genuinely general reconciliation pass, ported directly
/// from UDB's own real stitching primitives
/// (<see cref="GeometryStitcher"/> - <c>JoinVertices</c> x2,
/// <c>SplitLinesByVertices</c> x2 directions, <c>RemoveLoopedLinedefs</c>,
/// <c>JoinOverlappingLines</c>, <c>FlipBackwardLinedefs</c>, plus
/// <c>DrawLines</c>' own per-segment existing-line-crossing pre-pass),
/// run in UDB's own real order. Only *after* that stitch pass does
/// interior/exterior resolution run, using UDB's own real per-linedef
/// geometry-driven interior determination
/// (<see cref="BoundaryTracer.DetermineFrontInterior"/>) rather than a
/// polygon-winding shortcut - necessary since the stitched result can be
/// a genuinely more complex shape than the single simple loop the user
/// physically drew.
///
/// One atomic command for the whole loop, matching UDB's own real "one
/// undo step per draw session". Internally records a small undo action
/// per individual mutation, in the exact order performed (by
/// <see cref="DrawLoopCommand"/>'s own helpers and by
/// <see cref="GeometryStitcher"/>'s, which append to the identical
/// shared list), and reverses them in <see cref="Undo"/>. Rebuilt fresh
/// on every <see cref="Do"/>, so this stays redo-safe.
///
/// Every edge's *interior* side always resolves via
/// <see cref="BoundaryTracer.FindPotentialSectorAt"/> into a brand-new
/// sector - <see cref="DefaultFloorTexture"/>/etc. if the traced boundary
/// borders nothing existing, or a full property copy from whichever
/// existing sector the trace finds first (matching side of the boundary
/// first, then the opposite side - UDB's own real ambiguous-neighbor
/// resolution order, not nearest/largest). Every edge's *exterior* side
/// either joins an existing neighbor sector directly (no new sector) or
/// stays void, exactly matching UDB's own real interior/exterior split.
///
/// A resolved trace can cover more than just this loop's own edges (a
/// shared wall, or an old sector's own untouched sidedefs when a drawn
/// line splits it) - every sidedef this command ends up creating along
/// the way, not just ones on this loop's own new edges, is tracked for
/// undo the same way.
///
/// One deliberate simplification versus UDB's real <c>JoinSector</c>: a
/// freshly created sidedef that's *staying* one-sided always gets
/// <see cref="DefaultWallTexture"/> rather than copying a neighboring
/// sidedef's own specific texture name first (UDB's own real
/// <c>TakeSidedefSettings</c> does try that before falling back to the
/// default) - flagged in TODO.md as a known gap, not silently dropped. A
/// freshly created sidedef that's *becoming* two-sided (the opposite
/// side already has a real sidedef) gets <c>"-"</c> instead, matching
/// UDB's own real behavior exactly - a plain two-sided wall's middle
/// texture has nothing to mean on either face once there's a real sector
/// on both sides.
/// </summary>
public sealed class DrawLoopCommand : ICommand
{
    public const double DefaultFloorHeight = 0;
    public const double DefaultCeilingHeight = 128;
    public const int DefaultBrightness = 192;
    public const string DefaultFloorTexture = "FLOOR0_1";
    public const string DefaultCeilingTexture = "CEIL1_1";
    public const string DefaultWallTexture = "STARTAN2";

    private readonly MapData map;
    private readonly IReadOnlyList<DrawPoint> points;
    private readonly bool closeLoop;
    private readonly List<Action> undoActions = new();

    /// <param name="map">The map this loop is drawn into.</param>
    /// <param name="points">The points drawn, in order.</param>
    /// <param name="closeLoop">
    /// Whether the last point wraps back around to the first, building a
    /// closed ring (Phase 1/2's own original, still-default behavior) -
    /// or an open polyline (UDB's own real, genuinely unclosed
    /// <c>Tools.DrawLines</c> result), which builds one fewer segment and
    /// never connects the last point back to the first at all. Decided by
    /// the caller (<see cref="DoomArchitect"/>-side <c>DrawOverlayHandler</c>):
    /// true for its own "clicked back near the first point" close
    /// gesture, false for a plain commit elsewhere - a deliberate
    /// simplification of UDB's own real detection (purely geometric,
    /// <c>firstline.Start == lastline.End</c> after resolution/stitching,
    /// with no gesture involved at all), flagged in TODO.md as a known
    /// gap rather than silently diverging: this project's own Draw mode
    /// never adds a literal duplicate closing point the way UDB's real
    /// <c>DrawPointAt</c> does, so there is no vertex-identity signal to
    /// detect closure from after the fact - only the gesture that
    /// triggered the commit says which was intended.
    /// </param>
    public DrawLoopCommand(MapData map, IReadOnlyList<DrawPoint> points, bool closeLoop = true)
    {
        this.map = map;
        this.points = points;
        this.closeLoop = closeLoop;
    }

    public void Do()
    {
        undoActions.Clear();

        // Snapshots of the map's state *before* this session touches
        // anything - UDB's own real "oldlines"/pre-draw vertex set, used
        // as the "existing" side of every stitch check below so a
        // freshly created segment never gets compared against its own
        // later siblings here.
        var existingLinedefs = map.Linedefs.ToList();
        var existingVertices = map.Vertices.ToList();

        var newVertices = new List<Vertex>();
        var vertices = ResolveVertices(newVertices);

        var newLinedefs = new List<Linedef>();
        var segmentCount = closeLoop ? vertices.Count : vertices.Count - 1;
        for (var i = 0; i < segmentCount; i++)
        {
            var start = vertices[i];
            var end = vertices[(i + 1) % vertices.Count];
            var segment = CreateLinedefTracked(start, end);
            newLinedefs.Add(segment);

            GeometryStitcher.SplitAgainstExistingLines(map, segment, existingLinedefs, newLinedefs, newVertices, undoActions);
        }

        // UDB's own real "splitting only" check (Tools.DrawLines, only
        // ever considered for a genuinely unclosed drawing - an already-
        // closed loop skips this entirely, matching UDB exactly): true
        // when any of this draw's own raw segments falls on the already-
        // occupied side of an existing linedef, meaning the user is
        // splitting an existing sector's interior rather than drawing a
        // fresh shape - read by ResolveInteriorSide below to stop a
        // genuinely-new sector from being conjured out of the void
        // alongside that split. Computed against the pre-draw snapshot,
        // before this draw's own new lines could shadow each other as
        // each other's own "nearest".
        var splittingOnly = !closeLoop && IsSplittingOnly(newLinedefs, existingLinedefs);

        // UDB's own real gap-closing: a genuinely open draw whose own two
        // loose ends both stitch onto existing geometry gets one more
        // chance to close, by routing *through* that existing geometry
        // (DrawGapCloser.FindClosingPath, UDB's own real
        // Tools.FindClosestPath-based search) rather than staying open -
        // never attempted at all while splittingOnly (matches UDB's own
        // real "only ever considered inside the not-splitting-only
        // branch"), and only possible with at least 2 points drawn (an
        // open single-point "polyline" isn't a real thing to begin with).
        if (!closeLoop && !splittingOnly && vertices.Count >= 2)
        {
            var closing = DrawGapCloser.FindClosingPath(
                newLinedefs[0], points[0].SplitLinedef, points[0].ExistingVertex,
                newLinedefs[^1], points[^1].SplitLinedef, points[^1].ExistingVertex);

            if (closing != null) AppendClosingPath(closing.Value, vertices, newLinedefs, newVertices);
        }

        // The real stitch pass (UDB's own real MapSet.StitchGeometry,
        // CLASSIC mode - Tools.DrawLines' own default), in its own real
        // order. SplitLinesByLines (new-vs-new crossing splitting) is
        // deliberately not run here - confirmed directly against UDB's
        // source to be a no-op in CLASSIC mode, not a gap on this
        // project's side.
        GeometryStitcher.JoinVerticesWithinSet(map, newVertices, GeometryStitcher.StitchDistance, undoActions);
        GeometryStitcher.JoinVerticesOntoExisting(map, existingVertices, newVertices, GeometryStitcher.StitchDistance, undoActions);
        GeometryStitcher.SplitLinesByVertices(map, newLinedefs, existingVertices, GeometryStitcher.StitchDistance, newLinedefs, undoActions);
        GeometryStitcher.SplitLinesByVertices(map, existingLinedefs, newVertices, GeometryStitcher.StitchDistance, newLinedefs, undoActions);
        GeometryStitcher.RemoveLoopedLinedefs(map, newLinedefs, undoActions);
        GeometryStitcher.JoinOverlappingLines(map, newLinedefs, undoActions);

        // UDB's own real per-linedef FrontInterior is computed once for
        // every line in the fully-stitched result *before* either
        // resolution pass runs, then only ever read (never recomputed)
        // by both passes - resolving interior first would otherwise
        // populate real sidedefs that could change what a later
        // DetermineFrontInterior call finds, corrupting the exterior
        // pass's own results.
        var frontInterior = newLinedefs.ToDictionary(l => l, l => BoundaryTracer.DetermineFrontInterior(map, l));

        // Interior first, always - an edge's exterior-side trace can
        // legitimately walk across an interior sidedef another edge just
        // created (a shared corner two of this loop's own edges meet at),
        // so every interior side needs to exist before any exterior side
        // is resolved.
        var sidesCreated = false;
        foreach (var linedef in newLinedefs) sidesCreated |= ResolveInteriorSide(linedef, front: frontInterior[linedef], splittingOnly);
        foreach (var linedef in newLinedefs) sidesCreated |= ResolveExteriorSide(linedef, front: !frontInterior[linedef]);

        GeometryStitcher.FlipBackwardLinedefs(newLinedefs, undoActions);

        // UDB's own real cleanup: a fully-unstitched open draw (nothing
        // in it ever resolved to a real sector anywhere) leaves its raw
        // sideless linedefs in the map rather than deleting them - a
        // deliberate raw-line drawing (into the void, touching nothing),
        // not a failed sector attempt. Only once *something* in this
        // draw did get a real sector (sidesCreated) are the leftover
        // sideless segments from that same draw actually cleaned up.
        if (sidesCreated)
        {
            for (var i = newLinedefs.Count - 1; i >= 0; i--)
            {
                if (newLinedefs[i].Front != null || newLinedefs[i].Back != null) continue;
                RemoveSidelessLinedefTracked(newLinedefs[i]);
            }
        }

        // UDB's own real SplitOuterSectors post-pass, run last (matching
        // UDB's own real invocation from DrawGeometryMode.OnAccept, after
        // Tools.DrawLines itself has fully finished) - see this method's
        // own remarks.
        SplitOuterSectors(newLinedefs);
    }

    public void Undo()
    {
        for (var i = undoActions.Count - 1; i >= 0; i--) undoActions[i]();
    }

    /// <summary>
    /// Resolves every point to a real <see cref="Vertex"/>, in the loop's
    /// own drawn order, appending every genuinely *new* vertex it creates
    /// (brand-new position or split point - never an already-existing
    /// one named via <see cref="DrawPoint.AtExistingVertex"/>) to
    /// <paramref name="newVertices"/> for the stitch pass that follows in
    /// <see cref="Do"/> to use.
    ///
    /// Points splitting a <see cref="Linedef"/> are special: two of them
    /// can name the very same original linedef (a new loop sharing only
    /// part of a wider existing wall needs a split at each end of the
    /// shared portion) - and <see cref="DrawPoint.SplitLinedef"/> always
    /// captures whichever linedef was hit at draw time, which for two
    /// points on the same wall is the exact same, not-yet-split object.
    /// Resolving those independently and naively (in drawn order, each
    /// calling <see cref="MapData.SplitLinedef"/> straight on that shared
    /// reference) breaks the moment the *first* split shrinks it: the
    /// second point's own position generally no longer lies on what that
    /// same object now represents, so the split silently produces
    /// overlapping, corrupted geometry instead of a clean three-way
    /// division of the wall. Grouping same-linedef points together and
    /// splitting them along-the-wall order (nearest
    /// <see cref="Linedef.Start"/> first, each split's own leftover far
    /// half becoming the next split's target) is what actually matches
    /// what the user physically drew, regardless of the order the points
    /// happen to appear in <see cref="points"/>.
    /// </summary>
    private List<Vertex> ResolveVertices(List<Vertex> newVertices)
    {
        var resolved = new Vertex[points.Count];
        var splitGroups = new Dictionary<Linedef, List<int>>();

        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            if (point.ExistingVertex != null) resolved[i] = point.ExistingVertex;
            else if (point.SplitLinedef == null) resolved[i] = CreateVertexTracked(point.Position, newVertices);
            else
            {
                if (!splitGroups.TryGetValue(point.SplitLinedef, out var indices))
                {
                    indices = new List<int>();
                    splitGroups[point.SplitLinedef] = indices;
                }

                indices.Add(i);
            }
        }

        foreach (var (originalLinedef, indices) in splitGroups)
        {
            var start = originalLinedef.Start.Position;
            indices.Sort((x, y) =>
                Vector2.DistanceSquared(start, points[x].Position)
                    .CompareTo(Vector2.DistanceSquared(start, points[y].Position)));

            var tail = originalLinedef;
            foreach (var index in indices)
            {
                var vertex = CreateVertexTracked(points[index].Position, newVertices);
                var splitTail = tail;
                var originalEnd = splitTail.End;
                var newHalf = map.SplitLinedef(splitTail, vertex);

                undoActions.Add(() =>
                {
                    map.RemoveLinedef(newHalf);
                    vertex.RemoveLinedef(splitTail);
                    splitTail.End = originalEnd;
                    originalEnd.AddLinedef(splitTail);
                });

                resolved[index] = vertex;
                tail = newHalf;
            }
        }

        return resolved.ToList();
    }

    private Vertex CreateVertexTracked(Vector2 position, List<Vertex> newVertices)
    {
        var vertex = map.CreateVertex(position);
        undoActions.Add(() => map.RemoveVertex(vertex));
        newVertices.Add(vertex);
        return vertex;
    }

    private Linedef CreateLinedefTracked(Vertex start, Vertex end)
    {
        var linedef = map.CreateLinedef(start, end, null, null);
        undoActions.Add(() => map.RemoveLinedef(linedef));
        return linedef;
    }

    /// <summary>
    /// UDB's own real "splitting only" gate: while <paramref name="splittingOnly"/>
    /// is set, a trace that borders nothing existing at all
    /// (<see cref="FindMatchingSidedefInTrace"/> finds nothing - a truly
    /// new sector out of the void) is skipped rather than created, so an
    /// open draw that's actually just splitting an existing sector's
    /// interior doesn't also conjure an unrelated new sector out of the
    /// void alongside that split. Returns whether a sector was actually
    /// created here, for the caller's own sideless-cleanup bookkeeping.
    /// </summary>
    private bool ResolveInteriorSide(Linedef linedef, bool front, bool splittingOnly)
    {
        if ((front ? linedef.Front : linedef.Back) != null) return false; // an earlier edge's own trace already covered this one

        var trace = BoundaryTracer.FindPotentialSectorAt(map, linedef, front);
        if (trace == null) return false;

        var matching = FindMatchingSidedefInTrace(trace);
        if (matching == null && splittingOnly) return false;

        CreateAndPopulateSector(trace, matching ?? FindOppositeSidedefInTrace(trace));
        return true;
    }

    /// <summary>Returns whether an existing neighbor sector was actually joined here, for the caller's own sideless-cleanup bookkeeping.</summary>
    private bool ResolveExteriorSide(Linedef linedef, bool front)
    {
        if ((front ? linedef.Front : linedef.Back) != null) return false;

        var trace = BoundaryTracer.FindPotentialSectorAt(map, linedef, front);
        if (trace == null) return false;

        var target = FindMatchingSidedefInTrace(trace);
        if (target == null) return false; // no neighbor to join onto - stays void, matching Phase 1's common case

        // Every side in the trace, not just ones still void - an old
        // sidedef in here already belongs to *some* sector (often
        // target.Sector itself, but not always: a drawn line splitting
        // an existing room needs that room's own untouched sidedefs
        // redistributed too, which AttachOrRetargetSidedefTracked's own
        // "already exists" branch handles by re-pointing rather than
        // leaving it stale).
        foreach (var side in trace)
        {
            AttachOrRetargetSidedefTracked(side.Linedef, side.Front, target.Sector);
        }

        return true;
    }

    /// <summary>UDB's own real Tools.DrawLines "splitting only" check - see this class's own remarks on <see cref="splittingOnly"/>'s use in <see cref="Do"/>.</summary>
    private static bool IsSplittingOnly(IReadOnlyList<Linedef> newLinedefs, IReadOnlyList<Linedef> existingLinedefs)
    {
        foreach (var linedef in newLinedefs)
        {
            var center = (linedef.Start.Position + linedef.End.Position) / 2f;
            var nearest = GeometryStitcher.FindNearestLinedef(existingLinedefs, center);
            if (nearest == null) continue;

            var side = GeometryMath.SideOfLine(nearest.Start.Position, nearest.End.Position, center);
            if (side < 0 && nearest.Front != null) return true;
            if (side > 0 && nearest.Back != null) return true;
        }

        return false;
    }

    private void RemoveSidelessLinedefTracked(Linedef linedef)
    {
        map.RemoveLinedef(linedef);
        undoActions.Add(() => map.RestoreLinedef(linedef));
    }

    /// <summary>
    /// UDB's own real <c>Tools.SplitOuterSectors</c> post-pass (invoked
    /// from <c>DrawGeometryMode.OnAccept</c> *after* <c>Tools.DrawLines</c>
    /// itself completes, gated by its own real <c>SplitJoinedSectors</c>
    /// setting - always run here, since this project has no settings
    /// system to gate it behind yet): when this draw's own touched
    /// sidedefs belong to a sector whose own polygon has become genuinely
    /// disconnected into multiple separate islands
    /// (<see cref="PolygonNesting.BuildTree"/> reporting more than one
    /// top-level root - UDB's own real <c>Sector.Triangles.IslandVertices.Count
    /// &gt; 1</c>), re-traces a fresh boundary from each touched side and
    /// carves out whichever piece traces to a strict subset of the
    /// sector's own sides, as long as at least one of the sides *left
    /// behind* was also drawn by this same operation (otherwise there's
    /// nothing this draw actually split - leave it alone). At most one new
    /// sector is carved per candidate sector per call, matching UDB's own
    /// real behavior exactly (it also only ever splits once per group,
    /// even if a sector ended up with 3+ disconnected islands).
    ///
    /// Two deliberate simplifications versus UDB's own real
    /// <c>MakeSector</c>/<c>SectorWasInvalid</c> machinery, flagged rather
    /// than guessed (their own exact source wasn't available to verify
    /// byte-for-byte): the newly carved sector copies its properties
    /// directly from the sector it's being split out of
    /// (<see cref="CopySectorProperties"/>, via <see cref="CreateAndPopulateSector"/> -
    /// matching this project's own already-established inheritance
    /// convention elsewhere in this class) rather than UDB's own separate
    /// trace-based property search; and a split that leaves the
    /// *original* sector with fewer than 3 sides of its own (genuinely
    /// degenerate) isn't specially disposed of here - narrower than UDB's
    /// own real <c>SectorWasInvalid</c> cleanup, a real gap flagged in
    /// TODO.md rather than silently dropped.
    /// </summary>
    private void SplitOuterSectors(IReadOnlyList<Linedef> drawnLinedefs)
    {
        var drawnSides = new HashSet<Sidedef>();
        var candidateSectors = new Dictionary<Sector, HashSet<Sidedef>>();

        foreach (var linedef in drawnLinedefs)
        {
            RegisterDrawnSide(linedef.Front, drawnSides, candidateSectors);
            RegisterDrawnSide(linedef.Back, drawnSides, candidateSectors);
        }

        foreach (var (sector, touchedSides) in candidateSectors)
        {
            if (sector.Sidedefs.Count == touchedSides.Count) continue; // every side of it was drawn - nothing left to split off

            foreach (var side in touchedSides)
            {
                if (side.Sector != sector) continue; // already carved off by an earlier candidate sector this same pass

                var trace = BoundaryTracer.FindPotentialSectorAt(map, side.Linedef, side.IsFront);
                if (trace == null || trace.Count == 0 || trace.Count >= sector.Sidedefs.Count) continue;

                var tracedSides = new HashSet<Sidedef>();
                foreach (var ls in trace)
                {
                    var tracedSide = ls.Front ? ls.Linedef.Front : ls.Linedef.Back;
                    if (tracedSide != null) tracedSides.Add(tracedSide);
                }

                var splitByThisDraw = sector.Sidedefs.Any(s => !tracedSides.Contains(s) && drawnSides.Contains(s));
                if (!splitByThisDraw) continue;

                CreateAndPopulateSector(trace, source: sector.Sidedefs.FirstOrDefault());
                break;
            }
        }
    }

    private static void RegisterDrawnSide(Sidedef? side, HashSet<Sidedef> drawnSides, Dictionary<Sector, HashSet<Sidedef>> candidateSectors)
    {
        if (side == null) return;
        drawnSides.Add(side);

        if (!IsMultiIsland(side.Sector)) return;

        if (!candidateSectors.TryGetValue(side.Sector, out var set))
        {
            set = new HashSet<Sidedef>();
            candidateSectors[side.Sector] = set;
        }

        set.Add(side);
    }

    private static bool IsMultiIsland(Sector sector) =>
        PolygonNesting.BuildTree(SectorTracer.Trace(sector)).Count > 1;

    /// <summary>
    /// Turns a found <see cref="DrawGapCloser.Result"/> into real map
    /// geometry - UDB's own real loop building one new vertex+linedef per
    /// waypoint along the path (skipping the path's own first entry,
    /// already represented by whichever drawn endpoint the path starts
    /// from), then one final edge closing directly onto the drawn
    /// polyline's *other* end. These synthetic vertices sit exactly on
    /// the existing linedefs/vertices the path traced along, so the
    /// ordinary stitch pass that runs right after this (still ahead in
    /// <see cref="Do"/>) is what actually merges them into that existing
    /// geometry - this method only ever adds new, not-yet-stitched raw
    /// edges, the same as the drawn polyline's own segments.
    /// </summary>
    private void AppendClosingPath(DrawGapCloser.Result closing, List<Vertex> vertices, List<Linedef> newLinedefs, List<Vertex> newVertices)
    {
        var current = closing.Forward ? vertices[0] : vertices[^1];

        for (var i = 1; i < closing.Path.Count; i++)
        {
            var side = closing.Path[i];
            var position = side.Front ? side.Linedef.Start.Position : side.Linedef.End.Position;
            var next = CreateVertexTracked(position, newVertices);
            newLinedefs.Add(CreateLinedefTracked(current, next));
            current = next;
        }

        var finalTarget = closing.Forward ? vertices[^1] : vertices[0];
        newLinedefs.Add(CreateLinedefTracked(current, finalTarget));
    }

    private void CreateAndPopulateSector(IReadOnlyList<LinedefSide> trace, Sidedef? source)
    {
        var floorHeight = source?.Sector.FloorHeight ?? DefaultFloorHeight;
        var ceilingHeight = source?.Sector.CeilingHeight ?? DefaultCeilingHeight;
        var newSector = map.CreateSector(floorHeight, ceilingHeight);
        undoActions.Add(() => map.RemoveSector(newSector));

        if (source != null) CopySectorProperties(source.Sector, newSector);
        else
        {
            newSector.FloorTexture = DefaultFloorTexture;
            newSector.CeilingTexture = DefaultCeilingTexture;
            newSector.Brightness = DefaultBrightness;
        }

        // Every side in the trace, not just ones still void - see
        // ResolveExteriorSide's own matching remarks; nothing already
        // points at newSector (it was just created), so every non-null
        // matching side here is an old sidedef genuinely being
        // redistributed onto this newly-formed sector, not merely
        // filled in.
        foreach (var side in trace)
        {
            AttachOrRetargetSidedefTracked(side.Linedef, side.Front, newSector);
        }
    }

    /// <summary>
    /// UDB's own real ambiguous-neighbor rule: first match walking the
    /// trace in order - the *matching* side of each entry (the side that
    /// entry's own <see cref="LinedefSide.Front"/> represents) first;
    /// <see cref="FindOppositeSidedefInTrace"/> checks the other side of
    /// each entry instead, interior's own real fallback second pass when
    /// nothing matching is found anywhere in the boundary.
    /// </summary>
    private static Sidedef? FindMatchingSidedefInTrace(IReadOnlyList<LinedefSide> trace)
    {
        foreach (var side in trace)
        {
            var sidedef = side.Front ? side.Linedef.Front : side.Linedef.Back;
            if (sidedef != null) return sidedef;
        }

        return null;
    }

    private static Sidedef? FindOppositeSidedefInTrace(IReadOnlyList<LinedefSide> trace)
    {
        foreach (var side in trace)
        {
            var sidedef = side.Front ? side.Linedef.Back : side.Linedef.Front;
            if (sidedef != null) return sidedef;
        }

        return null;
    }

    private static void CopySectorProperties(Sector from, Sector to)
    {
        to.FloorTexture = from.FloorTexture;
        to.CeilingTexture = from.CeilingTexture;
        to.Brightness = from.Brightness;
        foreach (var (key, value) in from.Fields) to.Fields[key] = value;
    }

    private void AttachOrRetargetSidedefTracked(Linedef linedef, bool front, Sector sector)
    {
        var existing = front ? linedef.Front : linedef.Back;

        if (existing == null)
        {
            var opposite = front ? linedef.Back : linedef.Front;
            var originalOppositeMiddle = opposite?.MiddleTexture;

            map.AttachOrRetargetSidedef(linedef, front, sector);
            var created = (front ? linedef.Front : linedef.Back)!;

            // A wall gaining its very first sidedef, with nothing on the
            // other side either, is staying one-sided - needs a real,
            // solid texture (Phase 1's own established default) or it'd
            // render as nothing at all. A wall whose *opposite* side
            // already exists is becoming two-sided by this exact call -
            // matches MapData.AttachOrRetargetSidedef's own real cleanup
            // of the opposite side's now-superfluous middle texture, just
            // applied here to this brand-new side instead: a plain
            // two-sided wall's middle has nothing to mean either, on
            // either face, once there's a real sector on both sides.
            created.MiddleTexture = opposite != null ? "-" : DefaultWallTexture;

            undoActions.Add(() =>
            {
                sector.RemoveSidedef(created);
                if (front) linedef.Front = null; else linedef.Back = null;
                if (opposite != null) opposite.MiddleTexture = originalOppositeMiddle!;
            });
        }
        else
        {
            var originalSector = existing.Sector;
            map.AttachOrRetargetSidedef(linedef, front, sector);

            undoActions.Add(() =>
            {
                sector.RemoveSidedef(existing);
                existing.Sector = originalSector;
                originalSector.AddSidedef(existing);
            });
        }
    }
}
