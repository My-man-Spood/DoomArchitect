using System.Linq;
using System.Numerics;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Geometry;

/// <summary>
/// Walks a closed boundary of <see cref="LinedefSide"/>s starting from
/// one edge/side, over the map's raw vertex/linedef topology (every
/// linedef touching each vertex, old and newly-drawn alike - no
/// special-casing). This is genuinely new geometry work, distinct from
/// <see cref="SectorTracer"/>: that class only ever walks a single
/// already-built <see cref="Sector"/>'s own already-assigned sidedefs;
/// this one discovers a *candidate* boundary before any sidedef/sector
/// necessarily exists on either side of it, which is exactly what Draw
/// mode's "join onto/inherit from a neighboring sector" step needs.
///
/// The outer/hole validation reuses this project's own already-built,
/// already-tested <see cref="Loop"/> (point-in-polygon containment) and
/// <see cref="PolygonNesting"/> (recursive nesting) rather than a
/// second, parallel polygon class.
///
/// <see cref="FindOuterLines"/> retries from a different starting edge
/// (found via a rightward ray-cast from the wrongly-traced loop's own
/// right-most vertex) when its first attempted trace turns out to be an
/// inner loop rather than the true outer boundary, rather than simply
/// failing (see its own remarks for the full algorithm).
/// </summary>
public static class BoundaryTracer
{
    private const int MaxTraceCountAtDeadEnd = 3;
    private const int MaxPathLength = 4096;
    private const int MaxOuterRetries = 4096;

    /// <summary>
    /// The full boundary a new sector (or an existing one being joined)
    /// would have on <paramref name="front"/>'s side of
    /// <paramref name="startLinedef"/> - the outer loop plus every hole
    /// nested inside it, as one flat list (only this flat list survives
    /// past the trace/validation step, never the loop/tree structure
    /// used to find it). <c>null</c> if no valid closed boundary could
    /// be found.
    /// </summary>
    public static IReadOnlyList<LinedefSide>? FindPotentialSectorAt(MapData map, Linedef startLinedef, bool front)
    {
        var start = new LinedefSide(startLinedef, front);
        var outer = FindOuterLines(map, start);
        if (outer == null) return null;

        var allLines = new List<LinedefSide>(outer.Value.Lines);
        FindInnerLines(map, outer.Value.Loop, allLines);
        return allLines;
    }

    /// <summary>
    /// Walks from <paramref name="startLinedef"/>'s own
    /// <paramref name="startFront"/> side until it reaches
    /// <paramref name="endLinedef"/>'s own <paramref name="endFront"/>
    /// side, over the map's raw vertex/linedef topology (the same walk
    /// <see cref="FindPotentialSectorAt"/>'s own self-closing trace
    /// already uses via <see cref="Walk"/> - that one is just this same
    /// method's <c>start == end</c> special case). Used by
    /// <see cref="DrawGapCloser"/> to route a genuinely open drawn
    /// polyline's own two loose ends back together *through* existing map
    /// geometry, as opposed to the "crosses existing lines along its own
    /// straight path" case the stitch pass already handles. <c>null</c>
    /// if no such path exists.
    /// </summary>
    public static IReadOnlyList<LinedefSide>? FindClosestPath(Linedef startLinedef, bool startFront, Linedef endLinedef, bool endFront, bool turnAtEnds = true) =>
        Walk(new LinedefSide(startLinedef, startFront), new LinedefSide(endLinedef, endFront), turnAtEnds);

