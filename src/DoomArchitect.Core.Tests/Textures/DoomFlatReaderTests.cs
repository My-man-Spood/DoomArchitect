using DoomArchitect.Core.Textures;

namespace DoomArchitect.Core.Tests.Textures;

public class DoomFlatReaderTests
{
    private static readonly Playpal Palette = Playpal.Read(TextureLumpTestBuilder.Playpal((1, 2, 3)));

    [Theory]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    public void TryRead_PerfectSquareLength_UsesThatSize(int size)
    {
        var data = TextureLumpTestBuilder.Flat(size, size, 0);

        var image = DoomFlatReader.TryRead(data, Palette);

        Assert.NotNull(image);
        Assert.Equal(size, image!.Width);
        Assert.Equal(size, image.Height);
    }

    [Fact]
    public void TryRead_NonSquareLengthOver4096_ForcesTo64x64AndTruncates()
    {
        // 5000 bytes: not a perfect square, but over 4096 - UDB's own
        // quirk forces 64x64 and reads only the first 4096 bytes.
        var data = new byte[5000];
        Array.Fill(data, (byte)0, 0, 4096);
        Array.Fill(data, (byte)255, 4096, 5000 - 4096); // trailing bytes must be ignored, not read

        var image = DoomFlatReader.TryRead(data, Palette);

        Assert.NotNull(image);
        Assert.Equal(64, image!.Width);
        Assert.Equal(64, image.Height);
    }

    [Fact]
    public void TryRead_NonSquareLengthUnder4096_Fails()
    {
        var data = new byte[90]; // not a perfect square (9*9=81, 10*10=100), not over 4096

        var image = DoomFlatReader.TryRead(data, Palette);

        Assert.Null(image);
    }
}
