using System.Numerics;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Geometry;

/// <summary>
/// A direct port of UDB's own real geometry-stitching primitives
/// (<c>MapSet.JoinVertices</c>/<c>SplitLinesByVertices</c>/
/// <c>RemoveLoopedLinedefs</c>/<c>JoinOverlappingLines</c>/
/// <c>FlipBackwardLinedefs</c>, plus <c>Tools.DrawLines</c>' own
/// per-segment existing-line-crossing pre-pass) - see the "Port UDB's
/// Real Geometry-Stitching Pipeline" plan for the full research this is
/// built from. Every method here is orchestration only: it searches the
/// map graph and calls <see cref="MapData"/>'s own public mutation
/// primitives (never touches an <c>internal</c> setter directly except
/// where <see cref="MapData"/> itself doesn't yet expose a primitive for
/// something this needs - <see cref="Linedef.Start"/>/<see cref="Linedef.End"/>/
/// <see cref="Vertex.AddLinedef"/>/<see cref="Vertex.RemoveLinedef"/>
/// reassignment for undo closures, matching the exact same pattern
/// <c>DrawLoopCommand</c>'s own existing <c>SplitLinedef</c> undo closure
/// already uses), matching how <see cref="BoundaryTracer"/>/
/// <see cref="SectorTracer"/> already stay orchestration-only rather than
/// duplicating <see cref="MapData"/>'s own mutation logic.
///
/// Every method takes a shared <c>undoActions</c> list to append its own
/// undo closures to, rather than returning a diff object - keeps
/// <c>DrawLoopCommand</c>'s existing "one atomic command, sequential undo
/// closures, reverse order on <c>Undo()</c>" pattern intact instead of
/// introducing a second undo mechanism just for stitching.
///
/// Deliberately not ported: UDB's own real <c>SplitLinesByLines</c> (new-
/// line-vs-new-line crossing splitting) - confirmed directly against its
/// source to be a complete no-op in <c>MergeGeometryMode.CLASSIC</c>,
/// which is <c>Tools.DrawLines</c>' own real default mode. A self-
/// intersecting drawn polygon genuinely isn't split by real UDB during a
/// normal draw either - not a gap on this project's side.
/// </summary>
public static class GeometryStitcher
{
    /// <summary>UDB's own real <c>MapSet.STITCH_DISTANCE</c> - float-precision-coincidence tolerance, not a visual snap radius (that's the separate, already-correct UI-layer click-to-vertex/linedef snap).</summary>
    public const float StitchDistance = 0.005f;

    /// <summary>UDB's own real <c>Tools.MINIMUM_INTERSECTION_DISTANCE</c> - already a *squared* distance threshold (UDB compares it directly against its own squared-distance helper), guarding against a spurious split when drawing nearly parallel to/along an existing line.</summary>
    public const float MinimumIntersectionDistanceSquared = 0.25f;

