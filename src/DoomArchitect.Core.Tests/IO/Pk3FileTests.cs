using DoomArchitect.Core.IO;

namespace DoomArchitect.Core.Tests.IO;

public class Pk3FileTests
{
    [Fact]
    public void FindByPath_ExactNestedPath_MatchesRegardlessOfCase()
    {
        var pk3 = Pk3TestBuilder.Build(("zscript/actors/actor.zs", "class Actor {}"u8.ToArray()));

        var bytes = pk3.FindByPath("ZScript/Actors/Actor.zs");

        Assert.Equal("class Actor {}", System.Text.Encoding.ASCII.GetString(bytes!));
    }

    [Fact]
    public void FindByPath_BareTitle_FallsBackToARootEntryMatchByTitle()
    {
        var pk3 = Pk3TestBuilder.Build(("zscript.txt", "#include \"actor.zs\""u8.ToArray()));

        var bytes = pk3.FindByPath("zscript");

        Assert.NotNull(bytes);
    }

    [Fact]
    public void FindByPath_NotFound_ReturnsNull()
    {
        var pk3 = Pk3TestBuilder.Build(("zscript.txt", new byte[] { 1 }));

        Assert.Null(pk3.FindByPath("does/not/exist.zs"));
    }

    [Fact]
    public void FindLump_RootEntry_MatchesByTitleIgnoringExtension()
    {
        var pk3 = Pk3TestBuilder.Build(("PLAYPAL", new byte[] { 1, 2, 3 }));

        var lump = pk3.FindLump("PLAYPAL");

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
        var pk3 = Pk3TestBuilder.Build(($"{folder}/MYENTRY.png", new byte[] { 9 }));

        var lump = pk3.FindLump("MYENTRY");

        Assert.NotNull(lump);
        Assert.Equal(new byte[] { 9 }, lump!.Data);
        Assert.Contains(pk3.FindNamespaceLumps(ns), l => l.Name == "MYENTRY");
    }

    [Fact]
    public void FindNamespaceLumps_OnlyDirectChildrenOfThatFolder_NestedSubfoldersExcluded()
    {
        var pk3 = Pk3TestBuilder.Build(
            ("sprites/TROOA0.png", new byte[] { 1 }),
            ("sprites/subdir/ignored.png", new byte[] { 2 }));

        var sprites = pk3.FindNamespaceLumps(ResourceNamespace.Sprites);

        Assert.Equal(new[] { "TROOA0" }, sprites.Select(l => l.Name));
    }

    [Fact]
    public void FindLump_MatchesCaseInsensitively()
    {
        var pk3 = Pk3TestBuilder.Build(("Flats/MyFlat.PNG", new byte[] { 5 }));

        var lump = pk3.FindLump("myflat");

        Assert.Equal(new byte[] { 5 }, lump!.Data);
    }

    [Fact]
    public void FindLump_Missing_ReturnsNull()
    {
        var pk3 = Pk3TestBuilder.Build(("flats/MYFLAT.png", Array.Empty<byte>()));

        Assert.Null(pk3.FindLump("NOPE"));
    }

    [Fact]
    public void FindNamespaceLumps_DuplicatePathDifferingOnlyByCase_FirstEntryWins()
    {
        var pk3 = Pk3TestBuilder.Build(
            ("flats/DUPE.png", new byte[] { 1 }),
            ("FLATS/DUPE.PNG", new byte[] { 2 }));

        var flats = pk3.FindNamespaceLumps(ResourceNamespace.Flats);

        Assert.Single(flats);
        Assert.Equal(new byte[] { 1 }, flats[0].Data);
    }

    [Fact]
    public void BuildTree_FoldersBeforeFilesAlphabetically()
    {
        var pk3 = Pk3TestBuilder.Build(
            ("zscript.zs", Array.Empty<byte>()),
            ("flats/MYFLAT.png", Array.Empty<byte>()),
            ("acs/main.acs", Array.Empty<byte>()));

        var tree = pk3.BuildTree("my.pk3");

        Assert.Equal("my.pk3", tree.DisplayName);
        Assert.Equal(ResourceTreeNodeKind.Pk3Container, tree.Kind);
        Assert.Equal(new[] { "acs", "flats", "zscript.zs" }, tree.Children.Select(c => c.DisplayName));
        Assert.Equal(ResourceTreeNodeKind.Folder, tree.Children[0].Kind);
        Assert.Equal(ResourceTreeNodeKind.Folder, tree.Children[1].Kind);
        Assert.Equal(ResourceTreeNodeKind.File, tree.Children[2].Kind);

        var acsFolder = tree.Children[0];
        var mainAcs = Assert.Single(acsFolder.Children);
        Assert.Equal("main.acs", mainAcs.DisplayName);
        Assert.Equal(ResourceTreeNodeKind.File, mainAcs.Kind);
        Assert.Equal("acs/main.acs", mainAcs.Path);
    }

    [Fact]
    public void WithReplacedEntry_ReplacesOnlyTheTargetPath_LeavesOthersUntouched()
    {
        var pk3 = Pk3TestBuilder.Build(
            ("SCRIPTS", "old script"u8.ToArray()),
            ("flats/MYFLAT.png", new byte[] { 1, 2, 3 }),
            ("zscript.zs", "class Actor {}"u8.ToArray()));

        var entries = pk3.WithReplacedEntry("SCRIPTS", "new script"u8.ToArray());

        Assert.Equal(3, entries.Count);
        Assert.Equal("new script"u8.ToArray(), entries.Single(e => e.Path == "SCRIPTS").Data);
        Assert.Equal(new byte[] { 1, 2, 3 }, entries.Single(e => e.Path == "flats/MYFLAT.png").Data);
        Assert.Equal("class Actor {}"u8.ToArray(), entries.Single(e => e.Path == "zscript.zs").Data);
    }

    [Fact]
    public void WithReplacedEntry_PathMatchIsCaseInsensitive()
    {
        var pk3 = Pk3TestBuilder.Build(("Scripts/Main.acs", "old"u8.ToArray()));

        var entries = pk3.WithReplacedEntry("scripts/main.acs", "new"u8.ToArray());

        Assert.Equal("new"u8.ToArray(), Assert.Single(entries).Data);
    }
}
