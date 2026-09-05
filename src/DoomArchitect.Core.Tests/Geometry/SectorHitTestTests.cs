using System.Numerics;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Tests.Map;

namespace DoomArchitect.Core.Tests.Geometry;

public class SectorHitTestTests
{
    [Fact]
    public void Contains_PointInsideSector_ReturnsTrue()
    {
        var map = new MapData();
        var (sector, _) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 100), new Vector2(100, 100), new Vector2(100, 0));

        Assert.True(SectorHitTest.Contains(sector, new Vector2(50, 50)));
    }

    [Fact]
    public void Contains_PointOutsideSector_ReturnsFalse()
    {
        var map = new MapData();
        var (sector, _) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 100), new Vector2(100, 100), new Vector2(100, 0));

        Assert.False(SectorHitTest.Contains(sector, new Vector2(150, 150)));
    }

    [Fact]
    public void Contains_PointInsideHole_ReturnsFalse()
    {
        var map = new MapData();
        var (sector, _) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 100), new Vector2(100, 100), new Vector2(100, 0));

        map.CreateClosedBoundary(sector,
            new Vector2(20, 20), new Vector2(40, 20), new Vector2(40, 40), new Vector2(20, 40));

        Assert.False(SectorHitTest.Contains(sector, new Vector2(22, 22)));
    }

    [Fact]
    public void Contains_PointInsideIslandNestedInHole_ReturnsTrue()
    {
        var map = new MapData();
        var (sector, _) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 100), new Vector2(100, 100), new Vector2(100, 0));

        map.CreateClosedBoundary(sector,
            new Vector2(20, 20), new Vector2(40, 20), new Vector2(40, 40), new Vector2(20, 40));

        map.CreateClosedBoundary(sector,
            new Vector2(25, 25), new Vector2(25, 35), new Vector2(35, 35), new Vector2(35, 25));

        Assert.True(SectorHitTest.Contains(sector, new Vector2(30, 30)));
    }
}
