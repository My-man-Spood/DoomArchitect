using System.Text;
using DoomArchitect.Core.IO;

namespace DoomArchitect.Core.Tests.IO;

public class WadWriterTests
{
    [Fact]
    public void Write_ThenRead_RoundTripsNamesAndDataInOrder()
    {
        var lumps = new[]
        {
            new WadLump("MAP01", Array.Empty<byte>()),
            new WadLump("TEXTMAP", Encoding.ASCII.GetBytes("namespace = \"doom\";")),
            new WadLump("ENDMAP", Array.Empty<byte>()),
        };

        var bytes = WadWriter.Write(lumps);
        var wad = WadFile.Read(new MemoryStream(bytes));

        Assert.Equal(3, wad.Lumps.Count);
        Assert.Equal("MAP01", wad.Lumps[0].Name);
        Assert.Equal("TEXTMAP", wad.Lumps[1].Name);
        Assert.Equal("namespace = \"doom\";", Encoding.ASCII.GetString(wad.Lumps[1].Data));
        Assert.Equal("ENDMAP", wad.Lumps[2].Name);
    }

    [Fact]
    public void Write_AlwaysWritesAPwad()
    {
        var bytes = WadWriter.Write(Array.Empty<WadLump>());

        Assert.Equal("PWAD", Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public void Write_LumpNameLongerThanEightCharacters_IsTruncatedNotThrown()
    {
        var lumps = new[] { new WadLump("WAYTOOLONGNAME", new byte[] { 1, 2, 3 }) };

        var bytes = WadWriter.Write(lumps);
        var wad = WadFile.Read(new MemoryStream(bytes));

        Assert.Single(wad.Lumps);
        Assert.Equal("WAYTOOLO", wad.Lumps[0].Name);
    }

    [Fact]
    public void Write_LumpNameIsLowercase_IsUppercasedOnWrite()
    {
        var lumps = new[] { new WadLump("map01", Array.Empty<byte>()) };

        var bytes = WadWriter.Write(lumps);
        var wad = WadFile.Read(new MemoryStream(bytes));

        Assert.Equal("MAP01", wad.Lumps[0].Name);
    }

    [Fact]
    public void Write_MultipleLumpsWithVaryingSizes_EachRoundTripsItsOwnData()
    {
        var lumps = new[]
        {
            new WadLump("SMALL", new byte[] { 1 }),
            new WadLump("EMPTY", Array.Empty<byte>()),
            new WadLump("BIGGER", Enumerable.Range(0, 300).Select(i => (byte)i).ToArray()),
        };

        var bytes = WadWriter.Write(lumps);
        var wad = WadFile.Read(new MemoryStream(bytes));

        Assert.Equal(3, wad.Lumps.Count);
        Assert.Equal(new byte[] { 1 }, wad.Lumps[0].Data);
        Assert.Empty(wad.Lumps[1].Data);
        Assert.Equal(lumps[2].Data, wad.Lumps[2].Data);
    }
}
