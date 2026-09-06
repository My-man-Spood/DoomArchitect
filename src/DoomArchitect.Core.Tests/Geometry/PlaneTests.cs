using System.Numerics;
using DoomArchitect.Core.Geometry;

namespace DoomArchitect.Core.Tests.Geometry;

public class PlaneTests
{
    [Fact]
    public void GetIntersection_RayStraightUpIntoHorizontalPlane_FindsCorrectDistance()
    {
        var plane = PlaneMath.Horizontal(100);

        var found = plane.GetIntersection(Vector3.Zero, new Vector3(0, 0, 1), out var t);

        Assert.True(found);
        Assert.Equal(100, t, precision: 5);
    }

    [Fact]
    public void GetIntersection_RayStraightDownIntoHorizontalPlaneBelow_NegativeDistance()
    {
        // The plane is behind the ray's origin - still a valid answer,
        // just negative; callers decide whether that counts as a hit.
        var plane = PlaneMath.Horizontal(-50);

        var found = plane.GetIntersection(Vector3.Zero, new Vector3(0, 0, 1), out var t);

        Assert.True(found);
        Assert.Equal(-50, t, precision: 5);
    }

    [Fact]
    public void GetIntersection_RayParallelToPlane_ReturnsFalse()
    {
        var plane = PlaneMath.Horizontal(100);

        var found = plane.GetIntersection(new Vector3(0, 0, 0), new Vector3(1, 0, 0), out _);

        Assert.False(found);
    }

    [Fact]
    public void GetIntersection_AngledRay_FindsCorrectHitPoint()
    {
        var plane = PlaneMath.Horizontal(64);
        var origin = new Vector3(0, 0, 0);
        var direction = Vector3.Normalize(new Vector3(1, 0, 1)); // 45 degrees up

        var found = plane.GetIntersection(origin, direction, out var t);

        Assert.True(found);
        var hit = origin + direction * (float)t;
        Assert.Equal(64, hit.Z, precision: 4);
        Assert.Equal(64, hit.X, precision: 4); // 45 degrees: X == Z at the hit point
    }
}
