using DoomArchitect.Core.IO;

namespace DoomArchitect.Core.Tests.IO;

public class WadFileTests
{
    [Fact]
    public void Read_ValidWad_ParsesLumpsInOrderWithCorrectData()
    {
        var bytes = WadTestBuilder.Build(
            ("MAP01", Array.Empty<byte>()),
            ("TEXTMAP", WadTestBuilder.TextLump("namespace = \"doom\";")),
            ("ENDMAP", Array.Empty<byte>()));

        var wad = WadFile.Read(new MemoryStream(bytes));

        Assert.Equal(3, wad.Lumps.Count);
        Assert.Equal("MAP01", wad.Lumps[0].Name);
        Assert.Equal("TEXTMAP", wad.Lumps[1].Name);
        Assert.Equal("namespace = \"doom\";", System.Text.Encoding.ASCII.GetString(wad.Lumps[1].Data));
        Assert.Equal("ENDMAP", wad.Lumps[2].Name);
    }

    [Fact]
    public void FindByPath_BareLumpName_DelegatesToFindLump()
    {
        // A WAD has no real path hierarchy - a ZScript #include inside one
        // just references another lump directly by name.
        var bytes = WadTestBuilder.Build(("ZSCRIPT2", WadTestBuilder.TextLump("class Actor {}")));
        var wad = WadFile.Read(new MemoryStream(bytes));

        var found = wad.FindByPath("ZSCRIPT2");

        Assert.Equal("class Actor {}", System.Text.Encoding.ASCII.GetString(found!));
    }

    [Fact]
    public void FindByPath_PathLikeIncludeString_StripsToTheBareLumpName()
    {
        var bytes = WadTestBuilder.Build(("ZSCRIPT2", WadTestBuilder.TextLump("class Actor {}")));
        var wad = WadFile.Read(new MemoryStream(bytes));

        // A nested path makes no sense for a WAD, but shouldn't crash -
        // just strip it down to a bare title and try that.
        var found = wad.FindByPath("some/path/ZSCRIPT2.txt");

        Assert.Equal("class Actor {}", System.Text.Encoding.ASCII.GetString(found!));
    }

    [Fact]
    public void Read_InvalidIdentification_Throws()
    {
        var bytes = new byte[] { (byte)'X', (byte)'X', (byte)'X', (byte)'X', 0, 0, 0, 0, 0, 0, 0, 0 };

        Assert.Throws<InvalidDataException>(() => WadFile.Read(new MemoryStream(bytes)));
    }

    [Fact]
    public void FindLump_MatchesCaseInsensitively()
    {
        var bytes = WadTestBuilder.Build(("VERTEXES", new byte[] { 1, 2, 3 }));

        var wad = WadFile.Read(new MemoryStream(bytes));

        Assert.NotNull(wad.FindLump("vertexes"));
    }

    [Fact]
    public void FindLump_Missing_ReturnsNull()
    {
        var bytes = WadTestBuilder.Build(("VERTEXES", Array.Empty<byte>()));

        var wad = WadFile.Read(new MemoryStream(bytes));

        Assert.Null(wad.FindLump("SECTORS"));
    }

    [Fact]
    public void ReadMapTextMap_TextMapImmediatelyAfterMarker_ReturnsItsText()
    {
        var bytes = WadTestBuilder.Build(
            ("MAP01", Array.Empty<byte>()),
            ("TEXTMAP", WadTestBuilder.TextLump("namespace = \"doom\";")),
            ("ENDMAP", Array.Empty<byte>()));

        var wad = WadFile.Read(new MemoryStream(bytes));

        Assert.Equal("namespace = \"doom\";", wad.ReadMapTextMap("MAP01"));
    }

    [Fact]
    public void ReadMapTextMap_MapNotFound_Throws()
    {
        var bytes = WadTestBuilder.Build(("MAP01", Array.Empty<byte>()));

        var wad = WadFile.Read(new MemoryStream(bytes));

        Assert.Throws<KeyNotFoundException>(() => wad.ReadMapTextMap("MAP02"));
    }

    [Fact]
    public void FindUdmfMapNames_ReturnsOnlyMapsFollowedByTextMap()
    {
        var bytes = WadTestBuilder.Build(
            ("MAP01", Array.Empty<byte>()),
            ("TEXTMAP", WadTestBuilder.TextLump("namespace = \"doom\";")),
            ("ENDMAP", Array.Empty<byte>()),
            ("E1M1", Array.Empty<byte>()),
            ("THINGS", Array.Empty<byte>()));

        var wad = WadFile.Read(new MemoryStream(bytes));

        Assert.Equal(new[] { "MAP01" }, wad.FindUdmfMapNames());
    }

    [Fact]
    public void FindLumpsBetweenMarkers_ReturnsEverythingStrictlyBetweenTheMarkers()
    {
        var bytes = WadTestBuilder.Build(
            ("OTHER", Array.Empty<byte>()),
            ("S_START", Array.Empty<byte>()),
            ("POSSA1", new byte[] { 1 }),
            ("TROOA1", new byte[] { 2 }),
            ("S_END", Array.Empty<byte>()),
            ("AFTER", Array.Empty<byte>()));

        var wad = WadFile.Read(new MemoryStream(bytes));

        var sprites = wad.FindLumpsBetweenMarkers("S_START", "S_END");

        Assert.Equal(new[] { "POSSA1", "TROOA1" }, sprites.Select(l => l.Name));
    }

    [Fact]
    public void FindLumpsBetweenMarkers_MissingStartMarker_ReturnsEmpty()
    {
        var bytes = WadTestBuilder.Build(("POSSA1", Array.Empty<byte>()), ("S_END", Array.Empty<byte>()));

        var wad = WadFile.Read(new MemoryStream(bytes));

        Assert.Empty(wad.FindLumpsBetweenMarkers("S_START", "S_END"));
    }

    [Fact]
    public void FindLumpsBetweenMarkers_MissingEndMarker_ReturnsEmpty()
    {
        var bytes = WadTestBuilder.Build(("S_START", Array.Empty<byte>()), ("POSSA1", Array.Empty<byte>()));

        var wad = WadFile.Read(new MemoryStream(bytes));

        Assert.Empty(wad.FindLumpsBetweenMarkers("S_START", "S_END"));
    }

    [Fact]
    public void FindLumpsBetweenMarkers_AdjacentMarkersWithNothingBetween_ReturnsEmpty()
    {
        var bytes = WadTestBuilder.Build(("S_START", Array.Empty<byte>()), ("S_END", Array.Empty<byte>()));

        var wad = WadFile.Read(new MemoryStream(bytes));

        Assert.Empty(wad.FindLumpsBetweenMarkers("S_START", "S_END"));
    }

    [Fact]
    public void ReadMapTextMap_ClassicBinaryFormatMap_ThrowsNotSupported()
    {
        // A pre-UDMF map's marker is immediately followed by THINGS, not
        // TEXTMAP - exactly what real id Software WADs look like.
        var bytes = WadTestBuilder.Build(
            ("E1M1", Array.Empty<byte>()),
            ("THINGS", new byte[] { 1, 2, 3 }),
            ("LINEDEFS", Array.Empty<byte>()));

        var wad = WadFile.Read(new MemoryStream(bytes));

        Assert.Throws<NotSupportedException>(() => wad.ReadMapTextMap("E1M1"));
    }
}
