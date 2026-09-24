using System.Numerics;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Tests.Map;

namespace DoomArchitect.Core.Tests.Geometry;

public class SectorBoundsTests
{
    [Fact]
    public void Compute_PlainBox_ReturnsItsOwnCorners()
    {
        var map = new MapData();
        var (sector, _) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 100), new Vector2(64, 100), new Vector2(64, 0));

        var bounds = SectorBounds.Compute(sector);

        Assert.Equal(new Vector2(0, 0), bounds.Min);
        Assert.Equal(new Vector2(64, 100), bounds.Max);
        Assert.Equal(new Vector2(32, 50), bounds.Center);
    }

    [Fact]
    public void Compute_SectorWithAHole_HoleDoesNotExpandTheBounds()
    {
        var map = new MapData();
        var (sector, _) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 100), new Vector2(100, 100), new Vector2(100, 0));
        map.CreateClosedBoundary(sector,
            new Vector2(40, 40), new Vector2(60, 40), new Vector2(60, 60), new Vector2(40, 60));

        var bounds = SectorBounds.Compute(sector);

        Assert.Equal(new Vector2(0, 0), bounds.Min);
        Assert.Equal(new Vector2(100, 100), bounds.Max);
    }

    [Fact]
    public void Compute_SectorWithNoTracedLoops_ReturnsZero()
    {
        var map = new MapData();
        var sector = map.CreateSector(0, 128); // no sidedefs at all

        var bounds = SectorBounds.Compute(sector);

        Assert.Equal(Vector2.Zero, bounds.Min);
        Assert.Equal(Vector2.Zero, bounds.Max);
    }
}
