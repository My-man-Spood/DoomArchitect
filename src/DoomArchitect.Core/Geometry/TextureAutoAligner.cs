using System.Numerics;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Geometry;

/// <summary>One sidedef's part, and its new offset(s) - <c>null</c> for an axis that wasn't requested or can't be aligned for this part (see <see cref="TextureAutoAligner"/>'s own remarks on masked-middle Y).</summary>
public readonly record struct TextureAlignResult(Sidedef Side, WallPartKind Part, double? OffsetX, double? OffsetY);

/// <summary>
/// A close port of UDB's own real texture auto-align (<c>BaseVisualMode.AutoAlignTexturesUDMF</c>/
/// <c>AddSidedefsForAlignment</c>, verified directly against source): a
/// stack-based flood-fill starting from one wall part, walking outward
/// along shared vertices to connected linedefs, aligning every reachable
/// sidedef whose *same* part (see the scope note below) carries the same
/// texture name, until the texture changes or there's nowhere left to go.
///
/// X accumulates by each wall's own real length as the walk proceeds (so
/// texture columns land edge-to-edge); a "forward" job (one discovered by
/// walking off a sidedef's own far/End vertex) writes its own X directly
/// from the accumulated offset and hands the *next* wall
/// <c>offset + this wall's own length</c>; a "backward" job (discovered
/// off the near/Start vertex) mirrors it, writing <c>offset - length</c>
/// and handing that on. This asymmetry - not just "always add" - is what
/// keeps a chain aligned correctly when walked in both directions from
/// the same starting point at once, exactly matching UDB's own real
/// forward/backward job split.
///
/// Y is **not** distance-accumulated at all (two connected walls can sit
/// at completely different heights) - instead each aligned wall's own
/// *natural* (unclipped) texture-top world Z is solved to match the
/// start's, reusing <see cref="LinedefWallBuilder"/> itself as the oracle
/// for that formula (its <c>Top</c> + <c>VerticalTextureOffset</c> is
/// exactly that world Z, for any pegging state, since
/// <c>VerticalTextureOffset</c> already encodes whichever pegging rule
/// applies) rather than re-deriving UDB's own separate per-part Y-anchor
/// formulas a second time. That trick relies on <c>Top</c> itself being
/// independent of the offset being solved for - true for an upper/lower
/// part (fixed by sector heights alone) but **not** true for a masked
/// middle, whose own `Top`/`Bottom` shift together with its Y offset (see
/// <c>AddMaskedMiddleIfVisible</c>'s own remarks) - Y-alignment is
/// deliberately skipped for a masked-middle part reached mid-walk (X
/// still aligns normally; only <see cref="TextureAlignResult.OffsetY"/>
/// stays <c>null</c>), and skipped for the whole call if the *start* part
/// itself is masked-middle, rather than risk a subtly-wrong solve near a
/// clip boundary.
///
/// Returns *data*, not commands - no `MapData`/undo dependency at all, so
/// the algorithm itself is directly testable; the caller turns the
/// result into whatever `SetFieldCommand`s it needs.
///
/// Deliberately scoped down from UDB's own real algorithm (flagged, not
/// silently expanded into):
/// - Only propagates within the *same* part role (upper-to-upper, lower-
///   to-lower, middle-to-middle) - real UDB can chain across roles (e.g.
///   an upper into a neighbor's lower) if they happen to share a texture
///   name, via its own `VisualSidedefParts` triangle-count machinery this
///   project has no equivalent of.
/// - No 3D-floor (`middle3d`)/`GetControlSides` participation - this
///   project doesn't model 3D floors at all yet.
/// - No per-part texture *scale* (`scalex_mid` etc) applied to the walk
///   itself (the accumulated length, or the Y-solve) - deferred as a
///   follow-up once the simpler unscaled version is working.
/// - No "restrict to selection" variant (`visualautoaligntoselection*`) -
///   always walks and aligns everything reachable, matching UDB's own
///   plain `visualautoalign(x/y)` actions.
/// </summary>
public static class TextureAutoAligner
{
    private readonly record struct AlignJob(Sidedef Side, bool Forward, double OffsetX);

