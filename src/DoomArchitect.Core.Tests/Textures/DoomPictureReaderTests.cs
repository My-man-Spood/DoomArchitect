using DoomArchitect.Core.Textures;

namespace DoomArchitect.Core.Tests.Textures;

public class DoomPictureReaderTests
{
    private static readonly Playpal Palette = Playpal.Read(TextureLumpTestBuilder.Playpal((10, 20, 30), (40, 50, 60)));

    [Fact]
    public void TryRead_SinglePostPerColumn_DecodesOpaquePixelsAndLeavesGapsTransparent()
    {
        // 2 wide, 4 tall: column 0 has one post of 2 pixels starting at row 1;
        // column 1 is empty (no posts at all).
        var data = TextureLumpTestBuilder.Patch(
            height: 4,
            new (byte, byte[])[] { (1, new byte[] { 0, 1 }) },
            Array.Empty<(byte, byte[])>());

        var image = DoomPictureReader.TryRead(data, Palette);

        Assert.NotNull(image);
        Assert.Equal(2, image!.Width);
        Assert.Equal(4, image.Height);

        // Row 0, column 0: above the post - transparent.
        Assert.Equal(0, image.Rgba[Pixel(0, 0, image.Width) * 4 + 3]);
        // Row 1, column 0: first pixel of the post - palette index 0.
        AssertPixel(image, 0, 1, (10, 20, 30));
        // Row 2, column 0: second pixel of the post - palette index 1.
        AssertPixel(image, 0, 2, (40, 50, 60));
        // Column 1 has no posts at all - fully transparent.
        Assert.Equal(0, image.Rgba[Pixel(1, 0, image.Width) * 4 + 3]);
    }

    [Fact]
    public void TryRead_TallPatchOverHeight256_AccumulatesEqualTopDelta()
    {
        // Post 1 sets the running y to 50. Post 2's topdelta (50) is EQUAL
        // to that running y - for a patch taller than 256, that equality
        // means "accumulate": new y = 50 + 50 = 100, not 50. Row 100 must
        // be opaque and distinct from row 50 (which post 1 already drew).
        var data = TextureLumpTestBuilder.Patch(
            height: 300,
            new (byte, byte[])[] { (50, new byte[] { 5 }), (50, new byte[] { 5 }) });

        var image = DoomPictureReader.TryRead(data, Palette);

        Assert.NotNull(image);
        Assert.True(image!.Rgba[Pixel(0, 50, image.Width) * 4 + 3] > 0); // post 1, at its own topdelta
        Assert.True(image.Rgba[Pixel(0, 100, image.Width) * 4 + 3] > 0); // post 2, accumulated to 50+50
    }

    [Fact]
    public void TryRead_ShortPatchAtHeight256OrBelow_TreatsEqualTopDeltaAsAbsolute()
    {
        // Same setup as above, but at height <= 256: an equal topdelta is
        // NOT accumulated - post 2 is instead treated as the absolute row
        // 50, overwriting post 1 rather than stacking at row 100.
        var data = TextureLumpTestBuilder.Patch(
            height: 200,
            new (byte, byte[])[] { (50, new byte[] { 0 }), (50, new byte[] { 1 }) });

        var image = DoomPictureReader.TryRead(data, Palette);

        Assert.NotNull(image);
        AssertPixel(image!, 0, 50, (40, 50, 60)); // post 2 overwrote post 1 at the same absolute row
    }

    [Fact]
    public void TryRead_ColumnOverflow_AbortsTheWholePatch()
    {
        // topdelta 250 + 10 pixels overruns a 4-tall patch - the whole
        // patch must fail, not just that column.
        var data = TextureLumpTestBuilder.Patch(height: 4, new (byte, byte[])[] { (250, new byte[10]) });

        var image = DoomPictureReader.TryRead(data, Palette);

        Assert.Null(image);
    }

    private static void AssertPixel(PixelImage image, int x, int y, (byte R, byte G, byte B) expected)
    {
        var index = (y * image.Width + x) * 4;
        Assert.Equal(expected.R, image.Rgba[index]);
        Assert.Equal(expected.G, image.Rgba[index + 1]);
        Assert.Equal(expected.B, image.Rgba[index + 2]);
        Assert.True(image.Rgba[index + 3] > 0);
    }

    private static int Pixel(int x, int y, int width) => y * width + x;
}
