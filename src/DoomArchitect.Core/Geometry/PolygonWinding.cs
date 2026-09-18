using System.Numerics;

namespace DoomArchitect.Core.Geometry;

/// <summary>
/// The shoelace formula, shared between <see cref="Loop"/> (which traces
/// an already-built sector's boundary) and anything that needs to know a
/// bare, not-yet-built polygon's own winding before any <see cref="Loop"/>
/// exists for it - e.g. deciding which side of a freshly drawn closed
/// loop of vertices should become the new sector's interior. Matches
/// <see cref="Loop"/>'s own real sign convention exactly: negative area
/// is clockwise, which is an outer boundary (a real sector), not a hole.
/// </summary>
public static class PolygonWinding
{
    public static float SignedArea(IReadOnlyList<Vector2> points)
    {
        var sum = 0f;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }

        return sum * 0.5f;
    }

    public static bool IsClockwise(IReadOnlyList<Vector2> points) => SignedArea(points) < 0;
}
