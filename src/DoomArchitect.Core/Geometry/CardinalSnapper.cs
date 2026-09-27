using System.Numerics;

namespace DoomArchitect.Core.Geometry;

/// <summary>
/// Draw mode's cardinal/45-degree direction lock (<c>Alt+Shift</c> while
/// drawing) - constrains the next point's direction from the last placed
/// point to the nearest 45-degree increment (8 directions), while
/// preserving the cursor's own actual distance from that point. Rounds via
/// <see cref="MathF.Round"/> (round-to-nearest) rather than UDB's own
/// integer-truncation-with-+22-bias formula (<c>(deg+22)/45*45</c>) - same
/// nearest-45-degree result, just derived more directly.
///
/// 90-degree-only snapping only applies to a separate Draw Rectangle mode,
/// which this project has no equivalent of - Draw Lines mode (the only
/// mode this snapper serves) always uses the 45-degree, 8-direction form,
/// so there's no second variant to model here.
///
/// Deliberately not ported: keeping the grid offset relative to the first
/// point when cardinal lock and grid snap combine - a known simplification;
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
