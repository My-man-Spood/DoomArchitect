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

    /// <summary>
    /// A regression case for a real crash: a sprite-namespace lump whose
    /// header declares a width the rest of the lump's own data can't
    /// actually back (e.g. a truncated/corrupt lump, or one that isn't
    /// really patch-format data at all despite sitting in the sprite
    /// range - <see cref="Core.Textures.TextureSet.ResolveSpriteRotations"/>
    /// enumerates sprite-namespace lumps by name pattern alone, with no
    /// guarantee every match is actually well-formed). Reading the column
    /// offset table ran off the end of the byte array and threw a real
    /// <see cref="EndOfStreamException"/> straight out of <see cref="DoomPictureReader.TryRead"/>,
    /// crashing the whole app from several layers up
    /// (<c>SpriteIconCache.ProcessBudget</c>) - this method's own "Try"
    /// name is a real contract, not just nullable-annotation decoration,
    /// so malformed data must resolve to a null return like every other
    /// "couldn't decode this" case here, never an unhandled exception.
    /// </summary>
    [Fact]
    public void TryRead_HeaderClaimsMoreDataThanTheLumpActuallyHas_ReturnsNullInsteadOfThrowing()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((short)100); // width - claims 100 columns
        writer.Write((short)4); // height
        writer.Write((short)0); // offsetX
        writer.Write((short)0); // offsetY
        // No column offset table at all, let alone 100 entries of it -
        // the lump ends right after the header.

        var image = DoomPictureReader.TryRead(stream.ToArray(), Palette);

        Assert.Null(image);
    }

    /// <summary>
    /// Mirrors UDB's real <c>Validate()</c> gate, which normally runs
    /// *before* this reader is ever invoked at all (see this class's own
    /// remarks) - a lump that isn't really patch-format data, but reaches
    /// this reader as a fallback anyway, must be rejected outright rather
    /// than decoded into whatever bytes its bogus column offsets happen
    /// to land on (previously surfacing as a texture full of "random
    /// multicolor noise" instead of a clean miss).
    /// </summary>
    [Fact]
    public void TryRead_ColumnOffsetPointsBeforeTheHeaderAndOffsetTable_ReturnsNull()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((short)1); // width
        writer.Write((short)4); // height
        writer.Write((short)0); // offsetX
        writer.Write((short)0); // offsetY
        writer.Write(0); // column offset - points at byte 0, inside the header itself

        var image = DoomPictureReader.TryRead(stream.ToArray(), Palette);

        Assert.Null(image);
    }

    [Fact]
    public void TryRead_ColumnOffsetPointsPastTheEndOfTheLump_ReturnsNull()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((short)1); // width
        writer.Write((short)4); // height
        writer.Write((short)0); // offsetX
        writer.Write((short)0); // offsetY
        writer.Write(9000); // column offset - nowhere near this lump's own data

        var image = DoomPictureReader.TryRead(stream.ToArray(), Palette);

        Assert.Null(image);
    }

    [Fact]
    public void TryRead_NonZeroOffsets_ArePopulatedOnThePixelImage()
    {
        var data = BuildMinimalPatchWithOffsets(offsetX: 11, offsetY: -22);

        var image = DoomPictureReader.TryRead(data, Palette);

        Assert.NotNull(image);
        Assert.Equal(11, image!.OffsetX);
        Assert.Equal(-22, image.OffsetY);
    }

    /// <summary>A 1x1 patch with a single empty column, just to exercise the header's offset fields.</summary>
    private static byte[] BuildMinimalPatchWithOffsets(short offsetX, short offsetY)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((short)1); // width
        writer.Write((short)1); // height
        writer.Write(offsetX);
        writer.Write(offsetY);

        var offsetTablePosition = stream.Position;
        writer.Write(0); // placeholder column offset

        var columnOffset = (int)stream.Position;
        writer.Write((byte)255); // empty column - immediate terminator

        writer.Flush();
        stream.Position = offsetTablePosition;
        writer.Write(columnOffset);

        writer.Flush();
        return stream.ToArray();
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
