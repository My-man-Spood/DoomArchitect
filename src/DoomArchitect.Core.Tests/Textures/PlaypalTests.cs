using DoomArchitect.Core.Textures;

namespace DoomArchitect.Core.Tests.Textures;

public class PlaypalTests
{
    [Fact]
    public void Read_DecodesFirst256RgbTriples()
    {
        var data = TextureLumpTestBuilder.Playpal((10, 20, 30), (255, 0, 128));

        var palette = Playpal.Read(data);

        Assert.Equal(((byte)10, (byte)20, (byte)30), palette[0]);
        Assert.Equal(((byte)255, (byte)0, (byte)128), palette[1]);
        Assert.Equal(((byte)0, (byte)0, (byte)0), palette[2]);
    }

    [Fact]
    public void CreateFallback_IsFlatGray()
    {
        var palette = Playpal.CreateFallback();

        Assert.Equal(((byte)127, (byte)127, (byte)127), palette[0]);
        Assert.Equal(((byte)127, (byte)127, (byte)127), palette[255]);
    }
}
