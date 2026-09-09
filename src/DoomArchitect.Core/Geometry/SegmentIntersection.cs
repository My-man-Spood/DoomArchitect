using System.Numerics;

namespace DoomArchitect.Core.Geometry;

/// <summary>
/// A standard orientation-based 2D segment-intersection test (four
/// cross-product signs) - not a port of any specific UDB method, just
/// well-known general-purpose math, written fresh rather than sourced.
/// No special handling for collinear-overlapping segments - matches the
/// level of rigor a marquee-select rectangle-edge test needs, not a
/// general-purpose robust geometry primitive.
/// </summary>
public static class SegmentIntersection
{
    public static bool Intersects(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
    {
        var d1 = Cross(b2 - b1, a1 - b1);
        var d2 = Cross(b2 - b1, a2 - b1);
        var d3 = Cross(a2 - a1, b1 - a1);
        var d4 = Cross(a2 - a1, b2 - a1);

        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
            && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
}
