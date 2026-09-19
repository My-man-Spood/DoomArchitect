using System.Linq;
using System.Numerics;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Geometry;

/// <summary>
/// Ported from UDB's real <c>Tools.FindClosestPath</c>/<c>FindPotentialSectorAt</c>/
/// <c>FindOuterLines</c>/<c>FindInnerLines</c> - walks a closed boundary of
/// <see cref="LinedefSide"/>s starting from one edge/side, over the map's
/// raw vertex/linedef topology (every linedef touching each vertex, old
/// and newly-drawn alike - no special-casing, exactly matching UDB's own
/// uniform walk). This is genuinely new geometry work, distinct from
/// <see cref="SectorTracer"/>: that class only ever walks a single
/// already-built <see cref="Sector"/>'s own already-assigned sidedefs;
/// this one discovers a *candidate* boundary before any sidedef/sector
/// necessarily exists on either side of it, which is exactly what Draw
/// mode's "join onto/inherit from a neighboring sector" step needs.
///
/// The outer/hole validation reuses this project's own already-built,
/// already-tested <see cref="Loop"/> (point-in-polygon containment) and
/// <see cref="PolygonNesting"/> (recursive nesting) rather than porting a
/// second, parallel polygon class the way UDB's own separate
/// <c>EarClipPolygon</c> exists - both are constructible here since this
/// lives in the same <c>Core.Geometry</c> namespace/assembly
/// (<see cref="Loop"/>'s constructor is <c>internal</c>, not
/// <c>private</c>).
///
/// A known, deliberate simplification versus UDB's real algorithm: UDB's
/// own <c>FindOuterLines</c> retries from a different starting edge (found
/// via a rightward ray-cast) when its first attempted trace turns out to
/// be an inner loop rather than the true outer boundary. That retry step
/// is not implemented here yet - a trace that lands on the wrong loop on
/// its first attempt returns <c>null</c> (treated as "no potential
/// sector found here") rather than retrying. Flagged in TODO.md as a
/// known gap, not silently dropped.
/// </summary>
public static class BoundaryTracer
{
    private const int MaxTraceCountAtDeadEnd = 3;
    private const int MaxPathLength = 4096;

    /// <summary>
    /// The full boundary a new sector (or an existing one being joined)
    /// would have on <paramref name="front"/>'s side of
    /// <paramref name="startLinedef"/> - the outer loop plus every hole
    /// nested inside it, as one flat list (matching UDB's own real
    /// <c>alllines</c> - only this flat list survives past the trace/
    /// validation step, never the loop/tree structure used to find it).
    /// <c>null</c> if no valid closed boundary could be found.
    /// </summary>
    public static IReadOnlyList<LinedefSide>? FindPotentialSectorAt(MapData map, Linedef startLinedef, bool front)
    {
        var start = new LinedefSide(startLinedef, front);
        var outer = FindOuterLines(start);
        if (outer == null) return null;

        var allLines = new List<LinedefSide>(outer.Value.Lines);
        FindInnerLines(map, outer.Value.Loop, allLines);
        return allLines;
    }

    /// <summary>
    /// UDB's own real per-linedef interior/exterior determination
    /// (<c>Tools.DrawLines</c>' own "Determine drawing interior" step) -
    /// geometry-driven, not a global clockwise/counterclockwise polygon-
    /// winding shortcut, which only ever works for a single simple
    /// polygon and breaks down once stitching can produce a self-
    /// touching or multiply-connected shape. Front is interior if tracing
    /// from the front side finds a genuinely valid, self-containing
    /// boundary (<see cref="FindPotentialSectorAt"/> already does the
    /// trace + build-loop + validate-own-side-point-is-contained work
    /// this needs); otherwise falls back to checking whether the *back*
    /// side's own trace is valid instead - if it is, front is not
    /// interior; if neither side traces to a valid boundary, defaults to
    /// front being interior, matching UDB's own real default.
    ///
    /// One flagged simplification: UDB's own real fallback check is
    /// subtly different - it tests whether the *front* side's own point
    /// falls inside the *back* trace's own polygon, not merely whether
    /// the back trace is independently valid. Ported here as "does the
    /// back trace succeed at all" instead, reusing
    /// <see cref="FindPotentialSectorAt"/>'s own already-correct
    /// containment validation rather than re-deriving UDB's separate
    /// <c>EarClipPolygon.CalculateArea</c>/<c>Intersect</c> machinery a
    /// second time - should be behaviorally equivalent in practice, not
    /// verified byte-for-byte against every possible pathological shape.
    /// </summary>
    public static bool DetermineFrontInterior(MapData map, Linedef linedef)
    {
        if (FindPotentialSectorAt(map, linedef, front: true) != null) return true;
        return FindPotentialSectorAt(map, linedef, front: false) == null;
    }

    /// <summary>
    /// Traces from <paramref name="start"/> and validates the result is
    /// actually the outer boundary containing <paramref name="start"/>'s
    /// own side-point, not some other loop the walk happened to close on.
    /// </summary>
    private static (IReadOnlyList<LinedefSide> Lines, Loop Loop)? FindOuterLines(LinedefSide start)
    {
        var path = Walk(start, start, turnAtEnds: true);
        if (path == null) return null;

        var lines = TrimClosingDuplicate(path);
        var loop = BuildLoop(lines);
        if (loop == null) return null;

        var sidePoint = SidePoint(start);
        return loop.Contains(sidePoint) ? (lines, loop) : null;
    }

    /// <summary>
    /// Finds every hole nested inside <paramref name="outerLoop"/> by
    /// repeatedly tracing from the right-most as-yet-unclaimed vertex
    /// strictly inside it, matching UDB's own real <c>FindInnerLines</c>.
    /// Appends each valid hole's lines into <paramref name="allLines"/>.
    /// </summary>
    private static void FindInnerLines(MapData map, Loop outerLoop, List<LinedefSide> allLines)
    {
        var claimed = new HashSet<Linedef>(allLines.Select(l => l.Linedef));
        var ignoredStarts = new HashSet<Vertex>();

        while (true)
        {
            var candidate = FindUnclaimedVertexInside(map, outerLoop, claimed, ignoredStarts);
            if (candidate == null) return;

            var startSide = FirstOutgoingSide(candidate);
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
            // therefore land outside the traced ring itself, matching
            // UDB's own real check.
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
    /// discover. Searches every vertex in the map (not just ones touching
    /// already-claimed linedefs), matching UDB's own real "any as-yet-
    /// unclaimed map vertex" search.
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

    private static LinedefSide? FirstOutgoingSide(Vertex vertex)
    {
        foreach (var linedef in vertex.Linedefs)
        {
            if (linedef.Start == vertex) return new LinedefSide(linedef, true);
            if (linedef.End == vertex) return new LinedefSide(linedef, false);
        }

        return null;
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
    /// before giving up - matching UDB's own real <c>turnatends</c>/
    /// trace-count-cap behavior. <see cref="MaxPathLength"/> is a
    /// defensive safety net beyond what's confirmed of UDB's own real
    /// behavior, guaranteeing termination on pathological input rather
    /// than hanging.
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
    private static Vector2 SidePoint(LinedefSide side)
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
