using System.Numerics;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Tests.Geometry;

public class FlatTextureTransformTests
{
    [Fact]
    public void ComputeUv_NoOffsetRotationOrScale_MatchesThePreExistingPlainFormula()
    {
        var sector = new MapData().CreateSector(0, 128);

        var uv = FlatTextureTransform.ComputeUv(new Vector2(10, 20), sector, isCeiling: false, new Vector2(64, 64));

        Assert.Equal(10f / 64f, uv.X, 4);
        Assert.Equal(20f / 64f, uv.Y, 4);
    }

    [Fact]
    public void ComputeUv_WithPanOffset_AddsItInTexelSpace()
    {
        var sector = new MapData().CreateSector(0, 128);
        sector.Fields.SetFloat("xpanningfloor", 16.0);
        sector.Fields.SetFloat("ypanningfloor", 8.0);

        var uv = FlatTextureTransform.ComputeUv(new Vector2(10, 20), sector, isCeiling: false, new Vector2(64, 64));

        Assert.Equal(26f / 64f, uv.X, 4);
        Assert.Equal(28f / 64f, uv.Y, 4);
    }

    [Fact]
    public void ComputeUv_NinetyDegreeRotation_RotatesAboutTheOrigin()
    {
        var sector = new MapData().CreateSector(0, 128);
        sector.Fields.SetFloat("rotationfloor", 90.0);

        var uv = FlatTextureTransform.ComputeUv(new Vector2(10, 0), sector, isCeiling: false, new Vector2(64, 64));

        Assert.Equal(0f, uv.X, 3);
        Assert.Equal(10f / 64f, uv.Y, 3);
    }

    [Fact]
    public void ComputeUv_OffsetAndScaleTogether_OffsetIsAddedBeforeScaleMultiplies()
    {
        var sector = new MapData().CreateSector(0, 128);
        sector.Fields.SetFloat("xpanningfloor", 10.0);
        sector.Fields.SetFloat("ypanningfloor", 10.0);
        sector.Fields.SetFloat("xscalefloor", 2.0);
        sector.Fields.SetFloat("yscalefloor", 2.0);

        // At world position (0,0), the two possible orderings disagree:
        // (position + offset) * scale = (0+10)*2 = 20, but
        // position * scale + offset = 0*2+10 = 10 - this would silently
        // pass with either implementation if the position weren't
        // exactly 0, which is why it's chosen here.
        var uv = FlatTextureTransform.ComputeUv(Vector2.Zero, sector, isCeiling: false, new Vector2(64, 64));

        Assert.Equal(20f / 64f, uv.X, 4);
        Assert.Equal(20f / 64f, uv.Y, 4);
    }

    [Fact]
    public void ComputeUv_CeilingUsesItsOwnFieldsIndependentlyOfFloor()
    {
        var sector = new MapData().CreateSector(0, 128);
        sector.Fields.SetFloat("xpanningfloor", 100.0);
        sector.Fields.SetFloat("xpanningceiling", 5.0);

        var uv = FlatTextureTransform.ComputeUv(Vector2.Zero, sector, isCeiling: true, new Vector2(64, 64));

        Assert.Equal(5f / 64f, uv.X, 4);
    }

    [Fact]
    public void ComputeUv_NearZeroScale_ClampsToOneRatherThanDividingByZero()
    {
        var sector = new MapData().CreateSector(0, 128);
        // Set directly, not via SetFloat(... defaultValue: 0.0) - that
        // helper omits the key entirely when the value equals its own
        // default, which would leave this testing nothing (GetFloat's
        // own "unset" default of 1.0 would kick in instead of the real
        // clamp path this test means to exercise).
        sector.Fields["xscalefloor"] = new UniValue(UniversalType.Float, 0.0);

        var uv = FlatTextureTransform.ComputeUv(new Vector2(32, 0), sector, isCeiling: false, new Vector2(64, 64));

        Assert.Equal(32f / 64f, uv.X, 4);
    }
}
