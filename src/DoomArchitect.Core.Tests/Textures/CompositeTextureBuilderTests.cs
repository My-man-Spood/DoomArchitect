using DoomArchitect.Core.Textures;

namespace DoomArchitect.Core.Tests.Textures;

public class CompositeTextureBuilderTests
{
    [Fact]
    public void Build_OverlappingOpaquePatches_LaterPatchDrawsOverEarlier()
    {
        var earlier = new PatchPlacement(0, 0, "EARLIER");
        var later = new PatchPlacement(0, 0, "LATER");
        var definition = new CompositeTextureDefinition("TEX", 4, 4, new[] { earlier, later });

        var images = new Dictionary<string, PixelImage>
        {
            ["EARLIER"] = SolidImage(4, 4, (255, 0, 0)),
            ["LATER"] = SolidImage(4, 4, (0, 255, 0)),
        };
        var warnings = new List<string>();

        var result = CompositeTextureBuilder.Build(definition, name => images.GetValueOrDefault(name), warnings);

        Assert.NotNull(result);
        AssertPixel(result!, 0, 0, (0, 255, 0));
    }

    [Fact]
    public void Build_SomePatchesMissing_StillBuildsFromWhatResolved()
    {
        var present = new PatchPlacement(0, 0, "PRESENT");
        var missing = new PatchPlacement(0, 0, "MISSING");
        var definition = new CompositeTextureDefinition("TEX", 4, 4, new[] { present, missing });
        var warnings = new List<string>();

        var result = CompositeTextureBuilder.Build(
            definition, name => name == "PRESENT" ? SolidImage(4, 4, (10, 20, 30)) : null, warnings);

        Assert.NotNull(result);
        AssertPixel(result!, 0, 0, (10, 20, 30));
        Assert.Contains(warnings, w => w.Contains("MISSING"));
    }

    [Fact]
    public void Build_EveryPatchMissing_FailsEntirely()
    {
        var definition = new CompositeTextureDefinition("TEX", 4, 4, new[] { new PatchPlacement(0, 0, "GONE") });
        var warnings = new List<string>();

        var result = CompositeTextureBuilder.Build(definition, _ => null, warnings);

        Assert.Null(result);
        Assert.Contains(warnings, w => w.Contains("every patch failed"));
    }

    [Fact]
    public void Build_MaskedSinglePatchColumn_ZeroesTheNegativeOffset()
    {
        // A single masked (has a transparent pixel) patch, alone in its
        // column, placed with a negative Y offset. Per the ported vanilla
        // bug, the offset is zeroed rather than honored.
        var patch = new PatchPlacement(0, -5, "PATCH");
        var definition = new CompositeTextureDefinition("TEX", 4, 10, new[] { patch });
        var image = MaskedImage(4, 4, (200, 100, 50));
        var warnings = new List<string>();

        var result = CompositeTextureBuilder.Build(definition, _ => image, warnings);

        Assert.NotNull(result);
        // Drawn at row 0 (offset zeroed), not at the (out of bounds) row -5.
        AssertPixel(result!, 0, 0, (200, 100, 50));
    }

    [Fact]
    public void Build_UnmaskedMultiPatchColumn_TruncatesDrawHeightInsteadOfZeroingOffset()
    {
        // OTHER (no offset) is drawn first and fully covers columns 2-5,
        // rows 0-3. NEG (offset -2) is drawn last and overlaps OTHER at
        // columns 2-3, where columnNumPatches is 2 - the truncation
        // condition. If the offset were simply zeroed (as in the masked/
        // single-patch cases) rather than the draw height being truncated,
        // NEG would repaint all 4 rows at columns 2-3 and no trace of
        // OTHER would remain there. Truncating drawheight to
        // height+offset=2 means only rows 0-1 get overwritten by NEG;
        // rows 2-3 must still show OTHER's color.
        var other = new PatchPlacement(2, 0, "OTHER");
        var negativeOffset = new PatchPlacement(0, -2, "NEG");
        var definition = new CompositeTextureDefinition("TEX", 6, 10, new[] { other, negativeOffset });

        var negImage = SolidImage(4, 4, (10, 10, 10));
        var otherImage = SolidImage(4, 4, (20, 20, 20));
        var warnings = new List<string>();

        var result = CompositeTextureBuilder.Build(
            definition, name => name == "NEG" ? negImage : otherImage, warnings);

        Assert.NotNull(result);
        AssertPixel(result!, 2, 0, (10, 10, 10)); // within NEG's truncated height - overwritten
        AssertPixel(result!, 2, 1, (10, 10, 10)); // within NEG's truncated height - overwritten
        AssertPixel(result!, 2, 2, (20, 20, 20)); // beyond the truncated height - OTHER still shows
        AssertPixel(result!, 2, 3, (20, 20, 20)); // beyond the truncated height - OTHER still shows
    }

