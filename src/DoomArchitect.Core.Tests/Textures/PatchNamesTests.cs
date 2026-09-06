using DoomArchitect.Core.Textures;

namespace DoomArchitect.Core.Tests.Textures;

public class PatchNamesTests
{
    [Fact]
    public void Read_DecodesNamesInOrder()
    {
        var data = TextureLumpTestBuilder.PatchNames("WALL01", "DOOR3", "SUPPORT2");

        var names = PatchNames.Read(data);

        Assert.Equal(new[] { "WALL01", "DOOR3", "SUPPORT2" }, names);
    }
}
