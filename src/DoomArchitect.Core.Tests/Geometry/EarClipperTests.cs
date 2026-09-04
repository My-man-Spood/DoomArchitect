using System.Numerics;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Tests.Map;

namespace DoomArchitect.Core.Tests.Geometry;

public class EarClipperTests
{
    [Fact]
    public void Clip_SimpleBox_ProducesTwoTrianglesWithTheBoxArea()
    {
        var polygon = new[]
        {
            new Vector2(0, 0), new Vector2(0, 64), new Vector2(64, 64), new Vector2(64, 0),
        };

        var triangles = EarClipper.Clip(polygon);

        Assert.Equal(2, triangles.Count);
        Assert.Equal(4096f, TotalArea(triangles), 3);
    }

    [Fact]
    public void Clip_LShapedConcavePolygon_HandlesTheReflexCorner()
    {
        // A 10x10 box with a 5x5 notch bitten out of one corner (clockwise).
        var polygon = new[]
        {
            new Vector2(0, 0), new Vector2(0, 10), new Vector2(10, 10),
            new Vector2(10, 5), new Vector2(5, 5), new Vector2(5, 0),
        };

        var triangles = EarClipper.Clip(polygon);

        Assert.Equal(polygon.Length - 2, triangles.Count);
        Assert.Equal(75f, TotalArea(triangles), 3);
    }

    [Fact]
    public void Clip_CutSectorWithHole_HandlesTheCollinearBridgePoint()
    {
        var map = new MapData();
        var (sector, _) = map.CreateClosedSector(0, 128,
            new Vector2(0, 0), new Vector2(0, 100), new Vector2(100, 100), new Vector2(100, 0));

        map.CreateClosedBoundary(sector,
            new Vector2(40, 40), new Vector2(60, 40), new Vector2(60, 60), new Vector2(40, 60));

        var tree = PolygonNesting.BuildTree(SectorTracer.Trace(sector));
        var polygon = Assert.Single(PolygonCutter.Cut(tree));

        var triangles = EarClipper.Clip(polygon);

        // Not polygon.Count - 2: the bridge revisits y=40 three times
        // (hole start -> ... -> hole start again -> cut point again),
        // making one of those points a collinear "straight" vertex that
        // the pre-triangulation cleanup pass removes before clipping
        // starts - same as UDB does.
        Assert.Equal(polygon.Count - 3, triangles.Count);
        Assert.Equal(9600f, TotalArea(triangles), 3);
    }

    private static float TotalArea(IEnumerable<(Vector2 A, Vector2 B, Vector2 C)> triangles) =>
        triangles.Sum(t => MathF.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.C.X - t.A.X) * (t.B.Y - t.A.Y)) * 0.5f);
}
