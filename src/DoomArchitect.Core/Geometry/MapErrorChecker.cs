using System.Numerics;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Geometry;

/// <summary>
/// One reported map integrity issue - UDB's real <c>ErrorResult</c>
/// (<c>ErrorChecks/ErrorResult.cs</c>), scoped down to what
/// <c>MapErrorCheckDialog</c> actually needs: a human-readable
/// <see cref="Description"/>, whichever real map elements are involved
/// (so a UI can select them), and the already-computed bounding box to
/// zoom the 2D view to - UDB computes this on demand via each involved
/// element's own bbox (<c>ErrorResult.GetZoomArea</c>); computed once
/// here at check time instead, since every check already has the
/// relevant positions on hand.
/// </summary>
public sealed record MapErrorResult(
    string Description,
    IReadOnlyList<Vertex> Vertices,
    IReadOnlyList<Linedef> Linedefs,
    IReadOnlyList<Sector> Sectors,
    Vector2 BoundsMin,
    Vector2 BoundsMax);

/// <summary>
/// A plain, synchronous port of UDB's real "Check Map for Errors" tool
/// (<c>ErrorChecks/</c>) - UDB's own architecture is a reflection-
/// discovered, attribute-tagged set of checkers, each run on its own
/// worker thread (<c>Environment.ProcessorCount</c> at a time) with live
/// progress reporting; real overkill for this project's map sizes and
/// Godot's own UI-thread constraints, so this is just a plain static
/// list of check functions run in order. The checks themselves are
/// still ported faithfully from UDB's own real check classes, scoped to
/// the four most directly relevant to actual reported corruption
/// (overlapping/stray vertices, bad linedef sides, unclosed sectors) -
/// UDB's other real checks (missing/unknown textures, stuck/obsolete
/// things, map size) are a deliberate, separate follow-up, not needed
/// for this.
/// </summary>
public static class MapErrorChecker
{
    public static IReadOnlyList<MapErrorResult> RunAll(MapData map) =>
        CheckOverlappingVertices(map)
            .Concat(CheckStrayVertices(map))
            .Concat(CheckLinedefSides(map))
            .Concat(CheckUnclosedSectors(map))
            .ToList();

    /// <summary>UDB's real <c>CheckOverlappingVertices</c>' vertex-vs-vertex case (its own vertex-vs-line case is a separate, deliberately deferred check) - every real distinct position sharing more than one <see cref="Vertex"/>.</summary>
    public static IReadOnlyList<MapErrorResult> CheckOverlappingVertices(MapData map)
    {
        var results = new List<MapErrorResult>();

        foreach (var group in map.Vertices.GroupBy(v => v.Position))
        {
            var vertices = group.ToList();
            if (vertices.Count < 2) continue;

            var position = group.Key;
            results.Add(new MapErrorResult(
                $"{vertices.Count} vertices overlap at ({position.X}, {position.Y})",
                vertices, Array.Empty<Linedef>(), Array.Empty<Sector>(),
                position, position));
        }

        return results;
    }

    /// <summary>UDB's real <c>CheckStrayVertices</c> ("Check unconnected vertices") - exactly a vertex with no linedef touching it at all.</summary>
    public static IReadOnlyList<MapErrorResult> CheckStrayVertices(MapData map)
    {
        var results = new List<MapErrorResult>();

        foreach (var vertex in map.Vertices)
        {
            if (vertex.Linedefs.Count > 0) continue;

            results.Add(new MapErrorResult(
                $"Vertex at ({vertex.Position.X}, {vertex.Position.Y}) isn't connected to anything",
                new[] { vertex }, Array.Empty<Linedef>(), Array.Empty<Sector>(),
                vertex.Position, vertex.Position));
        }

        return results;
    }