    /// <summary>
    /// UDB's own real per-segment pre-pass in <c>Tools.DrawLines</c>:
    /// splits <paramref name="segment"/> (a just-created new linedef) at
    /// every point it crosses an *existing* linedef that belongs to a
    /// sector - never touches the existing line itself yet (that happens
    /// later, once the resulting split vertices are folded into the
    /// general stitch pass via <see cref="SplitLinesByVertices"/>).
    /// Appends every newly created vertex to <paramref name="newVertices"/>
    /// and every newly split-off linedef half to
    /// <paramref name="newLinedefs"/> - <paramref name="segment"/> itself
    /// is assumed already present in <paramref name="newLinedefs"/> by
    /// the caller. UDB's own real version tracks these split vertices in
    /// a separate <c>mergeverts</c>/<c>intersectverts</c> set, distinct
    /// from its own broader <c>newverts</c> - a distinction that only
    /// matters because a UDB <c>DrawnVertex</c> can individually opt out
    /// of stitching (<c>stitch: false</c>); this project's own
    /// <see cref="Undo.DrawPoint"/> has no such per-point opt-out, so
    /// every new vertex is always stitch-eligible and one list serves
    /// both roles.
    /// </summary>
    public static void SplitAgainstExistingLines(
        MapData map, Linedef segment, IReadOnlyList<Linedef> existingLines,
        List<Linedef> newLinedefs, List<Vertex> newVertices, List<Action> undoActions)
    {
        var measureStart = segment.Start.Position;
        var measureEnd = segment.End.Position;

        var intersections = new List<(float U, Vector2 Point)>();
        foreach (var existing in existingLines)
        {
            if (existing == segment) continue;
            if (existing.Front == null && existing.Back == null) continue; // "belongs to a sector" - matches UDB's own map.Sectors/Sidedefs walk

            var existingStart = existing.Start.Position;
            var existingEnd = existing.End.Position;

            if (!GeometryMath.TryGetSegmentIntersection(existingStart, existingEnd, measureStart, measureEnd, out var u, out var point)) continue;
            if (u <= 0f || u >= 1f) continue; // exact-endpoint hits aren't real crossings

            var measureFarFromExisting =
                GeometryMath.DistanceToSegmentSquared(existingStart, existingEnd, measureStart) > MinimumIntersectionDistanceSquared ||
                GeometryMath.DistanceToSegmentSquared(existingStart, existingEnd, measureEnd) > MinimumIntersectionDistanceSquared;
            var existingFarFromMeasure =
                GeometryMath.DistanceToSegmentSquared(measureStart, measureEnd, existingStart) > MinimumIntersectionDistanceSquared ||
                GeometryMath.DistanceToSegmentSquared(measureStart, measureEnd, existingEnd) > MinimumIntersectionDistanceSquared;
            if (!(measureFarFromExisting && existingFarFromMeasure)) continue;

            intersections.Add((u, point));
        }

        if (intersections.Count == 0) return;

        intersections.Sort((a, b) => a.U.CompareTo(b.U));

        var tail = segment;
        foreach (var (_, point) in intersections)
        {
            var vertex = map.CreateVertex(point);
            undoActions.Add(() => map.RemoveVertex(vertex));
            newVertices.Add(vertex);

            var splitTail = tail;
            var originalEnd = splitTail.End;
            var newHalf = map.SplitLinedef(splitTail, vertex);
            undoActions.Add(() =>
            {
                map.RemoveLinedef(newHalf);
                vertex.RemoveLinedef(splitTail);
                splitTail.End = originalEnd;
                originalEnd.AddLinedef(splitTail);
                splitTail.MarkAdjacentSectorsDirty();
            });

            newLinedefs.Add(newHalf);
            tail = newHalf;
        }
    }

    /// <summary>UDB's own real <c>MapSet.JoinVertices(set1, set2, keepsecond: true, joindist)</c> - merges each of <paramref name="movingVertices"/> onto the nearest <paramref name="fixedVertices"/> within <paramref name="joinDistance"/>, discarding the moving one every time (this project's only real use case - stitching our own new vertices onto pre-existing ones, never the reverse).</summary>
    public static void JoinVerticesOntoExisting(MapData map, IReadOnlyList<Vertex> fixedVertices, List<Vertex> movingVertices, float joinDistance, List<Action> undoActions)
    {
        var joinDistanceSquared = joinDistance * joinDistance;
        bool joined;

        do
        {
            joined = false;
            foreach (var moving in movingVertices)
            {
                Vertex? nearest = null;
                foreach (var fixedVertex in fixedVertices)
                {
                    if (fixedVertex == moving) continue;
                    if (Vector2.DistanceSquared(moving.Position, fixedVertex.Position) <= joinDistanceSquared)
                    {
                        nearest = fixedVertex;
                        break;
                    }
                }

                if (nearest == null) continue;

                MergeAndRecordUndo(map, moving, nearest, undoActions);
                movingVertices.Remove(moving);
                joined = true;
                break;
            }
        }
        while (joined);
    }

    /// <summary>UDB's own real <c>MapSet.JoinVertices(List&lt;Vertex&gt; set, joindist)</c> - merges any two vertices within <paramref name="vertices"/> that end up within <paramref name="joinDistance"/> of each other (this loop's own new vertices coinciding with each other, e.g. two intersection splits landing on the same spot, or the loop closing back onto its own start).</summary>
    public static void JoinVerticesWithinSet(MapData map, List<Vertex> vertices, float joinDistance, List<Action> undoActions)
    {
        var joinDistanceSquared = joinDistance * joinDistance;
        bool joined;

        do
        {
            joined = false;
            for (var i = 0; i < vertices.Count && !joined; i++)
            {
                for (var j = i + 1; j < vertices.Count; j++)
                {
                    if (Vector2.DistanceSquared(vertices[i].Position, vertices[j].Position) > joinDistanceSquared) continue;

                    MergeAndRecordUndo(map, vertices[j], vertices[i], undoActions);
                    vertices.RemoveAt(j);
                    joined = true;
                    break;
                }
            }
        }
        while (joined);
    }

