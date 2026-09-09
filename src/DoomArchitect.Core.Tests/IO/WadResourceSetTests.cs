using DoomArchitect.Core.IO;

namespace DoomArchitect.Core.Tests.IO;

public class WadResourceSetTests
{
    private static WadFile BuildWad(params (string Name, byte[] Data)[] lumps) =>
        WadFile.Read(new MemoryStream(WadTestBuilder.Build(lumps)));

    [Fact]
    public void FindLump_NameInBothWads_HigherPriorityWadWins()
    {
        var lower = BuildWad(("MYLUMP", new byte[] { 1 }));
        var higher = BuildWad(("MYLUMP", new byte[] { 2 }));
        var resources = new WadResourceSet(new[] { lower, higher });

        var lump = resources.FindLump("MYLUMP");

        Assert.Equal(new byte[] { 2 }, lump!.Data);
    }

    [Fact]
    public void FindLump_OnlyInLowerPriorityWad_FallsBackToIt()
    {
        var lower = BuildWad(("ONLYHERE", new byte[] { 9 }));
        var higher = BuildWad(("SOMETHINGELSE", Array.Empty<byte>()));
        var resources = new WadResourceSet(new[] { lower, higher });

        var lump = resources.FindLump("ONLYHERE");

        Assert.Equal(new byte[] { 9 }, lump!.Data);
    }

    [Fact]
    public void FindLump_InNeitherWad_ReturnsNull()
    {
        var resources = new WadResourceSet(new[] { BuildWad(), BuildWad() });

        Assert.Null(resources.FindLump("NOPE"));
    }

    [Fact]
    public void FindLumpsBetweenMarkers_UnionsRangesHighestPriorityFirst()
    {
        var lower = BuildWad(("S_START", Array.Empty<byte>()), ("LOWA0", Array.Empty<byte>()), ("S_END", Array.Empty<byte>()));
        var higher = BuildWad(("S_START", Array.Empty<byte>()), ("HIGHA0", Array.Empty<byte>()), ("S_END", Array.Empty<byte>()));
        var resources = new WadResourceSet(new[] { lower, higher });

        var sprites = resources.FindLumpsBetweenMarkers("S_START", "S_END");

        Assert.Equal(new[] { "HIGHA0", "LOWA0" }, sprites.Select(l => l.Name));
    }
}
