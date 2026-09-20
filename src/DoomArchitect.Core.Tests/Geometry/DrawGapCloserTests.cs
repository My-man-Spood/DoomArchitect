using System.Linq;
using System.Numerics;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Tests.Geometry;

public class DrawGapCloserTests
{
    [Fact]
    public void FindClosingPath_BothEndsStitchToTheSameLinedef_ReturnsTheSingleLineShortcut()
    {
        var map = new MapData();
        var existingLine = map.CreateLinedef(map.CreateVertex(new Vector2(0, 0)), map.CreateVertex(new Vector2(100, 0)), null, null);

        var firstLine = map.CreateLinedef(map.CreateVertex(new Vector2(0, -50)), map.CreateVertex(new Vector2(50, -50)), null, null);
        var lastLine = map.CreateLinedef(map.CreateVertex(new Vector2(50, -50)), map.CreateVertex(new Vector2(100, -50)), null, null);

        var result = DrawGapCloser.FindClosingPath(firstLine, existingLine, null, lastLine, existingLine, null);

        Assert.NotNull(result);
        var path = Assert.Single(result.Value.Path);
        Assert.Same(existingLine, path.Linedef);
    }

    [Fact]
    public void FindClosingPath_NeitherEndStitchesToAnything_ReturnsNull()
    {
        var map = new MapData();
        var firstLine = map.CreateLinedef(map.CreateVertex(new Vector2(0, 0)), map.CreateVertex(new Vector2(10, 0)), null, null);
        var lastLine = map.CreateLinedef(map.CreateVertex(new Vector2(20, 0)), map.CreateVertex(new Vector2(30, 0)), null, null);

        var result = DrawGapCloser.FindClosingPath(firstLine, null, null, lastLine, null, null);

        Assert.Null(result);
    }

    /// <summary>
    /// A simple existing A-B-C-D chain (each intermediate vertex has
    /// exactly the chain's own two linedefs, no branching at all) - the
    /// drawn polyline's own two ends stitch onto the chain's two dangling
    /// outer vertices (A and D), each with only a single attached linedef,
    /// so there's no angle-sort ambiguity to reason about at all: exactly
    /// one route exists through the chain regardless of which of the
    /// search's own multiple candidate combinations happens to find it.
    /// </summary>
    [Fact]
    public void FindClosingPath_BothEndsStitchToVerticesOfADisjointExistingChain_FindsARouteThroughAllOfIt()
    {
        var map = new MapData();
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(10, 0));
        var c = map.CreateVertex(new Vector2(20, 0));
        var d = map.CreateVertex(new Vector2(30, 0));
        var ab = map.CreateLinedef(a, b, null, null);
        var bc = map.CreateLinedef(b, c, null, null);
        var cd = map.CreateLinedef(c, d, null, null);

        var firstLine = map.CreateLinedef(map.CreateVertex(new Vector2(0, -50)), a, null, null);
        var lastLine = map.CreateLinedef(d, map.CreateVertex(new Vector2(30, -50)), null, null);

        var result = DrawGapCloser.FindClosingPath(firstLine, null, a, lastLine, null, d);

        Assert.NotNull(result);
        var touched = result.Value.Path.Select(side => side.Linedef).ToHashSet();
        Assert.Contains(ab, touched);
        Assert.Contains(bc, touched);
        Assert.Contains(cd, touched);
    }
}