    private static void MergeAndRecordUndo(MapData map, Vertex discard, Vertex keep, List<Action> undoActions)
    {
        var affected = discard.Linedefs.Select(l => (Linedef: l, WasStart: l.Start == discard)).ToList();
        map.MergeVertex(discard, keep);

        undoActions.Add(() =>
        {
            foreach (var (linedef, wasStart) in affected)
            {
                keep.RemoveLinedef(linedef);
                if (wasStart) linedef.Start = discard; else linedef.End = discard;
                discard.AddLinedef(linedef);
                linedef.MarkAdjacentSectorsDirty();
            }

            map.RestoreVertex(discard);
        });
    }

    /// <summary>
    /// UDB's own real <c>MapSet.SplitLinesByVertices</c> (the parts
    /// relevant to <c>MergeGeometryMode.CLASSIC</c>, UDB's own
    /// <c>Tools.DrawLines</c> default - its own real <c>REPLACE</c>-mode
    /// sector-removal branch is out of scope here, unused by that call
    /// path): splits every line in <paramref name="lines"/> wherever any
    /// of <paramref name="vertices"/> sits exactly on it (within
    /// <paramref name="splitDistance"/>) without already being one of its
    /// endpoints. <paramref name="lines"/> itself grows as splits happen
    /// (a segment split off earlier in this same pass can itself need
    /// splitting again by a different vertex - matches UDB's own real
    /// "add the new line to the blockmap, keep checking" behavior).
    /// <paramref name="trackNewSegmentsInto"/> also receives every new
    /// split-off half - pass the same reference as <paramref name="lines"/>
    /// when splitting our own new lines by existing vertices (so nothing
    /// extra is needed), or the flat draw-session <c>newLinedefs</c> list
    /// when splitting a separate snapshot of *existing* lines by our own
    /// new vertices (their split-off pieces become part of this draw
    /// session's own changed geometry too).
    /// </summary>
    public static void SplitLinesByVertices(
        MapData map, List<Linedef> lines, IReadOnlyList<Vertex> vertices, float splitDistance,
        List<Linedef> trackNewSegmentsInto, List<Action> undoActions)
    {
        var splitDistanceSquared = splitDistance * splitDistance;

        foreach (var vertex in vertices)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line.Start == vertex || line.End == vertex) continue;
                if (GeometryMath.DistanceToSegmentSquared(line.Start.Position, line.End.Position, vertex.Position) > splitDistanceSquared) continue;

                var originalEnd = line.End;
                var newHalf = map.SplitLinedef(line, vertex);
                var splitLine = line;
                undoActions.Add(() =>
                {
                    map.RemoveLinedef(newHalf);
                    vertex.RemoveLinedef(splitLine);
                    splitLine.End = originalEnd;
                    originalEnd.AddLinedef(splitLine);
                    splitLine.MarkAdjacentSectorsDirty();
                });

