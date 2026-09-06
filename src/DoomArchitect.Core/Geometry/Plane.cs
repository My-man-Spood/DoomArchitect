using System.Numerics;

namespace DoomArchitect.Core.Geometry;

/// <summary>
/// Ray-vs-plane intersection on top of the BCL's own <see cref="Plane"/>
/// (already exactly the general <c>Normal</c>/<c>D</c> representation
/// needed here - a point <c>p</c> lies on the plane exactly when
/// <c>Plane.DotCoordinate(plane, p) == 0</c>). Ported from UDB's own
/// <c>Source/Core/Geometry/Plane.cs</c>, which is itself already fully
/// general (never assumes a horizontal plane) - <see cref="Horizontal"/>
/// is just this codebase's only current way to build one, since
/// <see cref="Map.Sector"/> has no slope data yet. Writing the actual
/// ray-intersection math generally, rather than hardcoding "intersect
/// Z = height", means floor/ceiling hit-testing (<see cref="MapRaycaster"/>)
/// won't need touching at all once slopes exist - only how a sector's
/// plane gets built would change.
/// </summary>
public static class PlaneMath
{
    /// <summary>A flat plane at the given height, normal pointing up (+Z).</summary>
    public static Plane Horizontal(double height) => new(new Vector3(0, 0, 1), (float)-height);

    /// <summary>
    /// Where a ray (<paramref name="origin"/> + t*<paramref name="direction"/>)
    /// crosses <paramref name="plane"/>. Returns false only when the ray
    /// runs exactly parallel to the plane (no intersection at any t).
    /// Doesn't check <c>t &gt; 0</c> itself - a negative t (the plane is
    /// behind the ray's origin) is still a mathematically valid answer;
    /// matches UDB's own <c>Plane.GetIntersection</c>, which leaves that
    /// check to the caller.
    /// </summary>
    public static bool GetIntersection(this Plane plane, Vector3 origin, Vector3 direction, out double t)
    {
        var denominator = Plane.DotNormal(plane, -direction);
        if (denominator == 0)
        {
            t = 0;
            return false;
        }

        t = Plane.DotCoordinate(plane, origin) / denominator;
        return true;
    }
}
