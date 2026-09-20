using System.Linq;
using System.Numerics;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Tests.Geometry;

public class BoundaryTracerTests
{
    [Fact]
    public void FindPotentialSectorAt_PlainBox_ReturnsAllFourSidesInOrder()
    {
        var map = new MapData();
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(0, 64));
        var c = map.CreateVertex(new Vector2(64, 64));
        var d = map.CreateVertex(new Vector2(64, 0));

        var ab = map.CreateLinedef(a, b, null, null);
        var bc = map.CreateLinedef(b, c, null, null);
        var cd = map.CreateLinedef(c, d, null, null);
        var da = map.CreateLinedef(d, a, null, null);

        var result = BoundaryTracer.FindPotentialSectorAt(map, ab, front: true);

        Assert.NotNull(result);
        Assert.Equal(4, result!.Count);
        Assert.Equal(new[] { ab, bc, cd, da }, result.Select(s => s.Linedef));
        Assert.All(result, s => Assert.True(s.Front));
    }

    [Fact]
    public void FindPotentialSectorAt_OppositeSideOfTheBox_ReturnsFalseFrontEverywhere()
    {
        var map = new MapData();
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(0, 64));
        var c = map.CreateVertex(new Vector2(64, 64));
        var d = map.CreateVertex(new Vector2(64, 0));

        var ab = map.CreateLinedef(a, b, null, null);
        map.CreateLinedef(b, c, null, null);
        map.CreateLinedef(c, d, null, null);
        map.CreateLinedef(d, a, null, null);

        // The exterior (void) side of this same clockwise box - front=false
        // walks End-to-Start, so the outer boundary is traced the other
        // way around (b -> a -> d -> c -> b).
        var result = BoundaryTracer.FindPotentialSectorAt(map, ab, front: false);

        Assert.Null(result);
    }

    [Fact]
    public void DetermineFrontInterior_FrontAlreadyTracesTheInterior_ReturnsTrue()
    {
        var map = new MapData();
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(0, 64));
        var c = map.CreateVertex(new Vector2(64, 64));
        var d = map.CreateVertex(new Vector2(64, 0));

        var ab = map.CreateLinedef(a, b, null, null);
        map.CreateLinedef(b, c, null, null);
        map.CreateLinedef(c, d, null, null);
        map.CreateLinedef(d, a, null, null);

        Assert.True(BoundaryTracer.DetermineFrontInterior(map, ab));
    }

    [Fact]
    public void DetermineFrontInterior_EdgeConstructedBackward_FallsBackToTheBackSideCorrectly()
    {
        var map = new MapData();
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(0, 64));
        var c = map.CreateVertex(new Vector2(64, 64));
        var d = map.CreateVertex(new Vector2(64, 0));

        // Reversed relative to the box's own clockwise a->b->c->d->a
        // construction - front (Start->End, b->a) is the void side here,
        // back (End->Start, a->b) is the real interior.
        var ba = map.CreateLinedef(b, a, null, null);
        map.CreateLinedef(b, c, null, null);
        map.CreateLinedef(c, d, null, null);
        map.CreateLinedef(d, a, null, null);

        Assert.False(BoundaryTracer.DetermineFrontInterior(map, ba));
    }

    [Fact]
    public void FindPotentialSectorAt_BoxSplitByADiagonal_FindsOnlyOneTriangle()
    {
        var map = new MapData();
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(0, 64));
        var c = map.CreateVertex(new Vector2(64, 64));
        var d = map.CreateVertex(new Vector2(64, 0));

        var ab = map.CreateLinedef(a, b, null, null);
        var bc = map.CreateLinedef(b, c, null, null);
        map.CreateLinedef(c, d, null, null);
        map.CreateLinedef(d, a, null, null);
        var ca = map.CreateLinedef(c, a, null, null);

        var result = BoundaryTracer.FindPotentialSectorAt(map, ab, front: true);

        Assert.NotNull(result);
        Assert.Equal(3, result!.Count);
        Assert.Equal(new[] { ab, bc, ca }, result.Select(s => s.Linedef));
    }

    [Fact]
    public void FindPotentialSectorAt_BoxWithAHoleInsideIt_ReturnsBothOuterAndInnerSides()
    {
        var map = new MapData();
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(0, 100));
        var c = map.CreateVertex(new Vector2(100, 100));
        var d = map.CreateVertex(new Vector2(100, 0));
        var outerAb = map.CreateLinedef(a, b, null, null);
        map.CreateLinedef(b, c, null, null);
        map.CreateLinedef(c, d, null, null);
        map.CreateLinedef(d, a, null, null);

        // A small hole floating inside the box - counter-clockwise, same
        // convention CreateClosedBoundary's own doc comment establishes
        // for a hole.
        var h0 = map.CreateVertex(new Vector2(40, 40));
        var h1 = map.CreateVertex(new Vector2(60, 40));
        var h2 = map.CreateVertex(new Vector2(60, 60));
        var h3 = map.CreateVertex(new Vector2(40, 60));
        map.CreateLinedef(h0, h1, null, null);
        map.CreateLinedef(h1, h2, null, null);
        map.CreateLinedef(h2, h3, null, null);
        map.CreateLinedef(h3, h0, null, null);

        var result = BoundaryTracer.FindPotentialSectorAt(map, outerAb, front: true);

        Assert.NotNull(result);
        Assert.Equal(8, result!.Count);
        Assert.Contains(result, s => s.Linedef.Start.Position == new Vector2(40, 40));
    }

    [Fact]
    public void FindPotentialSectorAt_WithADanglingStubOffOneCorner_StillFindsOnlyTheBoxItself()
    {
        var map = new MapData();
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(0, 64));
        var c = map.CreateVertex(new Vector2(64, 64));
        var d = map.CreateVertex(new Vector2(64, 0));

        var ab = map.CreateLinedef(a, b, null, null);
        var bc = map.CreateLinedef(b, c, null, null);
        var cd = map.CreateLinedef(c, d, null, null);
        var da = map.CreateLinedef(d, a, null, null);

        // A dangling stub sticking straight out of corner A, away from the
        // box entirely - should never end up part of the traced boundary.
        var stubEnd = map.CreateVertex(new Vector2(-40, -40));
        var stub = map.CreateLinedef(a, stubEnd, null, null);

        var result = BoundaryTracer.FindPotentialSectorAt(map, ab, front: true);

        Assert.NotNull(result);
        Assert.Equal(4, result!.Count);
        Assert.Equal(new[] { ab, bc, cd, da }, result.Select(s => s.Linedef));
        Assert.DoesNotContain(result, s => s.Linedef == stub);
    }

    [Fact]
    public void FindPotentialSectorAt_TooFewVerticesToEncloseAnArea_ReturnsNull()
    {
        var map = new MapData();
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(0, 64));
        var ab = map.CreateLinedef(a, b, null, null);
        map.CreateLinedef(b, a, null, null);

        var result = BoundaryTracer.FindPotentialSectorAt(map, ab, front: true);

        Assert.Null(result);
    }

    /// <summary>
    /// A deliberately adversarial trap for the plain box's own closing
    /// corner (vertex A): a small triangular appendage hangs off A at an
    /// angle engineered (verified numerically against
    /// <see cref="LinedefAngleSorter.RelativeAngle"/> directly, not
    /// guessed) to out-score the box's own real closing edge (AB) in the
    /// tightest-turn comparison the walk uses at that vertex - without
    /// <see cref="BoundaryTracer"/>'s own rightward-ray-cast retry (see
    /// <c>FindOuterLines</c>'s own remarks), a trace starting from AB
    /// would wander out onto this appendage the moment it reaches A,
    /// rather than closing straight back onto AB there.
    ///
    /// This doesn't decisively distinguish "retried" from "succeeded on
    /// the first attempt anyway" - real experimentation (see this
    /// project's own commit history) found <c>Loop.Contains</c>'s ray-
    /// crossing test tends to still correctly place the box's own side-
    /// point inside a self-touching combined trace like this one even
    /// without a retry, so this specific construction can't prove the
    /// retry path itself fired. What it does confirm, and is worth
    /// keeping regardless: the walk still finds its way back to close on
    /// AB - the real target - rather than getting lost in the appendage
    /// or failing outright, and the box's own 4 sides are still present
    /// (alongside the appendage) in the result either way.
    /// </summary>
    [Fact]
    public void FindPotentialSectorAt_WithAnAngleWinningAppendageAtTheClosingVertex_StillClosesBackOnTheRealEdge()
    {
        var map = new MapData();
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(0, 64));
        var c = map.CreateVertex(new Vector2(64, 64));
        var d = map.CreateVertex(new Vector2(64, 0));
        var ab = map.CreateLinedef(a, b, null, null);
        var bc = map.CreateLinedef(b, c, null, null);
        var cd = map.CreateLinedef(c, d, null, null);
        var da = map.CreateLinedef(d, a, null, null);

        // A small triangle hanging off vertex A, at an angle numerically
        // confirmed (via LinedefAngleSorter.RelativeAngle directly) to
        // score higher than AB itself in the walk's own tightest-turn
        // comparison arriving via DA.
        var h = map.CreateVertex(new Vector2(14.14f, 14.14f));
        var i = map.CreateVertex(new Vector2(7.5f, 12.99f));
        var ah = map.CreateLinedef(a, h, null, null);
        var hi = map.CreateLinedef(h, i, null, null);
        var ia = map.CreateLinedef(i, a, null, null);

        var result = BoundaryTracer.FindPotentialSectorAt(map, ab, front: true);

        Assert.NotNull(result);
        Assert.Equal(new[] { ab, bc, cd, da, ah, hi, ia }, result!.Select(s => s.Linedef));
    }
}
