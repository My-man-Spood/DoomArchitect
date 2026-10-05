using DoomArchitect.Core.IO;

namespace DoomArchitect.Core.Tests.IO;

public class PathTreeBuilderTests
{
    [Fact]
    public void Build_TwoFilesSharingAFolder_FoldIntoOneFolderNode()
    {
        var tree = PathTreeBuilder.Build("root", ResourceTreeNodeKind.Pk3Container,
            new[] { "acs/a.acs", "acs/b.acs" });

        var acsFolder = Assert.Single(tree.Children);
        Assert.Equal(ResourceTreeNodeKind.Folder, acsFolder.Kind);
        Assert.Equal(new[] { "a.acs", "b.acs" }, acsFolder.Children.Select(c => c.DisplayName));
    }

    [Fact]
    public void Build_DeeplyNestedPath_CreatesEveryIntermediateFolder()
    {
        var tree = PathTreeBuilder.Build("root", ResourceTreeNodeKind.Pk3Container,
            new[] { "a/b/c/d.txt" });

        var a = Assert.Single(tree.Children);
        var b = Assert.Single(a.Children);
        var c = Assert.Single(b.Children);
        var d = Assert.Single(c.Children);
        Assert.Equal("d.txt", d.DisplayName);
        Assert.Equal(ResourceTreeNodeKind.File, d.Kind);
        Assert.Equal("a/b/c/d.txt", d.Path);
    }

    [Fact]
    public void Build_FileAtRootAlongsideAFolder_BothBecomeDirectChildren()
    {
        var tree = PathTreeBuilder.Build("root", ResourceTreeNodeKind.Pk3Container,
            new[] { "zscript.zs", "acs/main.acs" });

        Assert.Equal(new[] { "acs", "zscript.zs" }, tree.Children.Select(c => c.DisplayName)); // folders sort before files
    }

    [Fact]
    public void Build_EmptyInput_RootHasNoChildren()
    {
        var tree = PathTreeBuilder.Build("root", ResourceTreeNodeKind.DirectoryContainer, Array.Empty<string>());

        Assert.Empty(tree.Children);
    }
}