    public static IReadOnlyList<TextureAlignResult> Align(
        Sidedef startSide, WallPartKind part, bool alignX, bool alignY, Func<string, double> textureWidthLookup, Func<string, double> textureHeightLookup)
    {
        var results = new List<TextureAlignResult>();
        if (!alignX && !alignY) return results;

        var textureName = LinedefWallBuilder.GetPartTexture(startSide, part);
        if (textureName == "-") return results;

        double? referenceZ = null;
        if (alignY && part != WallPartKind.Middle)
        {
            referenceZ = TryGetTextureTopZ(startSide.Linedef, startSide, part, textureHeightLookup);
            if (referenceZ == null) alignY = false;
        }
        else
        {
            alignY = false;
        }

        var partSuffix = LinedefWallBuilder.PartSuffix(part);
        var startOffsetX = startSide.Fields.GetFloat($"offsetx_{partSuffix}", 0);

        var aligned = new HashSet<Sidedef>();
        var stack = new Stack<AlignJob>();
        stack.Push(new AlignJob(startSide, Forward: true, startOffsetX));

        while (stack.Count > 0)
        {
            var job = stack.Pop();
            if (aligned.Contains(job.Side)) continue;
            aligned.Add(job.Side);

            var linedef = job.Side.Linedef;
            var length = Vector2.Distance(linedef.Start.Position, linedef.End.Position);

            double forwardOffset, backwardOffset;
            double? newOffsetX = null;
            if (job.Forward)
            {
                if (alignX) newOffsetX = job.OffsetX;
                forwardOffset = job.OffsetX + Math.Round(length);
                backwardOffset = job.OffsetX;
            }
            else
            {
                if (alignX) newOffsetX = job.OffsetX - Math.Round(length);
                forwardOffset = job.OffsetX;
                backwardOffset = job.OffsetX - Math.Round(length);
            }

            double? newOffsetY = null;
            if (alignY)
            {
                var currentZ = TryGetTextureTopZ(linedef, job.Side, part, textureHeightLookup);
                if (currentZ != null)
                {
                    var currentOffsetY = job.Side.Fields.GetFloat($"offsety_{partSuffix}", 0);
                    newOffsetY = currentOffsetY + (referenceZ!.Value - currentZ.Value);
                }
            }

            if (newOffsetX != null) newOffsetX = Wrap(newOffsetX.Value, textureWidthLookup(textureName));
            if (newOffsetY != null) newOffsetY = Wrap(newOffsetY.Value, textureHeightLookup(textureName));

            results.Add(new TextureAlignResult(job.Side, part, newOffsetX, newOffsetY));

            var startVertex = job.Side.IsFront ? linedef.Start : linedef.End;
            var endVertex = job.Side.IsFront ? linedef.End : linedef.Start;

            PushNeighbors(stack, startVertex, forward: false, backwardOffset, part, textureName, aligned, textureHeightLookup);
            PushNeighbors(stack, endVertex, forward: true, forwardOffset, part, textureName, aligned, textureHeightLookup);
        }

        return results;
    }

    /// <summary>
    /// A close port of UDB's own real <c>AddSidedefsForAlignment</c>: at
    /// <paramref name="v"/>, every touching linedef's side that "continues"
    /// in <paramref name="forward"/>'s direction (matching UDB's own real
    /// <c>ld.Start == v</c>/<c>ld.End == v</c> branching, which resolves
    /// which of a two-sided linedef's Front/Back is the one being walked
    /// onto) gets queued, provided that side's own same-role part is
    /// actually visible and carries the matching texture name - this
    /// project's own scoped-down equivalent of UDB's
    /// <c>SidedefTextureMatch</c>/`matchtop`/`matchbottom`/`matchmid`
    /// checks, unified into one same-role lookup via
    /// <see cref="LinedefWallBuilder.Build"/> itself.
    /// </summary>
    private static void PushNeighbors(
        Stack<AlignJob> stack, Vertex v, bool forward, double offsetX, WallPartKind part, string textureName,
        HashSet<Sidedef> aligned, Func<string, double> textureHeightLookup)
    {
        foreach (var linedef in v.Linedefs)
        {
            var side1 = forward ? linedef.Front : linedef.Back;
            var side2 = forward ? linedef.Back : linedef.Front;
            if ((side1 != null && aligned.Contains(side1)) || (side2 != null && aligned.Contains(side2))) continue;

            if (linedef.Start == v && side1 != null)
            {
                if (PartVisibleAndMatches(linedef, side1, part, textureName, textureHeightLookup))
                {
                    stack.Push(new AlignJob(side1, forward, offsetX));
                }
            }
            else if (linedef.End == v && side2 != null)
            {
                if (PartVisibleAndMatches(linedef, side2, part, textureName, textureHeightLookup))
                {
                    stack.Push(new AlignJob(side2, forward, offsetX));
                }
            }
        }
    }

    private static bool PartVisibleAndMatches(Linedef linedef, Sidedef side, WallPartKind part, string textureName, Func<string, double> textureHeightLookup)
    {
        foreach (var segment in LinedefWallBuilder.Build(linedef, textureHeightLookup))
        {
            if (segment.Side == side && segment.PartKind == part && segment.Texture == textureName) return true;
        }

        return false;
    }

    /// <summary>
    /// The world Z at which this part's own texture naturally starts (V=0
    /// in its own UV space) - <c>Top + VerticalTextureOffset</c>, true
    /// regardless of pegging state since <c>VerticalTextureOffset</c>
    /// already encodes it (see this class's own remarks). <c>null</c> if
    /// this part isn't actually visible here at all (no geometry to align
    /// against).
    /// </summary>
    private static double? TryGetTextureTopZ(Linedef linedef, Sidedef side, WallPartKind part, Func<string, double> textureHeightLookup)
    {
        foreach (var segment in LinedefWallBuilder.Build(linedef, textureHeightLookup))
        {
            if (segment.Side == side && segment.PartKind == part) return segment.Top + segment.VerticalTextureOffset;
        }

        return null;
    }

    private static double Wrap(double value, double textureSize) => textureSize > 0 ? value % textureSize : value;
}
