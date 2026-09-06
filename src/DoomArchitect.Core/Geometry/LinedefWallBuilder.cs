using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Geometry;

/// <summary>
/// One wall quad: a vertical extent (<see cref="Bottom"/> to
/// <see cref="Top"/>) along a linedef's own Start-End segment, plus which
/// texture field it uses and the sidedef that texture came from (needed
/// by the App layer for that sidedef's own offsets - matching purely by
/// <see cref="Texture"/> name would misattribute offsets if front and
/// back happened to share a texture name). Turning this into an actual
/// rendered mesh (and eventually a real texture/material) is the App
/// layer's job.
/// </summary>
public readonly record struct WallSegment(Vertex Start, Vertex End, double Bottom, double Top, string Texture, Sidedef Side);

/// <summary>
/// Computes which wall quads exist for a linedef and their vertical
/// extents - a close port of UDB's own visual-mode wall builders
/// (<c>BaseVisualSector</c>/<c>VisualUpper</c>/<c>VisualLower</c>/
/// <c>VisualMiddleSingle</c>/<c>VisualMiddleDouble</c>,
/// Source/Plugins/BuilderModes/VisualModes/).
///
/// A two-sided linedef's *masked* middle texture (fences, bars, windows -
/// <c>VisualMiddleDouble</c> in UDB) needs the real composited texture's
/// pixel height to size correctly, which isn't something this pure-
/// geometry layer has on its own - callers pass a
/// <c>middleTextureHeightLookup</c> delegate for it. Two things are
/// deliberately not modeled yet, both because this codebase has no typed
/// linedef-flags concept at all yet (see <c>Core.Map.Linedef</c>):
/// - Pegging: UDB anchors the texture's bottom edge to the opening's
///   bottom when the line's "lower unpegged" flag is set; this always
///   uses UDB's *default* (flag unset) behavior - anchor the top edge to
///   the opening's top, hanging down.
/// - Repeating (<c>wrapmidtex</c>/Hexen's <c>Line_SetIdentification</c>
///   arg): always treated as off (UDB's own vanilla-format default too -
///   the flag doesn't even exist outside UDMF/Hexen), so a texture taller
///   than the opening is clipped to it, and a texture shorter than the
///   opening leaves the remainder of the opening with no geometry at all,
///   matching UDB exactly rather than tiling.
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

    /// <param name="linedef">The linedef to build wall quads for.</param>
    /// <param name="middleTextureHeightLookup">
    /// Resolves a texture name to its pixel height in map units, needed
    /// only to size a two-sided linedef's masked middle texture. Passing
    /// <c>null</c> (the default) skips masked-middle segments entirely -
    /// every other wall part is unaffected, so this stays a safe default
    /// for any caller not yet wired to real texture data.
    /// </param>
    public static IReadOnlyList<WallSegment> Build(Linedef linedef, Func<string, double>? middleTextureHeightLookup = null)
    {
        var front = linedef.Front;
        var back = linedef.Back;

        if (front != null && back != null) return BuildTwoSided(linedef, front, back, middleTextureHeightLookup);

        var only = front ?? back;
        return only == null ? Array.Empty<WallSegment>() : BuildOneSided(linedef, only);
    }

    /// <summary>Spans the full sector height - matches UDB's <c>VisualMiddleSingle</c>.</summary>
    private static IReadOnlyList<WallSegment> BuildOneSided(Linedef linedef, Sidedef side)
    {
        var floor = side.Sector.FloorHeight;
        var ceiling = side.Sector.CeilingHeight;

        if (ceiling - floor <= MinimumHeight) return Array.Empty<WallSegment>();

        return new[] { new WallSegment(linedef.Start, linedef.End, floor, ceiling, side.MiddleTexture, side) };
    }

    private static IReadOnlyList<WallSegment> BuildTwoSided(
        Linedef linedef, Sidedef front, Sidedef back, Func<string, double>? middleTextureHeightLookup)
    {
        var segments = new List<WallSegment>();

        AddUpperIfVisible(segments, linedef, front, back);
        AddUpperIfVisible(segments, linedef, back, front);
        AddLowerIfVisible(segments, linedef, front, back);
        AddLowerIfVisible(segments, linedef, back, front);

        if (middleTextureHeightLookup != null)
        {
            AddMaskedMiddleIfVisible(segments, linedef, front, back, middleTextureHeightLookup);
            AddMaskedMiddleIfVisible(segments, linedef, back, front, middleTextureHeightLookup);
        }

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

        segments.Add(new WallSegment(linedef.Start, linedef.End, bottom, sideCeiling, side.UpperTexture, side));
    }

    /// <summary>Mirror of <see cref="AddUpperIfVisible"/> for floors - matches UDB's <c>VisualLower</c>.</summary>
    private static void AddLowerIfVisible(List<WallSegment> segments, Linedef linedef, Sidedef side, Sidedef other)
    {
        var sideFloor = side.Sector.FloorHeight;
        var otherFloor = other.Sector.FloorHeight;
        if (sideFloor >= otherFloor) return;

        var top = Math.Min(otherFloor, side.Sector.CeilingHeight);
        if (top - sideFloor <= MinimumHeight) return;

        segments.Add(new WallSegment(linedef.Start, linedef.End, sideFloor, top, side.LowerTexture, side));
    }

    /// <summary>
    /// A two-sided masked middle (fence/bars/window) - matches UDB's
    /// <c>VisualMiddleDouble</c>: the "opening" between the two sectors is
    /// <c>[max(floor_front, floor_back), min(ceiling_front, ceiling_back)]</c>
    /// (same bounds an upper/lower wall's own gap uses), and - with no
    /// unpegged flag and no repeat, both hardcoded off per this class's
    /// remarks - the texture's top edge anchors to the top of that
    /// opening and hangs down by its own height, clipped to the opening
    /// on both ends. A texture shorter than the opening leaves the rest
    /// of the opening with no geometry; nothing is built at all if the
    /// side has no middle texture or the opening has no real height.
    /// </summary>
    private static void AddMaskedMiddleIfVisible(
        List<WallSegment> segments, Linedef linedef, Sidedef side, Sidedef other, Func<string, double> textureHeightLookup)
    {
        if (side.MiddleTexture == "-") return;

        var openingTop = Math.Min(side.Sector.CeilingHeight, other.Sector.CeilingHeight);
        var openingBottom = Math.Max(side.Sector.FloorHeight, other.Sector.FloorHeight);
        if (openingTop - openingBottom <= MinimumHeight) return;

        var textureHeight = textureHeightLookup(side.MiddleTexture);
        var top = openingTop;
        var bottom = Math.Max(openingBottom, top - textureHeight);
        if (top - bottom <= MinimumHeight) return;

        segments.Add(new WallSegment(linedef.Start, linedef.End, bottom, top, side.MiddleTexture, side));
    }
}