    [Fact]
    public void Build_UnmaskedSinglePatchColumn_AppliesThePlainNegativeOffsetBug()
    {
        // A single unmasked patch, alone in its column, with a negative
        // offset: no truncation (only 1 patch in the column), but realY
        // still gets zeroed via the plain "y < 0" fallback branch - the
        // whole patch draws starting at row 0.
        var patch = new PatchPlacement(0, -3, "SOLO");
        var definition = new CompositeTextureDefinition("TEX", 4, 10, new[] { patch });
        var image = SolidImage(4, 4, (77, 88, 99));
        var warnings = new List<string>();

        var result = CompositeTextureBuilder.Build(definition, _ => image, warnings);

        Assert.NotNull(result);
        // All 4 source rows draw, starting at row 0 (not row -3, not truncated).
        AssertPixel(result!, 0, 0, (77, 88, 99));
        AssertPixel(result!, 0, 3, (77, 88, 99));
    }

    [Fact]
    public void Build_PatchWithPartialAlphaPixel_TreatsItAsFullyOpaque()
    {
        // UDB's actual alpha test is `pixel.a > 0.5f` against a raw byte
        // (0-255) field - since C# widens byte to float, any nonzero byte
        // already exceeds 0.5, so UDB treats partial alpha (e.g. 50, from
        // a real PNG's alpha channel) as fully opaque, not half-blended.
        // Only alpha==0 is transparent. Regression test for that exact
        // quirk (caught in review - a naive ">127" threshold would
        // instead skip this pixel).
        var patch = new PatchPlacement(0, 0, "PARTIAL");
        var definition = new CompositeTextureDefinition("TEX", 2, 2, new[] { patch });
        var rgba = new byte[2 * 2 * 4];
        rgba[0] = 111;
        rgba[1] = 22;
        rgba[2] = 33;
        rgba[3] = 50; // partial alpha - must still be drawn, not skipped
        var image = new PixelImage(2, 2, rgba);
        var warnings = new List<string>();

        var result = CompositeTextureBuilder.Build(definition, _ => image, warnings);

        Assert.NotNull(result);
        AssertPixel(result!, 0, 0, (111, 22, 33));
    }

    private static PixelImage SolidImage(int width, int height, (byte R, byte G, byte B) color)
    {
        var rgba = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            rgba[i * 4] = color.R;
            rgba[i * 4 + 1] = color.G;
            rgba[i * 4 + 2] = color.B;
            rgba[i * 4 + 3] = 255;
        }

        return new PixelImage(width, height, rgba);
    }

    private static PixelImage MaskedImage(int width, int height, (byte R, byte G, byte B) color)
    {
        var image = SolidImage(width, height, color);
        // Make the very last pixel transparent - the whole bitmap counts
        // as "masked" once any pixel is transparent, per BitmapIsMasked.
        image.Rgba[^1] = 0;
        return image;
    }

    private static void AssertPixel(PixelImage image, int x, int y, (byte R, byte G, byte B) expected)
    {
        var index = (y * image.Width + x) * 4;
        Assert.Equal(expected.R, image.Rgba[index]);
        Assert.Equal(expected.G, image.Rgba[index + 1]);
        Assert.Equal(expected.B, image.Rgba[index + 2]);
    }
}
