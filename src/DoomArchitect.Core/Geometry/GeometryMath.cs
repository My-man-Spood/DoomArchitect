using System.Numerics;

namespace DoomArchitect.Core.Geometry;

internal static class GeometryMath
{
    /// <summary>Negative when p is right of a-&gt;b, positive when left, zero when on the line.</summary>
    public static float SideOfLine(Vector2 a, Vector2 b, Vector2 p) =>
        (p.Y - a.Y) * (b.X - a.X) - (p.X - a.X) * (b.Y - a.Y);

    /// <summary>Where p projects onto line a-&gt;b, as a fraction of its length (0 = at a, 1 = at b; can fall outside [0,1]).</summary>
    public static float NearestOnLine(Vector2 a, Vector2 b, Vector2 p)
    {
        var ab = b - a;
        return Vector2.Dot(p - a, ab) / ab.LengthSquared();
    }

    /// <summary>Proper bounded segment-vs-segment intersection test.</summary>
    public static bool SegmentsIntersect(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
    {
        var divisor = (b2.Y - b1.Y) * (a2.X - a1.X) - (b2.X - b1.X) * (a2.Y - a1.Y);
        if (divisor == 0f) return false;

        var uLine = ((b2.X - b1.X) * (a1.Y - b1.Y) - (b2.Y - b1.Y) * (a1.X - b1.X)) / divisor;
        var uRay = ((a2.X - a1.X) * (a1.Y - b1.Y) - (a2.Y - a1.Y) * (a1.X - b1.X)) / divisor;

        return uRay is >= 0f and <= 1f && uLine is >= 0f and <= 1f;
    }

    /// <summary>
    /// Same bounded segment-vs-segment math as <see cref="SegmentsIntersect"/>,
    /// also returning where along b (<paramref name="uB"/>, 0 at
    /// <paramref name="b1"/>, 1 at <paramref name="b2"/>) and the actual
    /// intersection point - mirrors UDB's real <c>Line2D.GetIntersection</c>,
    /// whose own <c>u</c> output is along its "other" argument (verified by
    /// its own real call site, <c>Tools.DrawLines</c>, using the returned
    /// <c>u</c> against the segment passed as "other" to get the split
    /// coordinates), the same convention kept here.
    /// </summary>
    public static bool TryGetSegmentIntersection(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2, out float uB, out Vector2 point)
    {
        var divisor = (b2.Y - b1.Y) * (a2.X - a1.X) - (b2.X - b1.X) * (a2.Y - a1.Y);
        if (divisor == 0f)
        {
            uB = 0f;
            point = default;
            return false;
        }

        var uA = ((b2.X - b1.X) * (a1.Y - b1.Y) - (b2.Y - b1.Y) * (a1.X - b1.X)) / divisor;
        uB = ((a2.X - a1.X) * (a1.Y - b1.Y) - (a2.Y - a1.Y) * (a1.X - b1.X)) / divisor;

        if (uA is < 0f or > 1f || uB is < 0f or > 1f)
        {
            point = default;
            return false;
        }

        point = b1 + (b2 - b1) * uB;
        return true;
    }

    /// <summary>Squared distance from p to the closest point on the *bounded* segment a-&gt;b (clipped to its own endpoints, not the infinite line).</summary>
    public static float DistanceToSegmentSquared(Vector2 a, Vector2 b, Vector2 p)
    {
        var ab = b - a;
        var lengthSquared = ab.LengthSquared();
        if (lengthSquared <= 0f) return (p - a).LengthSquared();

        var t = System.Math.Clamp(Vector2.Dot(p - a, ab) / lengthSquared, 0f, 1f);
        return (p - (a + ab * t)).LengthSquared();
    }

    /// <summary>Direction from a to b, normalized to [0, 2*PI).</summary>
    public static float Angle(Vector2 a, Vector2 b)
    {
        var delta = b - a;
        return NormalizeAngle(MathF.Atan2(delta.Y, delta.X));
    }

    public static float NormalizeAngle(float angle)
    {
        while (angle < 0f) angle += MathF.PI * 2f;
        while (angle >= MathF.PI * 2f) angle -= MathF.PI * 2f;
        return angle;
    }

    /// <summary>Unsigned difference between two angles, in [0, PI].</summary>
    public static float AngleDifference(float a, float b)
    {
        var d = NormalizeAngle(a) - NormalizeAngle(b);
        if (d < 0f) d += MathF.PI * 2f;
        if (d > MathF.PI) d = MathF.PI * 2f - d;
        return d;
    }
}