    /// <summary>
    /// UDB's real <c>CheckLineReferences</c> (a linedef with no sides at
    /// all, or only a back side - the exact shape
    /// <see cref="GeometryStitcher.FlipBackwardLinedefs"/> already exists
    /// to fix elsewhere, just not surfaced as its own detectable issue
    /// before now), plus one addition that isn't one of UDB's own named
    /// checks: a linedef whose front and back sidedef both belong to the
    /// *same* sector - not inherently invalid on its own (a real self-
    /// referencing line is a legitimate trick for fake glass/deep water,
    /// per <see cref="SectorTracer.Trace"/>'s own remarks), but trivial
    /// and high-value to surface given it's exactly the shape a sector-
    /// building trace's own dead-end/flip-side fallback can produce
    /// against already-broken geometry (confirmed directly against a
    /// real corrupted map).
    /// </summary>
    public static IReadOnlyList<MapErrorResult> CheckLinedefSides(MapData map)
    {
        var results = new List<MapErrorResult>();

        foreach (var linedef in map.Linedefs)
        {
            var bounds = LinedefBounds(linedef);

            if (linedef.Front == null && linedef.Back == null)
            {
                results.Add(new MapErrorResult(
                    "Linedef has no sidedefs at all", Array.Empty<Vertex>(), new[] { linedef }, Array.Empty<Sector>(),
                    bounds.Min, bounds.Max));
            }
            else if (linedef.Front == null)
            {
                results.Add(new MapErrorResult(
                    "Linedef only has a back sidedef - should be flipped", Array.Empty<Vertex>(), new[] { linedef }, Array.Empty<Sector>(),
                    bounds.Min, bounds.Max));
            }
            else if (linedef.Back != null && linedef.Front.Sector == linedef.Back.Sector)
            {
                results.Add(new MapErrorResult(
                    "Linedef's front and back both belong to the same sector", Array.Empty<Vertex>(), new[] { linedef },
                    new[] { linedef.Front.Sector },
                    bounds.Min, bounds.Max));
            }
        }

        return results;
    }

    /// <summary>
    /// UDB's real <c>CheckClosedSectors</c>' intent, ported through this
    /// project's own <see cref="SectorTracer"/> rather than UDB's own
    /// per-vertex bitmask - <see cref="SectorTracer.Trace"/> already
    /// silently drops any sidedef it can't fit into a closed loop rather
    /// than throwing or reporting that gap itself, so comparing the
    /// sector's own real (non-self-referencing - <see cref="SectorTracer"/>
    /// excludes those from tracing entirely) sidedef count against the
    /// total vertex count across every loop actually returned catches
    /// exactly that silently-dropped gap.
    /// </summary>
    public static IReadOnlyList<MapErrorResult> CheckUnclosedSectors(MapData map)
    {
        var results = new List<MapErrorResult>();

        foreach (var sector in map.Sectors)
        {
            var realSidedefCount = sector.Sidedefs.Count(sd => !IsSelfReferencing(sd));
            if (realSidedefCount == 0) continue;

            var loops = SectorTracer.Trace(sector);
            var tracedCount = loops.Sum(loop => loop.Vertices.Count);
            if (tracedCount >= realSidedefCount) continue;

            var bounds = SectorBounds.Compute(sector);
            results.Add(new MapErrorResult(
                $"Sector has {realSidedefCount - tracedCount} sidedef(s) that don't form a closed boundary",
                Array.Empty<Vertex>(), Array.Empty<Linedef>(), new[] { sector },
                bounds.Min, bounds.Max));
        }

        return results;
    }

    private static bool IsSelfReferencing(Sidedef sidedef)
    {
        var other = sidedef.IsFront ? sidedef.Linedef.Back : sidedef.Linedef.Front;
        return other != null && other.Sector == sidedef.Sector;
    }

    private static (Vector2 Min, Vector2 Max) LinedefBounds(Linedef linedef)
    {
        var a = linedef.Start.Position;
        var b = linedef.End.Position;
        return (Vector2.Min(a, b), Vector2.Max(a, b));
    }
}
