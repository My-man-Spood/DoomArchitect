using System.Numerics;
using DoomArchitect.Core.Lighting;

namespace DoomArchitect.Core.Tests.Lighting;

public class SectorBrightnessTests
{
    [Theory]
    [InlineData(255, 255)] // above the curve's threshold - unchanged
    [InlineData(192, 192)] // exactly at the threshold boundary - unchanged
    [InlineData(160, 144)] // 192 - (192-160)*1.5 = 144 (the default sector brightness)
    [InlineData(96, 48)] // 192 - (96)*1.5 = 48
    [InlineData(0, 0)] // 192 - 192*1.5 = -96, clamped to 0
    public void Calculate_AppliesTheDoomLightLevelsCurve(int input, int expected)
    {
        Assert.Equal(expected, SectorBrightness.Calculate(input));
    }

    [Fact]
    public void CalculateForWall_HorizontalWall_AppliesNegativeShadeThenTheCurve()
    {
        // dy == 0 - a wall running east-west. 160 - 16 = 144, then the
        // curve: 192 - (192-144)*1.5 = 120.
        var result = SectorBrightness.CalculateForWall(160, new Vector2(64, 0));

        Assert.Equal(120, result);
    }

    [Fact]
    public void CalculateForWall_VerticalWall_AppliesPositiveShadeThenTheCurve()
    {
        // dx == 0 - a wall running north-south. 160 + 16 = 176, then the
        // curve: 192 - (192-176)*1.5 = 168.
        var result = SectorBrightness.CalculateForWall(160, new Vector2(0, 64));

        Assert.Equal(168, result);
    }

    [Fact]
    public void CalculateForWall_DiagonalWall_GetsNoShadeAdjustment()
    {
        // Neither axis-delta is exactly zero - matches plain Calculate.
        var result = SectorBrightness.CalculateForWall(160, new Vector2(64, 64));

        Assert.Equal(SectorBrightness.Calculate(160), result);
    }

    [Fact]
    public void CalculateForWall_AtTheFakeContrastThreshold_SkipsShadingEntirely()
    {
        // level == 253 fails the "< 253" check, so no adjustment happens
        // even for an axis-aligned wall - the curve leaves 253 unchanged.
        var result = SectorBrightness.CalculateForWall(253, new Vector2(64, 0));

        Assert.Equal(253, result);
    }

    [Fact]
    public void CalculateForWall_JustBelowTheFakeContrastThreshold_StillShades()
    {
        // 252 - 16 = 236, then the curve leaves 236 unchanged (>= 192).
        var result = SectorBrightness.CalculateForWall(252, new Vector2(64, 0));

        Assert.Equal(236, result);
    }

    [Fact]
    public void CalculateForWall_ShadeAdjustmentClampsBeforeTheCurve()
    {
        // 250 + 16 = 266, clamped to 255 before the curve runs (255 is
        // unchanged by the curve).
        var result = SectorBrightness.CalculateForWall(250, new Vector2(0, 64));

        Assert.Equal(255, result);
    }
}
