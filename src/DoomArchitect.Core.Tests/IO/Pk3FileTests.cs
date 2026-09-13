using DoomArchitect.Core.IO;

namespace DoomArchitect.Core.Tests.IO;

public class Pk3FileTests
{
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
}
