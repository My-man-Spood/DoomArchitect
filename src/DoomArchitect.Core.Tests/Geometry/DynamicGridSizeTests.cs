using DoomArchitect.Core.Geometry;

namespace DoomArchitect.Core.Tests.Geometry;

public class DynamicGridSizeTests
{
    [Theory]
    [InlineData(128f, 4f)] // ceil(128/4)=32, already a power of two
    [InlineData(100f, 4f)] // ceil(100/4)=25, rounds up to 32
    [InlineData(1000f, 32f)] // ceil(1000/4)=250, rounds up to 256
    [InlineData(16f, 0.5f)] // ceil(16/4)=4, already a power of two
    [InlineData(4f, 0.125f)] // ceil(4/4)=1, already a power of two
    public void ForVisibleExtent_ReturnsExpectedPowerOfTwoGridSize(float visibleExtent, float expectedGridSize)
    {
        var result = DynamicGridSize.ForVisibleExtent(visibleExtent);

        Assert.Equal(expectedGridSize, result);
    }
}
