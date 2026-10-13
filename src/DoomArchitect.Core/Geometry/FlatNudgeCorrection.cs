using System.Numerics;

namespace DoomArchitect.Core.Geometry;

/// <summary>
/// Resolves an arrow-key nudge on a floor/ceiling texture into the real
/// <c>xpanning</c>/<c>ypanning</c> delta to apply, so Left/Right/Up/Down
/// always reads as screen-relative regardless of which way the mapper
/// currently happens to be facing - a floor/ceiling can be viewed from
/// any direction at all (unlike a wall, which only ever has two fixed
/// sides - a simple dot-product flip already handles the camera-relative
/// case there, see <c>MapView.HandleTextureNudge</c>'s own wall branch).
///
/// Derived directly from <see cref="FlatTextureTransform"/>'s own real
/// rendering formula, not ported from UDB - UDB's own real
/// <c>doSurfaceAngleCorrection</c> (a camera-angle-plus-rotation
/// quadrant snap) turned out to not transfer cleanly here: it's tightly
/// coupled to UDB's own internal angle convention (confirmed non-
/// standard - its real <c>Vector3D.FromAngleXYZ</c> measures from south,
/// winding the opposite way from `Atan2(y,x)`) and to its own
/// rendering's unconditional Y-flip (deliberately not ported into
/// <see cref="FlatTextureTransform"/>, since it would have regressed
/// every existing map's already-correct flat rendering) - after three
/// rounds of sign/axis bugs trying to adapt it piece by piece, rebuilt
/// from scratch against this project's own actual UV formula instead.
/// </summary>
public static class FlatNudgeCorrection
{
    /// <param name="cameraForward">The camera's own current forward direction, in map space - only its angle matters, not its magnitude.</param>
    /// <param name="sectorRotationDegrees">The target sector's own <c>rotationfloor</c>/<c>rotationceiling</c> field value.</param>
    /// <param name="horizontal">The raw signed nudge amount for Left(-)/Right(+).</param>
    /// <param name="vertical">The raw signed nudge amount for Down(-)/Up(+).</param>
    public static (double OffsetXDelta, double OffsetYDelta) Resolve(
        Vector2 cameraForward, double sectorRotationDegrees, double horizontal, double vertical)
    {
        // "Right" should shift the texture pattern toward the camera's
        // own screen-right in world space; "Up" toward the camera's own
        // forward (further into the room). cameraRight is cameraForward
        // rotated -90 degrees (clockwise viewed from above) - the real,
        // fixed relationship between a yaw-only (unrolled) camera's own
        // forward and right vectors, the same assumption the wall-nudge
        // branch's own camera-relative check already relies on.
        var cameraRight = new Vector2(cameraForward.Y, -cameraForward.X);

        // xpanning/ypanning are each a single discrete map-space axis -
        // a nudge has to move along exactly one of them at a time, never
        // a blend of both. The camera is essentially never pointed
        // exactly along a map axis during normal play, so using
        // cameraRight/cameraForward directly (as a first draft of this
        // did) produces a nonzero shift on *both* axes for every single
        // keypress - a permanent diagonal drift, worse the further off-
        // axis the camera happens to be aimed. Snapping each one to
        // whichever single world axis it's closest to - the same thing
        // the camera direction is used for, just discretized - picks
        // "which axis, which sign" without blending, matching how a
        // wall's own nudge already only ever moves one way.
        var desiredShift = SnapToNearestAxis(cameraRight) * (float)horizontal
            + SnapToNearestAxis(cameraForward) * (float)vertical;

        // FlatTextureTransform's own real UV formula rotates world
        // position by the sector's own rotationfloor/rotationceiling
        // before sampling - shifting that already-rotated sample by the
        // desired world-space amount means the offset delta itself needs
        // the identical rotation applied first (derived directly against
        // that formula below, not assumed).
        var rotation = sectorRotationDegrees * (MathF.PI / 180f);
        var cos = MathF.Cos((float)rotation);
        var sin = MathF.Sin((float)rotation);
        var rotatedShiftX = cos * desiredShift.X - sin * desiredShift.Y;
        var rotatedShiftY = sin * desiredShift.X + cos * desiredShift.Y;

        // u(pos) = (rotate(pos) + offset) / size - solving u_new(pos) =
        // u_old(pos - desiredShift) for every pos (the real definition
        // of "the pattern itself visibly moves by desiredShift") gives
        // offsetDelta = -rotate(desiredShift) exactly; confirmed by hand
        // with a concrete worked example first (a 1-pixel positive
        // offset moves a reference stripe at x=0 to x=-1, i.e. the
        // pattern moves opposite the offset's own sign), not assumed.
        return (-rotatedShiftX, -rotatedShiftY);
    }

    /// <summary>Collapses a direction onto whichever single world axis (+X, -X, +Y, or -Y) it's closest to, keeping only that axis's sign and zeroing the other - so a nudge always reads as "move along this one axis," never a blend of both, regardless of how far off-cardinal the camera actually happens to be aimed.</summary>
    private static Vector2 SnapToNearestAxis(Vector2 direction)
    {
        return MathF.Abs(direction.X) >= MathF.Abs(direction.Y)
            ? new Vector2(direction.X >= 0 ? 1f : -1f, 0f)
            : new Vector2(0f, direction.Y >= 0 ? 1f : -1f);
    }
}
