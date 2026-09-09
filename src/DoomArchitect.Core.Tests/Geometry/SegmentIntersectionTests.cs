using System.Numerics;
using DoomArchitect.Core.Geometry;

namespace DoomArchitect.Core.Tests.Geometry;

public class SegmentIntersectionTests
{
    [Fact]
    public void Intersects_CrossingSegments_ReturnsTrue()
    {
        var a1 = new Vector2(0, 0);
        var a2 = new Vector2(10, 10);
        var b1 = new Vector2(0, 10);
        var b2 = new Vector2(10, 0);

        Assert.True(SegmentIntersection.Intersects(a1, a2, b1, b2));
    }

    [Fact]
    public void Intersects_NonCrossingSegments_ReturnsFalse()
    {
        var a1 = new Vector2(0, 0);
        var a2 = new Vector2(10, 0);
        var b1 = new Vector2(0, 10);
        var b2 = new Vector2(10, 10);

        Assert.False(SegmentIntersection.Intersects(a1, a2, b1, b2));
    }

    [Fact]
    public void Intersects_ParallelNonOverlappingSegments_ReturnsFalse()
    {
        var a1 = new Vector2(0, 0);
        var a2 = new Vector2(10, 0);
        var b1 = new Vector2(0, 5);
        var b2 = new Vector2(10, 5);

        Assert.False(SegmentIntersection.Intersects(a1, a2, b1, b2));
    }
}
