using DoomArchitect.Core.IO;

namespace DoomArchitect.Core.Tests.IO;

public class ResourceContainerFactoryTests
{
    [Fact]
    public void Open_ExistingDirectory_OpensADirectoryResource()
    {
        var root = Directory.CreateTempSubdirectory("da_factory_test_").FullName;
        File.WriteAllBytes(Path.Combine(root, "zscript.txt"), "#include \"actor.zs\""u8.ToArray());

        var container = ResourceContainerFactory.Open(root);

        Assert.IsType<DirectoryResource>(container);
        Assert.NotNull(container.FindLump("zscript"));
    }
}
