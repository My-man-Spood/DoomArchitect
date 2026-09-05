using System.Numerics;
using DoomArchitect.Core.Geometry;

namespace DoomArchitect.Core.Tests.Geometry;

public class GridSnapperTests
{
    [Fact]
    public void Snap_PointAlreadyOnGrid_ReturnsSamePoint()
    {
        var result = GridSnapper.Snap(new Vector2(64, 128), 64);

        Assert.Equal(new Vector2(64, 128), result);
    }

    [Fact]
    public void Snap_PointNearGridLine_RoundsToNearestMultiple()
    {
        var result = GridSnapper.Snap(new Vector2(70, 100), 64);

        Assert.Equal(new Vector2(64, 128), result);
    }

    [Fact]
    public void Snap_ExactMidpoint_RoundsToEven()
    {
        // 32 / 64 = 0.5 and 96 / 64 = 1.5 are exact ties - .NET's default
        // MathF.Round (and UDB's own unqualified Math.Round call) breaks
        // ties towards the nearest even integer, not away from zero.
        var result = GridSnapper.Snap(new Vector2(32, 96), 64);

        Assert.Equal(new Vector2(0, 128), result);
    }

    [Fact]
    public void Snap_NegativeCoordinates_RoundsTowardsNearestMultiple()
    {
        var result = GridSnapper.Snap(new Vector2(-70, -10), 64);

        Assert.Equal(new Vector2(-64, 0), result);
    }
}
