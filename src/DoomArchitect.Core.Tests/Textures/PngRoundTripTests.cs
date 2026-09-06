using DoomArchitect.Core.Textures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace DoomArchitect.Core.Tests.Textures;

/// <summary>
/// Exercises the real <see cref="ImageSharpModernImageDecoder"/> (not a
/// fake <see cref="IModernImageDecoder"/> test double) end-to-end, so the
/// actual dependency wiring behind the swappable decoder seam is verified
/// too, not just the interface contract.
/// </summary>
public class PngRoundTripTests
{
    [Fact]
    public void TryDecode_RealPngBytes_RoundTripsPixelValues()
    {
        using var source = new Image<Rgba32>(2, 2);
        source[0, 0] = new Rgba32(10, 20, 30, 255);
        source[1, 0] = new Rgba32(40, 50, 60, 255);
        source[0, 1] = new Rgba32(70, 80, 90, 0);
        source[1, 1] = new Rgba32(100, 110, 120, 255);

        using var stream = new MemoryStream();
        source.Save(stream, new PngEncoder());
        var pngBytes = stream.ToArray();

        var decoder = new ImageSharpModernImageDecoder();
        var kind = ImageFormatSniffer.Detect(pngBytes);
        Assert.Equal(ImageFormatKind.Png, kind);

        var decoded = decoder.TryDecode(pngBytes, kind, out var image);

        Assert.True(decoded);
        Assert.NotNull(image);
        Assert.Equal(2, image!.Width);
        Assert.Equal(2, image.Height);
        AssertPixel(image, 0, 0, (10, 20, 30, 255));
        AssertPixel(image, 1, 1, (100, 110, 120, 255));
    }

    [Fact]
    public void TryDecode_NonImageBytes_ReturnsFalse()
    {
        var decoder = new ImageSharpModernImageDecoder();

        var decoded = decoder.TryDecode(new byte[] { 1, 2, 3, 4 }, ImageFormatKind.Png, out var image);

        Assert.False(decoded);
        Assert.Null(image);
    }

    private static void AssertPixel(PixelImage image, int x, int y, (byte R, byte G, byte B, byte A) expected)
    {
        var index = (y * image.Width + x) * 4;
        Assert.Equal(expected.R, image.Rgba[index]);
        Assert.Equal(expected.G, image.Rgba[index + 1]);
        Assert.Equal(expected.B, image.Rgba[index + 2]);
        Assert.Equal(expected.A, image.Rgba[index + 3]);
    }
}
