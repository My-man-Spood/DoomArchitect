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
///
/// <see cref="VerticalTextureOffset"/>/<see cref="HorizontalTextureOffset"/>
/// are what the App layer should actually use for the quad's own UV
/// origin, instead of reading <see cref="Side"/>'s own <c>OffsetY</c>/
/// <c>OffsetX</c> directly - both already fold in the real UDMF per-part
/// offset fields (<c>offsety_top</c>/<c>offsety_bottom</c>/<c>offsety_mid</c>
/// and their X counterparts, and the matching per-part <c>scalex_*</c>/
/// <c>scaley_*</c> division every real UDMF config applies to them - see
/// <see cref="GetPartTransform"/>'s own remarks) that a classic-format
/// sidedef's single shared <c>OffsetX</c>/<c>OffsetY</c> can't express at
/// all. For an upper/lower/single wall, <see cref="VerticalTextureOffset"/>
/// is that combined offset plus whatever the line's own real pegging flag
/// adds on top (see <c>LinedefWallBuilder</c>'s own remarks - the quad's
/// own extent is still fixed by sector heights alone either way, so this
/// only ever scrolls/anchors which part of the texture image shows within
/// it) - but for a masked middle (<see cref="IsMasked"/>), the combined Y
/// offset instead moves where the texture itself sits (see
/// <see cref="AddMaskedMiddleIfVisible"/>'s own remarks), which already
/// gets folded into <see cref="Top"/>/<see cref="Bottom"/> directly -
/// applying it a second time for the UV would double it.
///
/// <see cref="TextureScaleX"/>/<see cref="TextureScaleY"/> (both 1 unless
/// a per-part <c>scalex_*</c>/<c>scaley_*</c> field says otherwise) are
/// what the App layer should divide a texture's own raw pixel size by
/// before using it as a UV denominator - this class's own offset/height
/// math above is already computed in that same scaled space (see
/// <see cref="GetPartTransform"/>), so the App layer using an
/// unscaled pixel size instead would desync the two.
/// </summary>
public readonly record struct WallSegment(Vertex Start, Vertex End, double Bottom, double Top, string Texture, Sidedef Side, double VerticalTextureOffset, double HorizontalTextureOffset, double TextureScaleX, double TextureScaleY, bool IsMasked);

