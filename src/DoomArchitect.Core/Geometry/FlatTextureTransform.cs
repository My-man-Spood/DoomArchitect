using System.Numerics;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Geometry;

/// <summary>
/// A sector's floor/ceiling texture UV, accounting for its own real
/// pan/scale/rotation UDMF fields - a real, reported gap this fixes:
/// <see cref="Rendering.SectorMeshBuilder"/> (the Godot-side mesh
/// assembler, kept a "dumb" consumer of already-resolved numbers here,
/// mirroring <see cref="LinedefWallBuilder"/>'s own real split for
/// walls) used to compute a flat's UV as plain
/// <c>position / textureSize</c>, with no sector's own
/// <c>xpanningfloor</c>/<c>ypanningfloor</c>, <c>xscalefloor</c>/
/// <c>yscalefloor</c>, or <c>rotationfloor</c>/<c>rotationceiling</c>
/// field ever read anywhere in the rendering pipeline at all - correctly
/// saved/loaded the whole time, just never actually rendered.
///
/// Adapted from UDB's own real rendering formula
/// (<c>VisualFloor.cs</c>/<c>VisualCeiling.cs</c>'s own UV setup) with
/// one deliberate, reasoned departure: UDB's own formula also includes
/// an *unconditional* <c>pos.y = -pos.y</c> after rotating - applied
/// even at zero rotation, so porting it literally would silently negate
/// V for every sector in every existing map, not just rotated ones.
/// This project's own current no-offset formula (plain
/// <c>position / textureSize</c>, no flip) is already documented and
/// presumed correct against real GZDoom rendering - confirmed nothing
/// downstream (<see cref="Rendering.DoubleSidedMesh"/> is a pure UV
/// pass-through) compensates for a flip either, so adding one here
/// would be a real, confirmed regression for the overwhelmingly common
/// case, not a faithful port. The flip is dropped; rotation is kept
/// (ordinary CCW rotation about the world origin), then pan offset is
/// added, then scale multiplies, then the result is normalized by the
/// texture's own real pixel size - at zero rotation/offset/default
/// scale this reduces to exactly the pre-existing formula, so nothing
/// already working changes. <b>Not independently verified live</b>
/// which way "positive rotation" should visually spin without this
/// flip - worth a real rotated-flat test before trusting the sign.
/// </summary>
public static class FlatTextureTransform
{
    public static Vector2 ComputeUv(Vector2 worldPosition, Sector sector, bool isCeiling, Vector2 textureSize)
    {
        var suffix = isCeiling ? "ceiling" : "floor";
        var rotation = sector.Fields.GetFloat($"rotation{suffix}", 0.0) * (MathF.PI / 180f);
        var offsetX = sector.Fields.GetFloat($"xpanning{suffix}", 0.0);
        var offsetY = sector.Fields.GetFloat($"ypanning{suffix}", 0.0);
        var scaleX = sector.Fields.GetFloat($"xscale{suffix}", 1.0);
        var scaleY = sector.Fields.GetFloat($"yscale{suffix}", 1.0);

        // Near-zero clamp before dividing by textureSize - the same
        // defensive move LinedefWallBuilder.GetPartTransform already
        // makes for a wall's own scale fields. UDB's own real code
        // doesn't bother (a real 0 there just collapses the whole flat
        // to one UV point), but there's no reason to let a stray 0
        // (rather than "unset", which already defaults to 1 above) do
        // anything worse here than it does for walls.
        if (MathF.Abs((float)scaleX) < 0.001f) scaleX = 1.0;
        if (MathF.Abs((float)scaleY) < 0.001f) scaleY = 1.0;

        var cos = MathF.Cos((float)rotation);
        var sin = MathF.Sin((float)rotation);
        var rotatedX = cos * worldPosition.X - sin * worldPosition.Y;
        var rotatedY = sin * worldPosition.X + cos * worldPosition.Y;

        var u = (rotatedX + offsetX) * scaleX / textureSize.X;
        var v = (rotatedY + offsetY) * scaleY / textureSize.Y;
        return new Vector2((float)u, (float)v);
    }
}
