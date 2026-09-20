using System.Numerics;

namespace DoomArchitect.Core.Geometry;

/// <summary>
/// Draw mode's cardinal/45-degree direction lock (UDB's own real
/// <c>snaptocardinaldirection</c>, <c>Alt+Shift</c> in
/// <c>DrawGeometryMode.Update</c>) - constrains the next point's direction
/// from the last placed point to the nearest 45-degree increment (8
/// directions), while preserving the cursor's own actual distance from that
/// point. UDB's own real formula rounds via integer truncation with a +22
/// bias (<c>(deg+22)/45*45</c>); this uses <see cref="MathF.Round"/> instead
/// (round-to-nearest directly, no bias hack needed) - the same
/// nearest-45-degree result, just derived more directly.
///
/// UDB's own real <c>usefourcardinaldirections</c> flag (90-degree-only
/// snapping) is <c>true</c> only in its separate Draw Rectangle mode, which
/// this project has no equivalent of - Draw Lines mode (the only mode this
/// snapper serves) always uses the 45-degree, 8-direction form, so there's
/// no second variant to model here.
///
/// Deliberately not ported: UDB's own real grid-offset-kept-relative-to-the-
/// first-point refinement for when cardinal lock and grid snap combine - a
/// real UDB behavior, flagged as a known simplification rather than guessed;
/// grid snap, when also active, is applied afterward via the existing
/// <see cref="GridSnapper"/> directly on the already-locked point instead.
/// </summary>
public static class CardinalSnapper
{
    public static Vector2 Snap(Vector2 from, Vector2 cursor)
    {
        var offset = cursor - from;
        var distance = offset.Length();

        var angle = GeometryMath.Angle(from, cursor);
        var snappedAngle = GeometryMath.NormalizeAngle(MathF.Round(angle / (MathF.PI / 4f)) * (MathF.PI / 4f));

        return from + new Vector2(MathF.Cos(snappedAngle), MathF.Sin(snappedAngle)) * distance;
    }
}