/// <summary>
/// Computes which wall quads exist for a linedef and their vertical
/// extents - a close port of UDB's own visual-mode wall builders
/// (<c>BaseVisualSector</c>/<c>VisualUpper</c>/<c>VisualLower</c>/
/// <c>VisualMiddleSingle</c>/<c>VisualMiddleDouble</c>,
/// Source/Plugins/BuilderModes/VisualModes/).
///
/// Every wall part's real pegging is ported directly from its own UDB
/// counterpart (see each <c>Add*IfVisible</c>/<see cref="BuildOneSided"/>'s
/// own remarks) - a single-sided wall and a two-sided wall's lower part
/// both default to top-pegged and switch to bottom-pegged when the
/// line's "lower unpegged" flag (<see cref="IsLowerUnpegged"/>) is set;
/// a two-sided wall's upper part is the mirror image - it defaults to
/// *bottom*-pegged (hangs up from the opening) and only becomes top-
/// pegged when the line's "upper unpegged" flag
/// (<see cref="IsUpperUnpegged"/>) is set. This default-bottom-pegged
/// upper behavior is easy to get backwards (it reads unintuitively next
/// to the lower/single/masked-middle wall parts, which all default to
/// top-pegged) - verified directly against <c>VisualUpper.Setup</c>'s own
/// <c>tp.tlt.y</c> computation, not re-derived from memory.
///
/// A two-sided linedef's *masked* middle texture (fences, bars, windows -
/// <c>VisualMiddleDouble</c> in UDB) additionally needs the real
/// composited texture's pixel height just to size its own quad
/// (independent of pegging), which isn't something this pure-geometry
/// layer has on its own - callers pass a <c>textureHeightLookup</c>
/// delegate for it, now also consulted by every other wall part's own
/// default-pegging math above. One thing is still deliberately not
/// modeled:
/// - Repeating (<c>wrapmidtex</c>/Hexen's <c>Line_SetIdentification</c>
///   arg): always treated as off (UDB's own vanilla-format default too -
///   the flag doesn't even exist outside UDMF/Hexen), so a masked middle
///   texture taller than the opening is clipped to it, and one shorter
///   than the opening leaves the remainder of the opening with no
///   geometry at all, matching UDB exactly rather than tiling. Plain
///   upper/lower/single walls were never affected by this - they always
///   tile via the App layer's own repeating texture sampler, matching
///   UDB's own real (non-masked-middle) wall rendering.
/// - UDB's vanilla "both sides have a sky ceiling" pegging-glitch
///   emulation for a lower-unpegged lower wall (<c>VisualLower.Setup</c>'s
///   own <c>HasSkyCeiling</c> special case) - a narrow, sky-flat-specific
///   corner case layered on top of the real formula ported here, and this
///   codebase has no sky-flat concept modeled yet to key it off of.
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
    /// <param name="textureHeightLookup">
    /// Resolves a texture name to its pixel height in map units - needed
    /// to size a two-sided linedef's masked middle texture, and also
    /// consulted by every wall part's own default-pegging math (see this
    /// class's own remarks). Passing <c>null</c> (the default) skips
    /// masked-middle segments entirely and falls every other wall part
    /// back to always-top-pegged (ignoring both pegging flags) rather
    /// than computing a texture-height-dependent offset it can't - a
    /// safe default for any caller not yet wired to real texture data:
    /// only <see cref="WallSegment.VerticalTextureOffset"/> changes, never
    /// a segment's <see cref="WallSegment.Bottom"/>/<see cref="WallSegment.Top"/>.
    /// </param>
    public static IReadOnlyList<WallSegment> Build(Linedef linedef, Func<string, double>? textureHeightLookup = null)
    {
        var front = linedef.Front;
        var back = linedef.Back;

        if (front != null && back != null) return BuildTwoSided(linedef, front, back, textureHeightLookup);

        var only = front ?? back;
        return only == null ? Array.Empty<WallSegment>() : BuildOneSided(linedef, only, textureHeightLookup);
    }

    /// <summary>
    /// Spans the full sector height - matches UDB's <c>VisualMiddleSingle</c>.
    /// Default (lower-unpegged flag clear) is top-pegged (V=0 at the
    /// ceiling); flag set anchors the texture's own bottom edge to the
    /// sector's own floor instead (hangs up), matching
    /// <c>VisualMiddleSingle.Setup</c>'s own <c>tp.tlt.y</c> formula.
    /// </summary>
    private static IReadOnlyList<WallSegment> BuildOneSided(Linedef linedef, Sidedef side, Func<string, double>? textureHeightLookup)
    {
        var floor = side.Sector.FloorHeight;
        var ceiling = side.Sector.CeilingHeight;

        if (ceiling - floor <= MinimumHeight) return Array.Empty<WallSegment>();

        var transform = GetPartTransform(side, "mid");
        var verticalTextureOffset = transform.OffsetY;
        if (IsLowerUnpegged(linedef) && textureHeightLookup != null)
        {
            verticalTextureOffset = textureHeightLookup(side.MiddleTexture) / transform.ScaleY - (ceiling - floor) + transform.OffsetY;
        }

        return new[] { new WallSegment(linedef.Start, linedef.End, floor, ceiling, side.MiddleTexture, side, verticalTextureOffset, transform.OffsetX, transform.ScaleX, transform.ScaleY, IsMasked: false) };
    }

    private static IReadOnlyList<WallSegment> BuildTwoSided(
        Linedef linedef, Sidedef front, Sidedef back, Func<string, double>? textureHeightLookup)
    {
        var segments = new List<WallSegment>();

        AddUpperIfVisible(segments, linedef, front, back, textureHeightLookup);
        AddUpperIfVisible(segments, linedef, back, front, textureHeightLookup);
        AddLowerIfVisible(segments, linedef, front, back, textureHeightLookup);
        AddLowerIfVisible(segments, linedef, back, front, textureHeightLookup);

        if (textureHeightLookup != null)
        {
            AddMaskedMiddleIfVisible(segments, linedef, front, back, textureHeightLookup);
            AddMaskedMiddleIfVisible(segments, linedef, back, front, textureHeightLookup);
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
    ///
    /// Pegging is the mirror image of every other wall part: default
    /// (upper-unpegged flag clear) is *bottom*-pegged - the texture's own
    /// bottom edge anchors to <paramref name="other"/>'s own ceiling
    /// (using its real, unclamped height, not the clamped visible
    /// <c>bottom</c> above - matching <c>VisualUpper.Setup</c>'s own
    /// <c>tp</c> formula, which always references <c>Sidedef.Other.Sector.CeilHeight</c>
    /// directly) and hangs up; flag set switches to top-pegged (V=0 at
    /// <paramref name="side"/>'s own ceiling), matching
    /// <c>VisualUpper.Setup</c>'s own <c>IsFlagSet(UpperUnpeggedFlag)</c>
    /// branch exactly.
    /// </summary>
    private static void AddUpperIfVisible(List<WallSegment> segments, Linedef linedef, Sidedef side, Sidedef other, Func<string, double>? textureHeightLookup)
    {
        var sideCeiling = side.Sector.CeilingHeight;
        var otherCeiling = other.Sector.CeilingHeight;
        if (sideCeiling <= otherCeiling) return;

        var bottom = Math.Max(otherCeiling, side.Sector.FloorHeight);
        if (sideCeiling - bottom <= MinimumHeight) return;

        var transform = GetPartTransform(side, "top");
        var verticalTextureOffset = transform.OffsetY;
        if (!IsUpperUnpegged(linedef) && textureHeightLookup != null)
        {
            verticalTextureOffset = textureHeightLookup(side.UpperTexture) / transform.ScaleY - (sideCeiling - otherCeiling) + transform.OffsetY;
        }

        segments.Add(new WallSegment(linedef.Start, linedef.End, bottom, sideCeiling, side.UpperTexture, side, verticalTextureOffset, transform.OffsetX, transform.ScaleX, transform.ScaleY, IsMasked: false));
    }

    /// <summary>
    /// Mirror of <see cref="AddUpperIfVisible"/> for floors - matches
    /// UDB's <c>VisualLower</c>. Default (lower-unpegged flag clear) is
    /// top-pegged (V=0 at the top of the visible gap); flag set anchors
    /// the texture's own top edge to <paramref name="side"/>'s own
    /// ceiling minus <paramref name="other"/>'s own floor (both real,
    /// unclamped heights - matching <c>VisualLower.Setup</c>'s own
    /// <c>tp.tlt.y</c> formula, which needs no texture height at all for
    /// this branch, unlike <see cref="AddUpperIfVisible"/>'s default
    /// case).
    /// </summary>
    private static void AddLowerIfVisible(List<WallSegment> segments, Linedef linedef, Sidedef side, Sidedef other, Func<string, double>? textureHeightLookup)
    {
        var sideFloor = side.Sector.FloorHeight;
        var otherFloor = other.Sector.FloorHeight;
        if (sideFloor >= otherFloor) return;

        var top = Math.Min(otherFloor, side.Sector.CeilingHeight);
        if (top - sideFloor <= MinimumHeight) return;

        var transform = GetPartTransform(side, "bottom");
        var verticalTextureOffset = IsLowerUnpegged(linedef)
            ? side.Sector.CeilingHeight - otherFloor + transform.OffsetY
            : transform.OffsetY;

        segments.Add(new WallSegment(linedef.Start, linedef.End, sideFloor, top, side.LowerTexture, side, verticalTextureOffset, transform.OffsetX, transform.ScaleX, transform.ScaleY, IsMasked: false));
    }

    /// <summary>
    /// A sidedef's real per-part texture offset and scale, matching UDB's
    /// own pattern (identical across every one of <c>VisualUpper</c>/
    /// <c>VisualLower</c>/<c>VisualMiddleSingle</c>/<c>VisualMiddleDouble</c>'s
    /// own <c>Setup</c>): <c>tscale = (scalex_&lt;partSuffix&gt;,
    /// scaley_&lt;partSuffix&gt;)</c> (each defaulting to 1, e.g.
    /// <c>scalex_mid</c>/<c>scaley_mid</c> for <c>"mid"</c>), then
    /// <c>tof = (Sidedef.OffsetX, Sidedef.OffsetY) + (offsetx_&lt;partSuffix&gt;,
    /// offsety_&lt;partSuffix&gt;)</c> divided by <c>Abs(tscale)</c> - UDB
    /// gates that division behind a game-configuration
    /// <c>ScaledTextureOffsets</c> flag that's <c>true</c> in every real
    /// game config shipped with UDB (vanilla and every ZDoom-family one
    /// alike, verified directly against the actual `.cfg` data), so with
    /// no game-configuration system here to make it configurable, it's
    /// hardcoded true - the same precedent
    /// <c>CompositeTextureBuilder</c> already set for its own two
    /// always-true vanilla compatibility flags. A classic-format sidedef
    /// simply has no per-part fields to find, so both the offset and the
    /// scale naturally reduce to the shared <c>OffsetX</c>/<c>OffsetY</c>
    /// and a plain 1:1 scale there - a real, previously-missed gap
    /// otherwise: this codebase never read either the per-part offset or
    /// scale fields at all, so a UDMF map using them (a common, deliberate
    /// technique for placing a masked-middle decoration - e.g. a
    /// corpse/gore or door texture - at a precise height/size without a
    /// dedicated small sector for it) rendered that part at the wrong
    /// position, or - once the offset-only fix above started actually
    /// applying an offset that was only ever meant to be interpreted
    /// against a scaled texture - could push it out of its sector
    /// entirely, producing no geometry at all where something used to be
    /// (if only in the wrong place). Near-zero/zero scale field values
    /// are clamped to 1 rather than dividing by (near) zero.
    /// </summary>
    private static (double OffsetX, double OffsetY, double ScaleX, double ScaleY) GetPartTransform(Sidedef side, string partSuffix)
    {
        var scaleX = Math.Abs(side.Fields.GetFloat($"scalex_{partSuffix}", 1.0));
        var scaleY = Math.Abs(side.Fields.GetFloat($"scaley_{partSuffix}", 1.0));
        if (scaleX < 0.001) scaleX = 1.0;
        if (scaleY < 0.001) scaleY = 1.0;

        var offsetX = (side.OffsetX + side.Fields.GetFloat($"offsetx_{partSuffix}", 0)) / scaleX;
        var offsetY = (side.OffsetY + side.Fields.GetFloat($"offsety_{partSuffix}", 0)) / scaleY;

        return (offsetX, offsetY, scaleX, scaleY);
    }

    /// <summary>
    /// UDB's own real classic <c>ML_DONTPEGBOTTOM</c> bit (value 16,
    /// verified directly against its own game-configuration data) for a
    /// classic-format linedef, or the UDMF <c>dontpegbottom</c> field for
    /// one loaded from a UDMF map - <c>ClassicMapReader</c> stores a
    /// classic linedef's whole raw flags word verbatim as an integer
    /// <c>"flags"</c> field, while <c>UdmfReader</c> stores each named
    /// UDMF flag as its own boolean field directly - so checking both
    /// here, in that order, correctly resolves either format without
    /// needing to know which one produced this <see cref="Linedef"/>.
    /// </summary>
    private static bool IsLowerUnpegged(Linedef linedef) =>
        linedef.Fields.GetBool("dontpegbottom", false) || (linedef.Fields.GetInteger("flags", 0) & 16) != 0;

    /// <summary>
    /// UDB's own real classic <c>ML_DONTPEGTOP</c> bit (value 8,
    /// verified directly against its own game-configuration data) for a
    /// classic-format linedef, or the UDMF <c>dontpegtop</c> field for one
    /// loaded from a UDMF map - same dual-storage-convention read as
    /// <see cref="IsLowerUnpegged"/>, see its own remarks.
    /// </summary>
    private static bool IsUpperUnpegged(Linedef linedef) =>
        linedef.Fields.GetBool("dontpegtop", false) || (linedef.Fields.GetInteger("flags", 0) & 8) != 0;

    /// <summary>
    /// A two-sided masked middle (fence/bars/window) - matches UDB's
    /// <c>VisualMiddleDouble</c>: the "opening" between the two sectors is
    /// <c>[max(floor_front, floor_back), min(ceiling_front, ceiling_back)]</c>
    /// (same bounds an upper/lower wall's own gap uses). Real pegging,
    /// ported directly from <c>VisualMiddleDouble.Setup</c>'s own real
    /// crop-plane computation: with the line's own "lower unpegged" flag
    /// set, the texture's *bottom* edge anchors to the opening's own
    /// bottom and hangs *up*; the default (flag unset) anchors the *top*
    /// edge to the opening's own top and hangs down, this class's own
    /// prior unconditional behavior. Either way, the real combined offset
    /// (<see cref="GetPartOffset"/>) shifts that anchor point itself (not
    /// just which part of the texture image shows, unlike a plain
    /// upper/lower/single wall - see
    /// <see cref="WallSegment.VerticalTextureOffset"/>'s own remarks), and
    /// the result is clipped to the opening on both ends exactly as
    /// before - a texture shorter than the opening (or one pushed out of
    /// it entirely by its own offset) leaves the rest of the opening with
    /// no geometry rather than tiling. Nothing is built at all if the
    /// side has no middle texture or the opening has no real height.
    /// </summary>
    private static void AddMaskedMiddleIfVisible(
        List<WallSegment> segments, Linedef linedef, Sidedef side, Sidedef other, Func<string, double> textureHeightLookup)
    {
        if (side.MiddleTexture == "-") return;

        var openingTop = Math.Min(side.Sector.CeilingHeight, other.Sector.CeilingHeight);
        var openingBottom = Math.Max(side.Sector.FloorHeight, other.Sector.FloorHeight);
        if (openingTop - openingBottom <= MinimumHeight) return;

        var transform = GetPartTransform(side, "mid");
        var textureHeight = textureHeightLookup(side.MiddleTexture) / transform.ScaleY;
        var textureTop = IsLowerUnpegged(linedef)
            ? openingBottom + transform.OffsetY + textureHeight
            : openingTop + transform.OffsetY;
        var textureBottom = textureTop - textureHeight;

        var top = Math.Min(textureTop, openingTop);
        var bottom = Math.Max(textureBottom, openingBottom);
        if (top - bottom <= MinimumHeight) return;

        // How far the visible top edge sits below the texture's own
        // natural (unclipped) top - 0 unless the offset, or a too-tall/
        // short texture, pushed part of it outside the opening.
        var verticalTextureOffset = textureTop - top;

        segments.Add(new WallSegment(linedef.Start, linedef.End, bottom, top, side.MiddleTexture, side, verticalTextureOffset, transform.OffsetX, transform.ScaleX, transform.ScaleY, IsMasked: true));
    }
}