    /// <summary>
    /// Per-linedef interior/exterior determination - geometry-driven,
    /// not a global clockwise/counterclockwise polygon-winding shortcut,
    /// which only ever works for a single simple polygon and breaks down
    /// once stitching can produce a self-touching or multiply-connected
    /// shape. Front is interior if tracing from the front side finds a
    /// genuinely valid, self-containing boundary
    /// (<see cref="FindPotentialSectorAt"/> already does the trace +
    /// build-loop + validate-own-side-point-is-contained work this
    /// needs); otherwise falls back to checking whether the *back* side's
    /// own trace is valid instead - if it is, front is not interior; if
    /// neither side traces to a valid boundary, defaults to front being
    /// interior.
    ///
    /// One flagged simplification: UDB's own fallback check is subtly
    /// different - it tests whether the *front* side's own point falls
    /// inside the *back* trace's own polygon, not merely whether the back
    /// trace is independently valid. Ported here as "does the back trace
    /// succeed at all" instead, reusing <see cref="FindPotentialSectorAt"/>'s
    /// own already-correct containment validation rather than re-deriving
    /// a second, parallel polygon-area/intersection routine - should be
    /// behaviorally equivalent in practice, not verified byte-for-byte
    /// against every possible pathological shape.
    /// </summary>
    public static bool DetermineFrontInterior(MapData map, Linedef linedef)
    {
        if (FindPotentialSectorAt(map, linedef, front: true) != null) return true;
        return FindPotentialSectorAt(map, linedef, front: false) == null;
    }

    /// <summary>
    /// Traces from <paramref name="start"/> and validates the result is
    /// actually the outer boundary containing <paramref name="start"/>'s
    /// own side-point, not some other loop the walk happened to close on:
    /// when a trace closes on the wrong loop (an inner/hole boundary
    /// rather than the true outer one containing <paramref name="start"/>'s
    /// own side-point), it doesn't just fail - it retries from a
    /// different starting edge, found by
    /// casting a ray rightward from the wrongly-traced loop's own
    /// right-most vertex to the next linedef it crosses, and continues
    /// scanning rightward from there until either a trace succeeds or the
    /// ray runs off the edge of the map with nothing left to cross.
    /// <paramref name="start"/>'s own side-point stays fixed for every
    /// retry - only which edge/side the trace itself starts from changes.
    /// </summary>
    private static (IReadOnlyList<LinedefSide> Lines, Loop Loop)? FindOuterLines(MapData map, LinedefSide start)
    {
        var sidePoint = SidePoint(start);
        var scan = start;

        // MaxOuterRetries is a defensive safety net: UDB itself retries
        // unboundedly here, trusting real map geometry to always
        // terminate. Same reasoning as Walk's own MaxPathLength,
        // guaranteeing termination on pathological/malformed input
        // instead of hanging.
        for (var retry = 0; retry < MaxOuterRetries; retry++)
        {
            var path = Walk(scan, scan, turnAtEnds: true);
            if (path == null) return null;

            var lines = TrimClosingDuplicate(path);
            var loop = BuildLoop(lines);

            if (loop != null && loop.Contains(sidePoint)) return (lines, loop);

            var rightmost = FindRightmostVertex(lines);
            var crossing = FindNextLinedefToTheRight(map, rightmost);
            if (crossing == null) return null;

            scan = new LinedefSide(crossing, GeometryMath.SideOfLine(crossing.Start.Position, crossing.End.Position, rightmost.Position) < 0);
        }

        return null;
    }

    /// <summary>The right-most vertex touched by any line in a (wrongly-traced) loop - the seed for the rightward ray-cast retry below.</summary>
    private static Vertex FindRightmostVertex(IReadOnlyList<LinedefSide> lines)
    {
        Vertex best = lines[0].Linedef.Start;

        foreach (var side in lines)
        {
            if (side.Linedef.Start.Position.X > best.Position.X) best = side.Linedef.Start;
            if (side.Linedef.End.Position.X > best.Position.X) best = side.Linedef.End;
        }

        return best;
    }

