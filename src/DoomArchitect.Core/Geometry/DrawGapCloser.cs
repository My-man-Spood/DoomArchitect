using System.Linq;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Geometry;

/// <summary>
/// UDB's own real gap-closing search inside <c>Tools.DrawLines</c>: when a
/// drawn polyline is genuinely open (its own first and last points don't
/// coincide) but both ends themselves stitch onto existing geometry, UDB
/// tries to find a route *through* that existing geometry connecting the
/// two loose ends back together, so the draw can still close into a real
/// sector rather than staying open. Deliberately its own file, distinct
/// from <see cref="BoundaryTracer"/> (whose own <see cref="BoundaryTracer.Walk"/>/
/// <see cref="BoundaryTracer.FindClosestPath"/> this reuses as its actual
/// pathfinding primitive) and from <c>DrawLoopCommand</c> (which owns
/// turning a found path into real map mutations - this class only ever
/// searches, it never touches <see cref="MapData"/> at all).
///
/// This project keeps the original drawn <c>DrawPoint</c>'s own
/// <c>ExistingVertex</c>/<c>SplitLinedef</c> reference all the way through
/// to here, unlike UDB's own real <c>Tools.DrawLines</c> (a pure-geometry
/// function working only from already-resolved vertex positions, which has
/// to *re-discover* what a drawn endpoint stitches onto via a fresh
/// <c>MapSet.NearestLinedefRange</c>/<c>NearestVertexSquareRange</c>
/// distance search) - so this port uses that already-known reference
/// directly instead of re-deriving it, simpler and immune to a dense-area
/// distance search finding a different line/vertex than the one the user
/// actually clicked.
/// </summary>
public static class DrawGapCloser
{
    /// <summary>
    /// A found closing path, plus which end it actually starts from -
    /// UDB's own real <c>pathforward</c> (whether <c>shortestpath[0]</c>
    /// belongs to the *start* end's own candidates, not the end's) - the
    /// caller needs this to know which of the drawn polyline's own two
    /// ends the very first synthetic waypoint should connect onto.
    /// </summary>
    public readonly record struct Result(IReadOnlyList<LinedefSide> Path, bool Forward);

    /// <param name="firstLine">The drawn polyline's own first segment (its start side supplies the angle-sort reference when the start point stitches onto a vertex rather than a line).</param>
    /// <param name="startLinedef">The linedef the drawn polyline's own first point split/landed on, if any (its <c>DrawPoint.SplitLinedef</c>).</param>
    /// <param name="startVertex">The vertex the drawn polyline's own first point snapped onto, if any (its <c>DrawPoint.ExistingVertex</c>). At most one of this and <paramref name="startLinedef"/> is set.</param>
    /// <param name="lastLine">The drawn polyline's own last segment.</param>
    /// <param name="endLinedef">Same as <paramref name="startLinedef"/>, for the last point.</param>
    /// <param name="endVertex">Same as <paramref name="startVertex"/>, for the last point.</param>
    public static Result? FindClosingPath(
        Linedef firstLine, Linedef? startLinedef, Vertex? startVertex,
        Linedef lastLine, Linedef? endLinedef, Vertex? endVertex)
    {
        var startPoints = CandidatesAtEndpoint(firstLine, startLinedef, startVertex);
        var endPoints = CandidatesAtEndpoint(lastLine, endLinedef, endVertex);
        if (startPoints.Count == 0 || endPoints.Count == 0) return null;

        // UDB's own real fast paths for the common "closing back onto the
        // very thing you started from" case - skips the general search
        // entirely when both ends already stitch onto the exact same
        // line, or onto a line and one of that same line's own vertices.
        // Which direction the resulting single-entry path actually runs
        // (see this method's own final "Forward" determination below)
        // isn't assumed here even for these fast paths - matching UDB's
        // own real behavior of always re-checking pathforward against
        // startpoints afterward, not just for the general search result.
        IReadOnlyList<LinedefSide>? shortest;

        if (startLinedef != null && startLinedef == endLinedef)
        {
            shortest = new List<LinedefSide> { new(startLinedef, true) };
        }
        else if (startLinedef != null && endVertex != null && (startLinedef.Start == endVertex || startLinedef.End == endVertex))
        {
            shortest = new List<LinedefSide> { new(startLinedef, true) };
        }
        else if (endLinedef != null && startVertex != null && (endLinedef.Start == startVertex || endLinedef.End == startVertex))
        {
            shortest = new List<LinedefSide> { new(endLinedef, true) };
        }
        else
        {
            shortest = null;
            foreach (var startSide in startPoints)
            {
                foreach (var endSide in endPoints)
                {
                    var forward = BoundaryTracer.FindClosestPath(startSide.Linedef, startSide.Front, endSide.Linedef, endSide.Front);
                    if (forward != null && (shortest == null || forward.Count < shortest.Count)) shortest = forward;

                    var backward = BoundaryTracer.FindClosestPath(endSide.Linedef, endSide.Front, startSide.Linedef, startSide.Front);
                    if (backward != null && (shortest == null || backward.Count < shortest.Count)) shortest = backward;
                }
            }
        }

        if (shortest == null) return null;

        var forwardResult = startPoints.Any(p => p.Linedef == shortest[0].Linedef);
        return new Result(shortest, forwardResult);
    }

    /// <summary>
    /// Every candidate <see cref="LinedefSide"/> a closing path could
    /// legitimately start/end from at one of the drawn polyline's own
    /// endpoints - both sides of the stitched linedef directly if it
    /// landed on one, or (if it snapped onto a vertex instead) both sides
    /// of whichever of that vertex's own linedefs best continues the
    /// drawn line's own direction there, picked separately from each of
    /// that drawn line's own two possible facings (UDB's own real
    /// two-pass <c>LinedefAngleSorter</c> search, reusing this project's
    /// own already-tested <see cref="LinedefAngleSorter.SortByRelativeAngleDescending"/>
    /// rather than re-deriving UDB's own separate comparator's exact
    /// tie-break a second time - same "tightest turn" intent either way).
    /// Empty if the drawn point stitched onto nothing at all.
    /// </summary>
    private static List<LinedefSide> CandidatesAtEndpoint(Linedef drawnLine, Linedef? stitchLinedef, Vertex? stitchVertex)
    {
        var result = new List<LinedefSide>();

        if (stitchLinedef != null)
        {
            result.Add(new LinedefSide(stitchLinedef, true));
            result.Add(new LinedefSide(stitchLinedef, false));
            return result;
        }

        if (stitchVertex == null || stitchVertex.Linedefs.Count == 0) return result;

        AddBestAngleMatch(result, drawnLine, front: true, stitchVertex);
        AddBestAngleMatch(result, drawnLine, front: false, stitchVertex);
        return result;
    }

    private static void AddBestAngleMatch(List<LinedefSide> result, Linedef drawnLine, bool front, Vertex vertex)
    {
        var candidates = new List<LinedefSide>();
        foreach (var linedef in vertex.Linedefs)
        {
            if (linedef.Start == vertex) candidates.Add(new LinedefSide(linedef, true));
            if (linedef.End == vertex) candidates.Add(new LinedefSide(linedef, false));
        }

        if (candidates.Count == 0) return;

        LinedefAngleSorter.SortByRelativeAngleDescending(candidates, new LinedefSide(drawnLine, front), vertex);
        var winner = candidates[0].Linedef;
        result.Add(new LinedefSide(winner, true));
        result.Add(new LinedefSide(winner, false));
    }
}