                lines.Add(newHalf);
                if (!ReferenceEquals(trackNewSegmentsInto, lines)) trackNewSegmentsInto.Add(newHalf);
            }
        }
    }

    /// <summary>UDB's own real <c>MapSet.RemoveLoopedLinedefs</c> - drops any line in <paramref name="lines"/> whose two endpoints are the same vertex (or the same position).</summary>
    public static void RemoveLoopedLinedefs(MapData map, List<Linedef> lines, List<Action> undoActions)
    {
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var line = lines[i];
            if (line.Start != line.End && line.Start.Position != line.End.Position) continue;

            lines.RemoveAt(i);
            map.RemoveLinedef(line);
            var loopedFrontSector = line.Front?.Sector;
            var loopedBackSector = line.Back?.Sector;
            if (loopedFrontSector != null) loopedFrontSector.NeedsRebuild = true;
            if (loopedBackSector != null) loopedBackSector.NeedsRebuild = true;
            undoActions.Add(() =>
            {
                map.RestoreLinedef(line);
                if (loopedFrontSector != null) loopedFrontSector.NeedsRebuild = true;
                if (loopedBackSector != null) loopedBackSector.NeedsRebuild = true;
            });
        }
    }

    /// <summary>
    /// UDB's own real <c>MapSet.JoinOverlappingLines</c>: merges any two
    /// linedefs in <paramref name="lines"/> that end up sharing both
    /// endpoints into one (<see cref="MapData.JoinLinedefs"/>, UDB's own
    /// real <c>Linedef.Join</c>) - the survivor is always whichever one
    /// is being iterated when the overlap is found (matches UDB's own
    /// real search order exactly), the other is disposed.
    /// </summary>
    public static void JoinOverlappingLines(MapData map, List<Linedef> lines, List<Action> undoActions)
    {
        bool joined;

        do
        {
            joined = false;
            foreach (var keep in lines)
            {
                var remove = FindCoincidentOverlap(keep);
                if (remove == null) continue;

                var originalKeepFront = keep.Front;
                var originalKeepBack = keep.Back;

                lines.Remove(remove);
                map.JoinLinedefs(keep, remove);

                var newKeepFront = keep.Front;
                var newKeepBack = keep.Back;

                undoActions.Add(() =>
                {
                    if (newKeepFront != null && newKeepFront != originalKeepFront)
                    {
                        newKeepFront.Sector.RemoveSidedef(newKeepFront);
                        newKeepFront.Sector.NeedsRebuild = true;
                    }
                    if (newKeepBack != null && newKeepBack != originalKeepBack)
                    {
                        newKeepBack.Sector.RemoveSidedef(newKeepBack);
                        newKeepBack.Sector.NeedsRebuild = true;
                    }

                    if (originalKeepFront != null && originalKeepFront != newKeepFront)
                    {
                        originalKeepFront.Sector.AddSidedef(originalKeepFront);
                        originalKeepFront.Sector.NeedsRebuild = true;
                    }
                    if (originalKeepBack != null && originalKeepBack != newKeepBack)
                    {
                        originalKeepBack.Sector.AddSidedef(originalKeepBack);
                        originalKeepBack.Sector.NeedsRebuild = true;
                    }

                    keep.Front = originalKeepFront;
                    keep.Back = originalKeepBack;

                    map.RestoreLinedef(remove);
                    if (remove.Front != null) remove.Front.Sector.NeedsRebuild = true;
                    if (remove.Back != null) remove.Back.Sector.NeedsRebuild = true;
                });

                joined = true;
                break;
            }
        }
        while (joined);
    }

    private static Linedef? FindCoincidentOverlap(Linedef line)
    {
        foreach (var candidate in line.Start.Linedefs)
        {
            if (candidate == line) continue;
            if (line.End == candidate.End || line.End == candidate.Start) return candidate;
        }

        foreach (var candidate in line.End.Linedefs)
        {
            if (candidate == line) continue;
            if (line.Start == candidate.End || line.Start == candidate.Start) return candidate;
        }

        return null;
    }

    /// <summary>UDB's own real <c>MapSet.FlipBackwardLinedefs</c>: a linedef left with only a Back side (no Front) gets its vertices and sidedefs flipped, matching the format convention that Front must exist whenever Back does.</summary>
    public static void FlipBackwardLinedefs(List<Linedef> lines, List<Action> undoActions)
    {
        foreach (var line in lines)
        {
            if (line.Back == null || line.Front != null) continue;

            var originalStart = line.Start;
            var originalEnd = line.End;
            var originalFront = line.Front;
            var originalBack = line.Back;

            line.Start = originalEnd;
            line.End = originalStart;
            line.Front = originalBack;
            line.Back = originalFront;
            line.MarkAdjacentSectorsDirty();

            undoActions.Add(() =>
            {
                line.Start = originalStart;
                line.End = originalEnd;
                line.Front = originalFront;
                line.Back = originalBack;
                line.MarkAdjacentSectorsDirty();
            });
        }
    }

    /// <summary>UDB's own real <c>MapSet.NearestLinedef</c> - the candidate whose own bounded segment lies closest to a point, a plain linear scan (UDB's own version shortcuts through its blockmap first; this project has no equivalent spatial index for 2D edit-mode geometry queries yet).</summary>
    internal static Linedef? FindNearestLinedef(IReadOnlyList<Linedef> candidates, Vector2 point)
    {
        Linedef? nearest = null;
        var nearestDistanceSquared = float.MaxValue;

        foreach (var linedef in candidates)
        {
            var distanceSquared = GeometryMath.DistanceToSegmentSquared(linedef.Start.Position, linedef.End.Position, point);
            if (distanceSquared < nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearest = linedef;
            }
        }

        return nearest;
    }
}