    /// <summary>
    /// A rightward ray-cast: the closest linedef (by crossing X, strictly
    /// to the right of <paramref name="from"/>) that crosses the
    /// horizontal ray running rightward from <paramref name="from"/> -
    /// "all sectors are closed" is the assumption this relies on (the
    /// very next thing the ray hits is necessarily a real boundary edge
    /// to continue tracing from). A tie (two lines crossing at the same
    /// X) prefers whichever is closer to parallel with the x-axis -
    /// UDB's own relative-angle tie-break, approximated here directly via
    /// each candidate's own acute angle from horizontal rather than
    /// re-derived byte-for-byte (a genuinely rare exact-tie case).
    /// </summary>
    private static Linedef? FindNextLinedefToTheRight(MapData map, Vertex from)
    {
        Linedef? best = null;
        var bestCrossX = float.MaxValue;

        foreach (var linedef in map.Linedefs)
        {
            if (linedef.Start.Position.X <= from.Position.X && linedef.End.Position.X <= from.Position.X) continue;
            if (!TryGetHorizontalCrossingX(linedef, from.Position, out var crossX)) continue;
            if (crossX <= from.Position.X + 0.00001f) continue;

            if (crossX < bestCrossX - 0.0001f)
            {
                best = linedef;
                bestCrossX = crossX;
            }
            else if (best != null && MathF.Abs(crossX - bestCrossX) <= 0.0001f && AngleFromHorizontal(linedef) < AngleFromHorizontal(best))
            {
                best = linedef;
                bestCrossX = crossX;
            }
        }

        return best;
    }

    /// <summary>Where <paramref name="linedef"/>'s own bounded segment crosses the horizontal line through <paramref name="from"/>, if at all (parallel-to-the-ray lines never cross it at a single point).</summary>
    private static bool TryGetHorizontalCrossingX(Linedef linedef, Vector2 from, out float crossX)
    {
        var a = linedef.Start.Position;
        var b = linedef.End.Position;

        if (a.Y == b.Y)
        {
            crossX = 0f;
            return false;
        }

        var t = (from.Y - a.Y) / (b.Y - a.Y);
        if (t is < 0f or > 1f)
        {
            crossX = 0f;
            return false;
        }

        crossX = a.X + (b.X - a.X) * t;
        return true;
    }

    private static float AngleFromHorizontal(Linedef linedef)
    {
        var delta = linedef.End.Position - linedef.Start.Position;
        var angle = MathF.Abs(MathF.Atan2(delta.Y, delta.X));
        return angle > MathF.PI / 2f ? MathF.PI - angle : angle;
    }

    /// <summary>
    /// Finds every hole nested inside <paramref name="outerLoop"/> by
    /// repeatedly tracing from the right-most as-yet-unclaimed vertex
    /// strictly inside it. Appends each valid hole's lines into
    /// <paramref name="allLines"/>.
    /// </summary>
    private static void FindInnerLines(MapData map, Loop outerLoop, List<LinedefSide> allLines)
    {
        var claimed = new HashSet<Linedef>(allLines.Select(l => l.Linedef));
        var ignoredStarts = new HashSet<Vertex>();

        while (true)
        {
            var candidate = FindUnclaimedVertexInside(map, outerLoop, claimed, ignoredStarts);
            if (candidate == null) return;

            var startSide = FindOutwardSide(candidate);
            if (startSide == null)
            {
                ignoredStarts.Add(candidate);
                continue;
            }

            var path = Walk(startSide.Value, startSide.Value, turnAtEnds: true);
            var lines = path == null ? null : TrimClosingDuplicate(path);
            var loop = lines == null ? null : BuildLoop(lines);

            // A real hole's silhouette encloses solid space on its
            // *outside* - the side-point of the line we started from must
            // therefore land outside the traced ring itself.
            if (loop == null || loop.Contains(SidePoint(startSide.Value)))
            {
                ignoredStarts.Add(candidate);
                continue;
            }

            allLines.AddRange(lines!);
            foreach (var side in lines!) claimed.Add(side.Linedef);
        }
    }

