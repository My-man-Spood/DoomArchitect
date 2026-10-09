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

    [Fact]
    public void ContainsFile_RealEntry_ReturnsTrue()
    {
        var (dir, root) = DirectoryTestBuilder.BuildWithRoot(("maps/MAP01.wad", Array.Empty<byte>()));

        Assert.True(dir.ContainsFile(Path.Combine(root, "maps", "MAP01.wad")));
    }

    [Fact]
    public void ContainsFile_DifferentCasingAndSeparators_StillMatches()
    {
        var (dir, root) = DirectoryTestBuilder.BuildWithRoot(("maps/MAP01.wad", Array.Empty<byte>()));

        // Path.GetFullPath normalizes "./" and ".." segments, so this still
        // resolves to the exact same file even though it's spelled
        // differently than how DirectoryResource itself first saw it.
        var differentlySpelledPath = Path.Combine(root, "maps", "..", "maps", "MAP01.WAD");

        Assert.True(dir.ContainsFile(differentlySpelledPath));
    }

    [Fact]
    public void ContainsFile_UnrelatedPath_ReturnsFalse()
    {
        var (dir, root) = DirectoryTestBuilder.BuildWithRoot(("maps/MAP01.wad", Array.Empty<byte>()));

        Assert.False(dir.ContainsFile(Path.Combine(root, "maps", "MAP02.wad")));
    }

    [Fact]
    public void ResolveAbsolutePath_RealEntry_ReturnsItsOwnOnDiskPath()
    {
        var (dir, root) = DirectoryTestBuilder.BuildWithRoot(("maps/MAP01.wad", Array.Empty<byte>()));

        var resolved = dir.ResolveAbsolutePath("maps/MAP01.wad");

        Assert.Equal(Path.Combine(root, "maps", "MAP01.wad"), resolved);
    }

    [Fact]
    public void ResolveAbsolutePath_UnknownRelativePath_ReturnsNull()
    {
        var dir = DirectoryTestBuilder.Build(("maps/MAP01.wad", Array.Empty<byte>()));

        Assert.Null(dir.ResolveAbsolutePath("maps/MAP02.wad"));
    }

    /// <summary>
    /// The actual wiring, not just PathTreeBuilder.ExpandNestedWads in
    /// isolation - a real GZDoom/ZDoom-convention maps/MAP01.wad on disk
    /// should come back from BuildTree as a real lump structure, not a
    /// flat file leaf.
    /// </summary>
    [Fact]
    public void BuildTree_PerMapWadInsideMapsFolder_ExpandsIntoItsOwnLumpStructure()
    {
        var mapWad = WadWriter.Write(new[]
        {
            new WadLump("MAP01", Array.Empty<byte>()),
            new WadLump("THINGS", Array.Empty<byte>()),
            new WadLump("BEHAVIOR", Array.Empty<byte>()),
        });
        var dir = DirectoryTestBuilder.Build(("maps/MAP01.wad", mapWad));

        var tree = dir.BuildTree("mod");

        var mapsFolder = Assert.Single(tree.Children);
        var expandedWad = Assert.Single(mapsFolder.Children);
        Assert.Equal(ResourceTreeNodeKind.WadContainer, expandedWad.Kind);
        var mapGroup = Assert.Single(expandedWad.Children);
        Assert.Equal(ResourceTreeNodeKind.MapGroup, mapGroup.Kind);
        Assert.Equal("MAP01", mapGroup.DisplayName);
    }
}
