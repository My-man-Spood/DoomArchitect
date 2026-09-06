using DoomArchitect.Core.Textures;

namespace DoomArchitect.Core.Tests.Textures;

public class ImageFormatSnifferTests
{
    [Fact]
    public void Detect_PngSignature_ReturnsPng()
    {
        var data = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0 };

        Assert.Equal(ImageFormatKind.Png, ImageFormatSniffer.Detect(data));
    }

    [Fact]
    public void Detect_JpegSignature_ReturnsJpeg()
    {
        var data = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0 };

        Assert.Equal(ImageFormatKind.Jpeg, ImageFormatSniffer.Detect(data));
    }

    [Fact]
    public void Detect_PcxSignature_ReturnsPcx()
    {
        var data = new byte[] { 0x0A, 0x05, 0x01, 0x08, 0, 0, 0, 0 };

        Assert.Equal(ImageFormatKind.Pcx, ImageFormatSniffer.Detect(data));
    }

    [Fact]
    public void Detect_TgaLikeHeader_ReturnsTga()
    {
        var data = new byte[18];
        // colorMapType=0, imageType=0 (both valid), width=64, height=64, bpp=24
        data[12] = 64;
        data[14] = 64;
        data[16] = 24;

        Assert.Equal(ImageFormatKind.Tga, ImageFormatSniffer.Detect(data));
    }

    [Fact]
    public void Detect_TgaShapedHeaderWithInvalidDimensions_ReturnsUnknown()
    {
        // Regression test: colorMapType/imageType alone matched a real Doom
        // patch_t header often enough to misclassify legitimate patches as
        // TGA (caught in review). UDB's real heuristic also range-checks
        // width/height and bits-per-pixel - this data passes the first two
        // checks but has a zeroed width, which those extra checks must catch.
        var data = new byte[18];
        data[1] = 1; // colorMapType - valid
        data[2] = 10; // imageType - valid
        // width (bytes 12-13) left at 0 - must be rejected

        Assert.Equal(ImageFormatKind.Unknown, ImageFormatSniffer.Detect(data));
    }

    [Fact]
    public void Detect_UnrecognizedData_ReturnsUnknown()
    {
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        Assert.Equal(ImageFormatKind.Unknown, ImageFormatSniffer.Detect(data));
    }
}
