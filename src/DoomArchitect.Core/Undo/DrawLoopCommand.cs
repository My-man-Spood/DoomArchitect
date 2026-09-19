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
    private readonly List<Action> undoActions = new();

    public DrawLoopCommand(MapData map, IReadOnlyList<DrawPoint> points)
    {
        this.map = map;
        this.points = points;
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
        for (var i = 0; i < vertices.Count; i++)
        {
            var start = vertices[i];
            var end = vertices[(i + 1) % vertices.Count];
            var segment = CreateLinedefTracked(start, end);
            newLinedefs.Add(segment);

            GeometryStitcher.SplitAgainstExistingLines(map, segment, existingLinedefs, newLinedefs, newVertices, undoActions);
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
        foreach (var linedef in newLinedefs) ResolveInteriorSide(linedef, front: frontInterior[linedef]);
        foreach (var linedef in newLinedefs) ResolveExteriorSide(linedef, front: !frontInterior[linedef]);

        GeometryStitcher.FlipBackwardLinedefs(newLinedefs, undoActions);
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

    private void ResolveInteriorSide(Linedef linedef, bool front)
    {
        if ((front ? linedef.Front : linedef.Back) != null) return; // an earlier edge's own trace already covered this one

        var trace = BoundaryTracer.FindPotentialSectorAt(map, linedef, front);
        if (trace == null) return;

        var source = FindMatchingSidedefInTrace(trace) ?? FindOppositeSidedefInTrace(trace);
        CreateAndPopulateSector(trace, source);
    }

    private void ResolveExteriorSide(Linedef linedef, bool front)
    {
        if ((front ? linedef.Front : linedef.Back) != null) return;

        var trace = BoundaryTracer.FindPotentialSectorAt(map, linedef, front);
        if (trace == null) return;

        var target = FindMatchingSidedefInTrace(trace);
        if (target == null) return; // no neighbor to join onto - stays void, matching Phase 1's common case

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
