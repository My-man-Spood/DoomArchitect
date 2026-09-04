using System.Numerics;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Geometry;

/// <summary>
/// Ported from Ultimate Doom Builder's <c>SectorBuilder</c>/<c>Triangulation</c>
/// tracing step: a sector owns no vertices of its own, so its boundary
/// loop(s) are reconstructed by walking its sidedefs. A front sidedef is
/// walked Start-to-End; a back sidedef is walked End-to-Start - either way,
/// the sector being traced ends up on the walker's right at every step.
/// </summary>
public static class SectorTracer
{
    public static IReadOnlyList<Loop> Trace(Sector sector)
    {
        var remaining = new HashSet<Sidedef>(sector.Sidedefs);
        RemoveSelfReferencingSidedefs(remaining);

        var ignoredStarts = new HashSet<Vertex>();
        var loops = new List<Loop>();

        while (remaining.Count > 0)
        {
            var start = FindRightmostVertex(remaining, ignoredStarts);
            if (start == null) break;

            var path = TracePath(start, sector, remaining);
            if (path == null)
            {
                ignoredStarts.Add(start);
                continue;
            }

            foreach (var sidedef in path) remaining.Remove(sidedef);

            loops.Add(new Loop(path.Select(EnteringVertex).ToArray()));
        }

        return loops;
    }

    /// <summary>
    /// A linedef whose front and back sidedef both belong to the sector
    /// being traced (a real mapping trick - e.g. fake glass or deep
    /// water) contributes no boundary of its own and must be excluded up
    /// front, or its two sides would offer contradictory next-edges at
    /// both of its endpoints.
    /// </summary>
    private static void RemoveSelfReferencingSidedefs(HashSet<Sidedef> sidedefs)
    {
        sidedefs.RemoveWhere(sidedef =>
        {
            var other = sidedef.IsFront ? sidedef.Linedef.Back : sidedef.Linedef.Front;
            return other != null && other.Sector == sidedef.Sector;
        });
    }

    private static Vertex EnteringVertex(Sidedef sidedef) =>
        sidedef.IsFront ? sidedef.Linedef.Start : sidedef.Linedef.End;

    private static Vertex ExitingVertex(Sidedef sidedef) =>
        sidedef.IsFront ? sidedef.Linedef.End : sidedef.Linedef.Start;

    private static Vertex? FindRightmostVertex(IReadOnlyCollection<Sidedef> remaining, ISet<Vertex> ignore)
    {
        Vertex? best = null;
        foreach (var sidedef in remaining)
        {
            foreach (var vertex in new[] { sidedef.Linedef.Start, sidedef.Linedef.End })
            {
                if (ignore.Contains(vertex)) continue;
                if (best == null || vertex.Position.X > best.Position.X) best = vertex;
            }
        }
        return best;
    }

    /// <summary>
    /// Traces one closed loop starting and ending at <paramref name="start"/>.
    /// Ported from UDB's <c>DoTracePath</c>: recursive with backtracking -
    /// candidates at a branch are tried in order (best-angle first per
    /// <see cref="RelativeAngle"/>), and if a branch's continuation
    /// eventually dead-ends, the next candidate is tried instead. Matches
    /// UDB exactly in one respect that looks like a bug but isn't: a
    /// sidedef visited along a dead-end branch stays marked visited even
    /// after backtracking past it, rather than being un-marked. For any
    /// simple (non-self-intersecting) sector this never costs a solution -
    /// it just avoids re-exploring ground already known to fail.
    /// </summary>
    private static List<Sidedef>? TracePath(Vertex start, Sector sector, ISet<Sidedef> remaining)
    {
        var visited = new HashSet<Sidedef>();
        var history = new List<Sidedef>();

        return TraceFrom(start, start, sector, remaining, visited, null, history) ? history : null;
    }

    private static bool TraceFrom(
        Vertex findMe, Vertex fromHere, Sector sector, ISet<Sidedef> remaining,
        HashSet<Sidedef> visited, Sidedef? arrivedVia, List<Sidedef> history)
    {
        if (fromHere == findMe && history.Count > 0) return true;

        var candidates = CandidatesAt(fromHere, sector, remaining, visited);
        if (arrivedVia != null && candidates.Count > 1) SortByRelativeAngleDescending(candidates, arrivedVia, fromHere);

        foreach (var candidate in candidates)
        {
            visited.Add(candidate);
            history.Add(candidate);

            if (TraceFrom(findMe, ExitingVertex(candidate), sector, remaining, visited, candidate, history)) return true;

            history.RemoveAt(history.Count - 1);
        }

        return false;
    }

    private static List<Sidedef> CandidatesAt(Vertex vertex, Sector sector, ISet<Sidedef> remaining, ISet<Sidedef> visited)
    {
        var candidates = new List<Sidedef>();

        foreach (var linedef in vertex.Linedefs)
        {
            if (linedef.Front != null && linedef.Front.Sector == sector && linedef.Start == vertex
                && remaining.Contains(linedef.Front) && !visited.Contains(linedef.Front))
            {
                candidates.Add(linedef.Front);
            }

            if (linedef.Back != null && linedef.Back.Sector == sector && linedef.End == vertex
                && remaining.Contains(linedef.Back) && !visited.Contains(linedef.Back))
            {
                candidates.Add(linedef.Back);
            }
        }

        return candidates;
    }

    /// <summary>
    /// Ported from UDB's <c>SidedefAngleSorter</c>, for a vertex where more
    /// than one of the sector's own sidedefs meet (a sector touching
    /// itself at a single point). Orders candidates so the one that best
    /// continues the loop just walked (largest angle relative to the edge
    /// just arrived on) is tried first - backtracking in
    /// <see cref="TraceFrom"/> falls through to the rest in order if it
    /// turns out wrong.
    /// </summary>
    private static void SortByRelativeAngleDescending(List<Sidedef> candidates, Sidedef baseSidedef, Vertex baseVertex)
    {
        candidates.Sort((x, y) =>
            RelativeAngle(baseSidedef, y, baseVertex).CompareTo(RelativeAngle(baseSidedef, x, baseVertex)));
    }

    private static float RelativeAngle(Sidedef baseSidedef, Sidedef candidate, Vertex baseVertex)
    {
        var baseLine = baseSidedef.Linedef;
        var candidateLine = candidate.Linedef;

        var baseAngle = GeometryMath.Angle(baseLine.Start.Position, baseLine.End.Position);
        if (baseLine.End == baseVertex) baseAngle += MathF.PI;

        var candidateAngle = GeometryMath.Angle(candidateLine.Start.Position, candidateLine.End.Position);
        if (candidateLine.End == baseVertex) candidateAngle += MathF.PI;

        var n = GeometryMath.AngleDifference(baseAngle, candidateAngle);

        var baseFar = baseLine.Start == baseVertex ? baseLine.End.Position : baseLine.Start.Position;
        var candidateFar = candidateLine.Start == baseVertex ? candidateLine.End.Position : candidateLine.Start.Position;

        var dir = baseSidedef.IsFront;
        if (baseLine.End == baseVertex) dir = !dir;

        var s = GeometryMath.SideOfLine(baseFar, candidateFar, baseVertex.Position);
        if (s < 0 && dir) n = MathF.PI * 2f - n;
        if (s > 0 && !dir) n = MathF.PI * 2f - n;

        return n;
    }
}
