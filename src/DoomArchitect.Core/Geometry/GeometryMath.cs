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
