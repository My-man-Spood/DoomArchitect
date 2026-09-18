using System.Linq;
using System.Numerics;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Tests.Map;

namespace DoomArchitect.Core.Tests.Geometry;

public class PolygonWindingTests
{
    private static readonly Vector2[] ClockwiseSquare =
    {
        new(0, 0), new(0, 64), new(64, 64), new(64, 0),
    };

    private static readonly Vector2[] CounterClockwiseSquare =
    {
        new(0, 0), new(64, 0), new(64, 64), new(0, 64),
    };

    [Fact]
    public void IsClockwise_ClockwiseSquare_ReturnsTrue()
    {
        Assert.True(PolygonWinding.IsClockwise(ClockwiseSquare));
    }

    [Fact]
    public void IsClockwise_CounterClockwiseSquare_ReturnsFalse()
    {
        Assert.False(PolygonWinding.IsClockwise(CounterClockwiseSquare));
    }

    [Fact]
    public void SignedArea_ReversingPointOrder_NegatesTheSign()
    {
        var area = PolygonWinding.SignedArea(ClockwiseSquare);
        var reversedArea = PolygonWinding.SignedArea(ClockwiseSquare.Reverse().ToArray());

        Assert.Equal(-area, reversedArea);
    }

    /// <summary>
    /// Cross-checks <see cref="PolygonWinding.IsClockwise"/> against
    /// <see cref="Loop.IsClockwise"/> for the exact same points, proving
    /// the two stay consistent now that <see cref="Loop"/> delegates to
    /// this shared helper rather than duplicating the formula.
    /// </summary>
    [Fact]
    public void IsClockwise_MatchesLoopIsClockwiseForTheSamePoints()
    {
        var map = new MapData();
        var (sector, _) = map.CreateClosedSector(0, 128, ClockwiseSquare);

        var loop = Assert.Single(SectorTracer.Trace(sector));

        Assert.Equal(PolygonWinding.IsClockwise(ClockwiseSquare), loop.IsClockwise);
    }
}
