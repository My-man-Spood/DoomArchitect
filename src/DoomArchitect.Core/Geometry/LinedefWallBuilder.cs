using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Geometry;

/// <summary>
/// One wall quad: a vertical extent (<see cref="Bottom"/> to
/// <see cref="Top"/>) along a linedef's own Start-End segment, plus which
/// texture field it uses. Turning that into an actual rendered mesh
/// (and eventually a real texture/material) is the App layer's job.
/// </summary>
public readonly record struct WallSegment(Vertex Start, Vertex End, double Bottom, double Top, string Texture);

/// <summary>
/// Computes which wall quads exist for a linedef and their vertical
/// extents - a close port of UDB's own visual-mode wall builders
/// (<c>BaseVisualSector</c>/<c>VisualUpper</c>/<c>VisualLower</c>/
/// <c>VisualMiddleSingle</c>, Source/Plugins/BuilderModes/VisualModes/).
///
/// Deliberately NOT covered here: a two-sided linedef's *masked* middle
/// texture (fences, bars, windows - <c>VisualMiddleDouble</c> in UDB).
/// Verified against UDB's own code that this one is genuinely dependent
/// on the actual texture's pixel height for its default (non-repeating)
/// vertical placement - there's no way to get it right without a real
/// texture pipeline, so it's left for that follow-up rather than faked
/// here. Every other wall part built here has no such dependency - a
/// missing texture (`"-"`) only changes which material gets applied at
/// render time, never whether the shape itself exists.
///
/// Also not ported: UDB's "render as sky" substitution for a missing
/// upper/lower texture next to a sky-flat sector - confirmed (by reading
/// UDB's own code) to be a texture/render-pass decision only, with zero
/// effect on wall shape, so there's nothing for this pure-geometry layer
/// to do about it either way.
/// </summary>
public static class LinedefWallBuilder
{
    private const double MinimumHeight = 0.01;

    public static IReadOnlyList<WallSegment> Build(Linedef linedef)
    {
        var front = linedef.Front;
        var back = linedef.Back;

        if (front != null && back != null) return BuildTwoSided(linedef, front, back);

        var only = front ?? back;
        return only == null ? Array.Empty<WallSegment>() : BuildOneSided(linedef, only);
    }

    /// <summary>Spans the full sector height - matches UDB's <c>VisualMiddleSingle</c>.</summary>
    private static IReadOnlyList<WallSegment> BuildOneSided(Linedef linedef, Sidedef side)
    {
        var floor = side.Sector.FloorHeight;
        var ceiling = side.Sector.CeilingHeight;

        if (ceiling - floor <= MinimumHeight) return Array.Empty<WallSegment>();

        return new[] { new WallSegment(linedef.Start, linedef.End, floor, ceiling, side.MiddleTexture) };
    }

    private static IReadOnlyList<WallSegment> BuildTwoSided(Linedef linedef, Sidedef front, Sidedef back)
    {
        var segments = new List<WallSegment>();

        AddUpperIfVisible(segments, linedef, front, back);
        AddUpperIfVisible(segments, linedef, back, front);
        AddLowerIfVisible(segments, linedef, front, back);
        AddLowerIfVisible(segments, linedef, back, front);

        return segments;
    }

    /// <summary>
    /// A side gets an upper wall exactly when its own sector's ceiling is
    /// higher than the other side's - matches UDB's <c>VisualUpper</c>
    /// (called once per side, with <paramref name="side"/>/
    /// <paramref name="other"/> swapped for the back side, so equal
    /// ceilings correctly produce no upper wall on either side). The
    /// <c>Math.Max</c> clamp on the bottom replicates UDB's defensive
    /// handling of an inverted/"closed" other-sector (floor above
    /// ceiling) - a real vanilla-Doom mapping trick.
    /// </summary>
    private static void AddUpperIfVisible(List<WallSegment> segments, Linedef linedef, Sidedef side, Sidedef other)
    {
        var sideCeiling = side.Sector.CeilingHeight;
        var otherCeiling = other.Sector.CeilingHeight;
        if (sideCeiling <= otherCeiling) return;

        var bottom = Math.Max(otherCeiling, side.Sector.FloorHeight);
        if (sideCeiling - bottom <= MinimumHeight) return;

        segments.Add(new WallSegment(linedef.Start, linedef.End, bottom, sideCeiling, side.UpperTexture));
    }

    /// <summary>Mirror of <see cref="AddUpperIfVisible"/> for floors - matches UDB's <c>VisualLower</c>.</summary>
    private static void AddLowerIfVisible(List<WallSegment> segments, Linedef linedef, Sidedef side, Sidedef other)
    {
        var sideFloor = side.Sector.FloorHeight;
        var otherFloor = other.Sector.FloorHeight;
        if (sideFloor >= otherFloor) return;

        var top = Math.Min(otherFloor, side.Sector.CeilingHeight);
        if (top - sideFloor <= MinimumHeight) return;

        segments.Add(new WallSegment(linedef.Start, linedef.End, sideFloor, top, side.LowerTexture));
    }
}
