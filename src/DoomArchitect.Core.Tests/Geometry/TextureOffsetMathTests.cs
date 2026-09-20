using DoomArchitect.Core.Geometry;

namespace DoomArchitect.Core.Tests.Geometry;

public class TextureOffsetMathTests
{
    [Fact]
    public void Nudge_PlainAdd_NoWrapNeeded()
    {
        Assert.Equal(1, TextureOffsetMath.Nudge(0, 1, 64));
        Assert.Equal(9, TextureOffsetMath.Nudge(1, 8, 64));
    }

    [Fact]
    public void Nudge_PastTexturePositiveEdge_WrapsAround()
    {
        Assert.Equal(4, TextureOffsetMath.Nudge(60, 8, 64));
    }

    [Fact]
    public void Nudge_PastTextureNegativeEdge_WrapsAround()
    {
        // C#'s own remainder operator keeps the dividend's sign - matches
        // UDB's real (literally ported, not reinterpreted) wrap exactly:
        // it doesn't force a positive-only result either.
        Assert.Equal(-4, TextureOffsetMath.Nudge(4, -8, 64));
    }

    [Fact]
    public void Nudge_ZeroDelta_ReturnsOldValueUnchanged()
    {
        Assert.Equal(37, TextureOffsetMath.Nudge(37, 0, 64));
    }

    [Fact]
    public void Nudge_DeltaIsExactMultipleOfTextureSize_IsATrueNoOp()
    {
        // Matches UDB's own real short-circuit exactly - nudging by a
        // whole texture width/height is defined as no visible change at
        // all, before wrapping even runs.
        Assert.Equal(12, TextureOffsetMath.Nudge(12, 64, 64));
        Assert.Equal(12, TextureOffsetMath.Nudge(12, -128, 64));
    }

    [Fact]
    public void Nudge_UnresolvableTexture_SkipsWrappingEntirely()
    {
        Assert.Equal(108, TextureOffsetMath.Nudge(100, 8, 0));
        Assert.Equal(108, TextureOffsetMath.Nudge(100, 8, -1));
    }
}
