using DoomArchitect.Core.ZDoom;

namespace DoomArchitect.Core.Tests.ZDoom;

public class GzdbColorTests
{
    [Theory]
    [InlineData("#FF0000", 0xFF, 0x00, 0x00)]
    [InlineData("#00ff00", 0x00, 0xFF, 0x00)]
    [InlineData("#F00", 0xFF, 0x00, 0x00)] // 3-digit shorthand expands each digit
    [InlineData("ff0000", 0xFF, 0x00, 0x00)] // bare hex, no leading #
    public void TryParse_ParsesHexColors(string input, byte r, byte g, byte b)
    {
        var ok = GzdbColor.TryParse(input, out var color);

        Assert.True(ok);
        Assert.Equal((r, g, b), color);
    }

    [Fact]
    public void TryParse_InvalidHtmlLength_FallsBackToBlack()
    {
        var ok = GzdbColor.TryParse("#12345", out var color);

        Assert.True(ok);
        Assert.Equal((byte)0, color.R);
        Assert.Equal((byte)0, color.G);
        Assert.Equal((byte)0, color.B);
    }

    [Fact]
    public void TryParse_NamedColor_ResolvesOnlyWhenATableIsSupplied()
    {
        Assert.False(GzdbColor.TryParse("red", out _));

        var table = new Dictionary<string, (byte, byte, byte)>(StringComparer.OrdinalIgnoreCase) { ["red"] = (255, 0, 0) };
        var ok = GzdbColor.TryParse("red", out var color, table);

        Assert.True(ok);
        Assert.Equal((255, 0, 0), color);
    }

    [Fact]
    public void TryParse_QuotedAndSpacedInput_IsNormalizedFirst()
    {
        var ok = GzdbColor.TryParse("\"F F 0 0 0 0\"", out var color);

        Assert.True(ok);
        Assert.Equal((byte)0xFF, color.R);
    }
}
