using DoomArchitect.Core.IO;

namespace DoomArchitect.Core.Tests.IO;

public class DirectoryResourceTests
{
    [Fact]
    public void FindByPath_ExactNestedPath_MatchesRegardlessOfCase()
    {
        var dir = DirectoryTestBuilder.Build(("zscript/actors/actor.zs", "class Actor {}"u8.ToArray()));

        var bytes = dir.FindByPath("ZScript/Actors/Actor.zs");

        Assert.Equal("class Actor {}", System.Text.Encoding.ASCII.GetString(bytes!));
    }

    [Fact]
    public void FindByPath_BareTitle_FallsBackToARootEntryMatchByTitle()
    {
        var dir = DirectoryTestBuilder.Build(("zscript.txt", "#include \"actor.zs\""u8.ToArray()));

        var bytes = dir.FindByPath("zscript");

        Assert.NotNull(bytes);
    }

    [Fact]
    public void FindByPath_NotFound_ReturnsNull()
    {
        var dir = DirectoryTestBuilder.Build(("zscript.txt", new byte[] { 1 }));

        Assert.Null(dir.FindByPath("does/not/exist.zs"));
    }

    [Fact]
    public void FindLump_RootEntry_MatchesByTitleIgnoringExtension()
    {
        var dir = DirectoryTestBuilder.Build(("PLAYPAL", new byte[] { 1, 2, 3 }));

        var lump = dir.FindLump("PLAYPAL");

        Assert.Equal(new byte[] { 1, 2, 3 }, lump!.Data);
    }

    [Theory]
    [InlineData(ResourceNamespace.Patches, "patches")]
    [InlineData(ResourceNamespace.Textures, "textures")]
    [InlineData(ResourceNamespace.Flats, "flats")]
    [InlineData(ResourceNamespace.Sprites, "sprites")]
    [InlineData(ResourceNamespace.Graphics, "graphics")]
    public void FindLump_FallsBackToEachNamespaceFolder(ResourceNamespace ns, string folder)
    {
        var dir = DirectoryTestBuilder.Build(($"{folder}/MYENTRY.png", new byte[] { 9 }));

        var lump = dir.FindLump("MYENTRY");

        Assert.NotNull(lump);
        Assert.Equal(new byte[] { 9 }, lump!.Data);
        Assert.Contains(dir.FindNamespaceLumps(ns), l => l.Name == "MYENTRY");
    }

    [Fact]
    public void FindNamespaceLumps_OnlyDirectChildrenOfThatFolder_NestedSubfoldersExcluded()
    {
        var dir = DirectoryTestBuilder.Build(
            ("sprites/TROOA0.png", new byte[] { 1 }),
            ("sprites/subdir/ignored.png", new byte[] { 2 }));

        var sprites = dir.FindNamespaceLumps(ResourceNamespace.Sprites);

        Assert.Equal(new[] { "TROOA0" }, sprites.Select(l => l.Name));
    }

    [Fact]
    public void FindLump_MatchesCaseInsensitively()
    {
        var dir = DirectoryTestBuilder.Build(("Flats/MyFlat.PNG", new byte[] { 5 }));

        var lump = dir.FindLump("myflat");

        Assert.Equal(new byte[] { 5 }, lump!.Data);
    }

    [Fact]
    public void FindLump_Missing_ReturnsNull()
    {
        var dir = DirectoryTestBuilder.Build(("flats/MYFLAT.png", Array.Empty<byte>()));

        Assert.Null(dir.FindLump("NOPE"));
    }

    [Fact]
    public void BuildTree_RealNestedFolderStructure()
    {
        var dir = DirectoryTestBuilder.Build(
            ("acs/lib/shared.acs", Array.Empty<byte>()),
            ("acs/main.acs", Array.Empty<byte>()));

        var tree = dir.BuildTree("myproject");

        Assert.Equal("myproject", tree.DisplayName);
        Assert.Equal(ResourceTreeNodeKind.DirectoryContainer, tree.Kind);
        var acsFolder = Assert.Single(tree.Children);
        Assert.Equal(ResourceTreeNodeKind.Folder, acsFolder.Kind);
        Assert.Equal(new[] { "lib", "main.acs" }, acsFolder.Children.Select(c => c.DisplayName));
        Assert.Equal(ResourceTreeNodeKind.Folder, acsFolder.Children[0].Kind);
        Assert.Equal(ResourceTreeNodeKind.File, acsFolder.Children[1].Kind);
    }
}