    /// <summary>
    /// The right-most vertex, strictly inside <paramref name="outerLoop"/>,
    /// that still has at least one linedef not already accounted for in
    /// <paramref name="claimed"/> - a candidate seed for another hole to
    /// discover. Searches every vertex in the map, not just ones touching
    /// already-claimed linedefs.
    /// </summary>
    private static Vertex? FindUnclaimedVertexInside(MapData map, Loop outerLoop, HashSet<Linedef> claimed, HashSet<Vertex> ignore)
    {
        // A vertex that's part of the outer loop's own boundary can still
        // have other, unclaimed linedefs attached (e.g. a box split by one
        // new diagonal - the diagonal's own endpoints still touch the
        // box's other, unused sides) without being a real interior hole
        // seed - excluded explicitly rather than trusted to point-in-
        // polygon containment, which is ambiguous for a point sitting
        // exactly on the polygon's own boundary.
        var outerVertices = new HashSet<Vertex>(outerLoop.Vertices);
        Vertex? best = null;

        foreach (var vertex in map.Vertices)
        {
            if (ignore.Contains(vertex) || outerVertices.Contains(vertex)) continue;
            if (vertex.Linedefs.Count == 0) continue;
            if (vertex.Linedefs.All(claimed.Contains)) continue;
            if (!outerLoop.Contains(vertex.Position)) continue;
            if (best == null || vertex.Position.X > best.Position.X) best = vertex;
        }

        return best;
    }

    /// <summary>
    /// The deterministic hole-trace starting side, at the right-most
    /// as-yet-unclaimed vertex found inside the outer loop - *not*
    /// whichever of its linedefs happens to come first in
    /// <see cref="Vertex.Linedefs"/> (this project's own earlier,
    /// incorrect approach): since every linedef touching this vertex
    /// necessarily points away from it (it's the right-most point in
    /// this whole region), a plain test point 100 units to its own
    /// *right* is guaranteed to sit outside whatever loop gets traced
    /// from here, regardless of which side of that linedef the walk
    /// starts from - this matches UDB's own <c>FindInnerLines</c>
    /// exactly. The wrong-side-first-found bug this fixes: an existing,
    /// already-partially-owned island sitting inside a brand-new loop
    /// (e.g. a pillar's own boundary, its interior side already
    /// belonging to the pillar's own sector, when a new sector is drawn
    /// as a border around it) could pick that already-occupied interior
    /// side as the "start", which the very next containment check
    /// correctly rejects as not a real hole - but with no fallback, the
    /// whole island was then silently skipped rather than retried from
    /// its own genuinely unclaimed exterior side, leaving the new
    /// sector's own boundary un-holed and creating a mesh that
    /// physically overlaps the very island it was drawn around.
    /// </summary>
    private static LinedefSide? FindOutwardSide(Vertex vertex)
    {
        const float targetAngle = MathF.PI / 2f;

        Linedef? found = null;
        var bestDelta = float.MaxValue;

        foreach (var linedef in vertex.Linedefs)
        {
            var lineAngle = GeometryMath.Angle(linedef.Start.Position, linedef.End.Position);
            if (linedef.End == vertex) lineAngle += MathF.PI;

            var delta = GeometryMath.AngleDifference(targetAngle, lineAngle);
            if (found == null || delta < bestDelta)
            {
                found = linedef;
                bestDelta = delta;
            }
        }

        if (found == null) return null;

        var testPosition = vertex.Position + new Vector2(100f, 0f);
        var front = GeometryMath.SideOfLine(found.Start.Position, found.End.Position, testPosition) < 0f;
        return new LinedefSide(found, front);
    }

