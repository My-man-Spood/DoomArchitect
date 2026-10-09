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

    private static byte[] BuildMinimalMapWad() => WadWriter.Write(new[]
    {
        new WadLump("MAP01", Array.Empty<byte>()),
        new WadLump("THINGS", Array.Empty<byte>()),
        new WadLump("BEHAVIOR", Array.Empty<byte>()),
    });

    [Fact]
    public void ExpandNestedWads_WadDirectlyInsideMapsFolder_ExpandsIntoItsOwnLumpTree()
    {
        var tree = PathTreeBuilder.Build("mod", ResourceTreeNodeKind.DirectoryContainer, new[] { "maps/MAP01.wad" });
        var bytes = BuildMinimalMapWad();

        PathTreeBuilder.ExpandNestedWads(tree, _ => bytes);

        var mapsFolder = Assert.Single(tree.Children);
        var expanded = Assert.Single(mapsFolder.Children);
        Assert.Equal(ResourceTreeNodeKind.WadContainer, expanded.Kind);
        Assert.Equal("maps/MAP01.wad", expanded.Path);
        var mapGroup = Assert.Single(expanded.Children);
        Assert.Equal(ResourceTreeNodeKind.MapGroup, mapGroup.Kind);
        Assert.Equal("MAP01", mapGroup.DisplayName);
    }

    [Fact]
    public void ExpandNestedWads_CorruptWad_FallsBackToAPlainFileLeaf()
    {
        var tree = PathTreeBuilder.Build("mod", ResourceTreeNodeKind.DirectoryContainer, new[] { "maps/MAP01.wad" });

        PathTreeBuilder.ExpandNestedWads(tree, _ => new byte[] { 1, 2, 3 });

        var mapsFolder = Assert.Single(tree.Children);
        var leaf = Assert.Single(mapsFolder.Children);
        Assert.Equal(ResourceTreeNodeKind.File, leaf.Kind);
        Assert.Equal("maps/MAP01.wad", leaf.Path);
    }

    [Fact]
    public void ExpandNestedWads_WadOutsideMapsFolder_IsNotExpanded()
    {
        var tree = PathTreeBuilder.Build("mod", ResourceTreeNodeKind.DirectoryContainer, new[] { "backup/MAP01.wad" });
        var bytes = BuildMinimalMapWad();

        PathTreeBuilder.ExpandNestedWads(tree, _ => bytes);

        var backupFolder = Assert.Single(tree.Children);
        var leaf = Assert.Single(backupFolder.Children);
        Assert.Equal(ResourceTreeNodeKind.File, leaf.Kind);
    }

    [Fact]
    public void ExpandNestedWads_ReadFileReturnsNull_FallsBackToAPlainFileLeaf()
    {
        var tree = PathTreeBuilder.Build("mod", ResourceTreeNodeKind.DirectoryContainer, new[] { "maps/MAP01.wad" });

        PathTreeBuilder.ExpandNestedWads(tree, _ => null);

        var mapsFolder = Assert.Single(tree.Children);
        var leaf = Assert.Single(mapsFolder.Children);
        Assert.Equal(ResourceTreeNodeKind.File, leaf.Kind);
    }
}
