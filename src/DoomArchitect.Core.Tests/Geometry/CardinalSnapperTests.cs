using System.Numerics;
using DoomArchitect.Core.Geometry;

namespace DoomArchitect.Core.Tests.Geometry;

public class CardinalSnapperTests
{
    [Fact]
    public void Snap_CursorAlreadyDueEast_ReturnsCursorUnchanged()
    {
        var result = CardinalSnapper.Snap(new Vector2(0, 0), new Vector2(100, 0));

        Assert.Equal(100, result.X, precision: 3);
        Assert.Equal(0, result.Y, precision: 3);
    }

    [Fact]
    public void Snap_CloserToDueEastThanNorthEast_SnapsToDueEast()
    {
        // 10 degrees off due east - nearer to 0 than to 45.
        var cursor = new Vector2(0, 0) + new Vector2(MathF.Cos(MathF.PI / 18f), MathF.Sin(MathF.PI / 18f)) * 100f;

        var result = CardinalSnapper.Snap(new Vector2(0, 0), cursor);

        Assert.Equal(100, result.X, precision: 3);
        Assert.Equal(0, result.Y, precision: 3);
    }

    [Fact]
    public void Snap_CloserToNorthEastThanDueEast_SnapsToNorthEast()
    {
        // 40 degrees off due east - nearer to 45 than to 0.
        var cursor = new Vector2(0, 0) + new Vector2(MathF.Cos(MathF.PI * 40f / 180f), MathF.Sin(MathF.PI * 40f / 180f)) * 100f;

        var result = CardinalSnapper.Snap(new Vector2(0, 0), cursor);

        Assert.Equal(100 * MathF.Cos(MathF.PI / 4f), result.X, precision: 3);
        Assert.Equal(100 * MathF.Sin(MathF.PI / 4f), result.Y, precision: 3);
    }

    [Fact]
    public void Snap_PreservesTheCursorsOwnDistanceFromTheLastPoint()
    {
        var from = new Vector2(20, -30);
        var cursor = from + new Vector2(MathF.Cos(MathF.PI * 17f / 180f), MathF.Sin(MathF.PI * 17f / 180f)) * 137f;

        var result = CardinalSnapper.Snap(from, cursor);

        Assert.Equal(137, (result - from).Length(), precision: 2);
    }

    [Fact]
    public void Snap_RelativeToANonOriginLastPoint_LocksAroundThatPointNotTheOrigin()
    {
        var from = new Vector2(50, 50);
        var cursor = from + new Vector2(100, 5); // close to due east of `from`, not of the origin

        var result = CardinalSnapper.Snap(from, cursor);

        Assert.Equal(from.X + (cursor - from).Length(), result.X, precision: 2);
        Assert.Equal(from.Y, result.Y, precision: 2);
    }

    [Fact]
    public void Snap_CursorExactlyOnLastPoint_ReturnsLastPointUnchanged()
    {
        var from = new Vector2(12, 34);

        var result = CardinalSnapper.Snap(from, from);

        Assert.Equal(from.X, result.X, precision: 3);
        Assert.Equal(from.Y, result.Y, precision: 3);
    }
}