    /// <summary>
    /// Walks from <paramref name="start"/> until it returns to the exact
    /// <paramref name="end"/> side (for a self-closing trace, <c>end ==
    /// start</c>). At each step, picks the candidate with the largest
    /// <see cref="LinedefAngleSorter.RelativeAngle"/> (the tightest real
    /// turn), preferring a less-traced candidate on a tie to avoid
    /// oscillating between the same two edges forever. A dead end (no
    /// candidate other than the edge just arrived on) flips to that same
    /// linedef's other side and keeps walking, capped at
    /// <see cref="MaxTraceCountAtDeadEnd"/> visits to any one linedef
    /// before giving up - matching UDB's own <c>turnatends</c>/
    /// trace-count-cap behavior. <see cref="MaxPathLength"/> is an
    /// additional defensive safety net (UDB itself has no such cap,
    /// trusting real map geometry to terminate on its own), guaranteeing
    /// termination on pathological input rather than hanging.
    /// </summary>
    private static List<LinedefSide>? Walk(LinedefSide start, LinedefSide end, bool turnAtEnds)
    {
        var path = new List<LinedefSide> { start };
        var traceCount = new Dictionary<Linedef, int> { [start.Linedef] = 1 };
        var current = start;

        while (true)
        {
            var vertex = current.Front ? current.Linedef.End : current.Linedef.Start;
            var candidates = CandidatesAt(vertex, current.Linedef);

            LinedefSide next;
            if (candidates.Count == 0)
            {
                if (!turnAtEnds || traceCount.GetValueOrDefault(current.Linedef) >= MaxTraceCountAtDeadEnd) return null;
                next = new LinedefSide(current.Linedef, !current.Front);
            }
            else
            {
                LinedefAngleSorter.SortByRelativeAngleDescending(candidates, current, vertex);
                next = candidates[0];

                // Never second-guess a natural pick that's the start/end
                // line itself - the start line's own trace count is never
                // zero (it's already 1 from being the very first entry in
                // the path), so without this exception the tie-break below
                // would always prefer *some* fresh alternative over the
                // one edge that would actually close the loop, and the
                // walk would never terminate on real, valid geometry.
                if (next.Linedef != start.Linedef && next.Linedef != end.Linedef)
                {
                    foreach (var candidate in candidates)
                    {
                        if (traceCount.GetValueOrDefault(candidate.Linedef) < traceCount.GetValueOrDefault(next.Linedef))
                        {
                            next = candidate;
                            break;
                        }
                    }
                }
            }

            path.Add(next);
            traceCount[next.Linedef] = traceCount.GetValueOrDefault(next.Linedef) + 1;

            if (next.Linedef == end.Linedef && next.Front == end.Front) return path;

            current = next;
            if (path.Count > MaxPathLength) return null;
        }
    }

    /// <summary>Every side starting exactly at <paramref name="vertex"/>, excluding <paramref name="arrivedVia"/> itself (re-tried explicitly only at a true dead end).</summary>
    private static List<LinedefSide> CandidatesAt(Vertex vertex, Linedef arrivedVia)
    {
        var candidates = new List<LinedefSide>();

        foreach (var linedef in vertex.Linedefs)
        {
            if (linedef == arrivedVia) continue;
            if (linedef.Start == vertex) candidates.Add(new LinedefSide(linedef, true));
            if (linedef.End == vertex) candidates.Add(new LinedefSide(linedef, false));
        }

        return candidates;
    }

    /// <summary>
    /// <see cref="Walk"/>'s returned path always ends by re-adding the
    /// starting side (that's how it detects it closed the loop) - every
    /// consumer needs the real, deduplicated edge list instead, or the
    /// starting edge would be processed twice downstream.
    /// </summary>
    private static List<LinedefSide> TrimClosingDuplicate(List<LinedefSide> path) =>
        path.Count > 0 ? path.GetRange(0, path.Count - 1) : path;

    private static Loop? BuildLoop(List<LinedefSide> lines)
    {
        if (lines.Count < 3) return null;

        var vertices = lines.Select(side => side.Front ? side.Linedef.Start : side.Linedef.End).ToList();
        return new Loop(vertices);
    }

    /// <summary>A point just off to the side <paramref name="side"/> represents, used to test which traced loop actually contains it.</summary>
    internal static Vector2 SidePoint(LinedefSide side)
    {
        const float offset = 0.1f;

        var a = side.Linedef.Start.Position;
        var b = side.Linedef.End.Position;
        var mid = (a + b) / 2f;
        var direction = Vector2.Normalize(b - a);
        var rightNormal = new Vector2(direction.Y, -direction.X);

        return side.Front ? mid + rightNormal * offset : mid - rightNormal * offset;
    }
}
