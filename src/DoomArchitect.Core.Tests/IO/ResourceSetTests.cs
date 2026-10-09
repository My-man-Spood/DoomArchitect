using DoomArchitect.Core.IO;

namespace DoomArchitect.Core.Tests.IO;

public class ResourceSetTests
{
    private static WadFile BuildWad(params (string Name, byte[] Data)[] lumps) =>
        WadFile.Read(new MemoryStream(WadTestBuilder.Build(lumps)));

    [Fact]
    public void FindLump_NameInBothWads_HigherPriorityWadWins()
    {
        var lower = BuildWad(("MYLUMP", new byte[] { 1 }));
        var higher = BuildWad(("MYLUMP", new byte[] { 2 }));
        var resources = new ResourceSet(new IResourceContainer[] { lower, higher });

        var lump = resources.FindLump("MYLUMP");

        Assert.Equal(new byte[] { 2 }, lump!.Data);
    }

    [Fact]
    public void FindLump_OnlyInLowerPriorityWad_FallsBackToIt()
    {
        var lower = BuildWad(("ONLYHERE", new byte[] { 9 }));
        var higher = BuildWad(("SOMETHINGELSE", Array.Empty<byte>()));
        var resources = new ResourceSet(new IResourceContainer[] { lower, higher });

        var lump = resources.FindLump("ONLYHERE");

        Assert.Equal(new byte[] { 9 }, lump!.Data);
    }

    [Fact]
    public void FindLump_InNeitherWad_ReturnsNull()
    {
        var resources = new ResourceSet(new IResourceContainer[] { BuildWad(), BuildWad() });

        Assert.Null(resources.FindLump("NOPE"));
    }

    [Fact]
    public void FindNamespaceLumps_UnionsRangesHighestPriorityFirst()
    {
        var lower = BuildWad(("S_START", Array.Empty<byte>()), ("LOWA0", Array.Empty<byte>()), ("S_END", Array.Empty<byte>()));
        var higher = BuildWad(("S_START", Array.Empty<byte>()), ("HIGHA0", Array.Empty<byte>()), ("S_END", Array.Empty<byte>()));
        var resources = new ResourceSet(new IResourceContainer[] { lower, higher });

        var sprites = resources.FindNamespaceLumps(ResourceNamespace.Sprites);

        Assert.Equal(new[] { "HIGHA0", "LOWA0" }, sprites.Select(l => l.Name));
    }

    [Fact]
    public void FindLump_NameOnlyInPk3_ResolvesAcrossMixedContainerTypes()
    {
        var wad = BuildWad(("SOMETHINGELSE", Array.Empty<byte>()));
        var pk3 = Pk3TestBuilder.Build(("flats/MYFLAT.png", new byte[] { 1, 2, 3 }));
        var resources = new ResourceSet(new IResourceContainer[] { wad, pk3 });

        var lump = resources.FindLump("MYFLAT");

        Assert.Equal(new byte[] { 1, 2, 3 }, lump!.Data);
    }

    [Fact]
    public void FindLump_SameNameInBoth_HigherPriorityPk3WinsOverLowerPriorityWad()
    {
        var wad = BuildWad(("MYFLAT", new byte[] { 9 }));
        var pk3 = Pk3TestBuilder.Build(("flats/MYFLAT.png", new byte[] { 1, 2, 3 }));
        var resources = new ResourceSet(new IResourceContainer[] { wad, pk3 });

        var lump = resources.FindLump("MYFLAT");

        Assert.Equal(new byte[] { 1, 2, 3 }, lump!.Data);
    }

    [Fact]
    public void Containers_ReturnsEveryContainerHighestPriorityFirst()
    {
        var lower = BuildWad();
        var higher = BuildWad();
        var resources = new ResourceSet(new IResourceContainer[] { lower, higher });

        Assert.Equal(new IResourceContainer[] { higher, lower }, resources.Containers);
    }

    [Fact]
    public void FindLumpSource_ReturnsTheHighestPriorityContainerDefiningTheLump()
    {
        var lower = BuildWad(("MYLUMP", Array.Empty<byte>()));
        var higher = BuildWad(("MYLUMP", Array.Empty<byte>()));
        var resources = new ResourceSet(new IResourceContainer[] { lower, higher });

        Assert.Same(higher, resources.FindLumpSource("MYLUMP"));
    }

    [Fact]
    public void FindLumpSource_OnlyInLowerPriorityContainer_StillFindsIt()
    {
        var lower = BuildWad(("ONLYHERE", Array.Empty<byte>()));
        var higher = BuildWad(("SOMETHINGELSE", Array.Empty<byte>()));
        var resources = new ResourceSet(new IResourceContainer[] { lower, higher });

        Assert.Same(lower, resources.FindLumpSource("ONLYHERE"));
    }

    [Fact]
    public void FindLumpSource_InNoContainer_ReturnsNull()
    {
        var resources = new ResourceSet(new IResourceContainer[] { BuildWad(), BuildWad() });

        Assert.Null(resources.FindLumpSource("NOPE"));
    }

    [Fact]
    public void FindIncludeText_RootLevelPk3Entry_DecodesItAsUtf8Text()
    {
        var pk3 = Pk3TestBuilder.Build(("zcommon.acs", System.Text.Encoding.UTF8.GetBytes("#define FOO 1")));
        var resources = new ResourceSet(new IResourceContainer[] { pk3 });

        Assert.Equal("#define FOO 1", resources.FindIncludeText("zcommon.acs"));
    }

    [Fact]
    public void FindIncludeText_WadLump_DecodesItAsUtf8Text()
    {
        var wad = BuildWad(("ZCOMMON", System.Text.Encoding.UTF8.GetBytes("#define FOO 1")));
        var resources = new ResourceSet(new IResourceContainer[] { wad });

        Assert.Equal("#define FOO 1", resources.FindIncludeText("zcommon.acs"));
    }

    [Fact]
    public void FindIncludeText_PathWithASubfolder_StillMatchesByBareTitle()
    {
        var pk3 = Pk3TestBuilder.Build(("zcommon.acs", System.Text.Encoding.UTF8.GetBytes("#define FOO 1")));
        var resources = new ResourceSet(new IResourceContainer[] { pk3 });

        Assert.Equal("#define FOO 1", resources.FindIncludeText("acs/zcommon.acs"));
    }

    [Fact]
    public void FindIncludeText_NotFoundAnywhere_ReturnsNull()
    {
        var resources = new ResourceSet(new IResourceContainer[] { BuildWad(), BuildWad() });

        Assert.Null(resources.FindIncludeText("zcommon.acs"));
    }

    /// <summary>
    /// Real, reported bug: a real on-disk folder resource (the
    /// GZDoom/ZDoom convention this whole project already targets)
    /// commonly organizes its own ACS sources under an `acs/` subfolder -
    /// `#include "acs/souls.acs"` failed to resolve even though the file
    /// genuinely existed, because the original implementation only ever
    /// tried a bare-title match (<see cref="FindLump"/>'s own root-only +
    /// fixed-texture-namespace search), never the real relative path a
    /// <see cref="DirectoryResource"/> already indexes recursively.
    /// </summary>
    [Fact]
    public void FindIncludeText_RealFileInASubfolder_ResolvesByItsExactRelativePath()
    {
        var folder = DirectoryTestBuilder.Build(("acs/souls.acs", System.Text.Encoding.UTF8.GetBytes("#define SOULS 1")));
        var resources = new ResourceSet(new IResourceContainer[] { folder });

        Assert.Equal("#define SOULS 1", resources.FindIncludeText("acs/souls.acs"));
    }
}
