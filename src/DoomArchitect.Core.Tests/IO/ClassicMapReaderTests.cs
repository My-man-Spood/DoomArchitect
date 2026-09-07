using DoomArchitect.Core.IO;

namespace DoomArchitect.Core.Tests.IO;

public class ClassicMapReaderTests
{
    private static byte[] BuildWad(
        byte[] linedefs, byte[] sidedefs, byte[] vertexes, byte[] sectors, string mapName = "MAP01",
        bool includeBehavior = false, byte[]? things = null)
    {
        var lumps = new List<(string, byte[])>
        {
            (mapName, Array.Empty<byte>()),
            ("THINGS", things ?? Array.Empty<byte>()),
            ("LINEDEFS", linedefs),
            ("SIDEDEFS", sidedefs),
            ("VERTEXES", vertexes),
            ("SEGS", Array.Empty<byte>()),
            ("SSECTORS", Array.Empty<byte>()),
            ("NODES", Array.Empty<byte>()),
            ("SECTORS", sectors),
            ("REJECT", Array.Empty<byte>()),
            ("BLOCKMAP", Array.Empty<byte>()),
        };

        if (includeBehavior) lumps.Add(("BEHAVIOR", Array.Empty<byte>()));

        return WadTestBuilder.Build(lumps.ToArray());
    }

    private static WadFile SimpleSquareRoom()
    {
        var vertexes = BinaryMapTestBuilder.Concat(
            BinaryMapTestBuilder.Vertex(0, 0),
            BinaryMapTestBuilder.Vertex(64, 0),
            BinaryMapTestBuilder.Vertex(64, 64),
            BinaryMapTestBuilder.Vertex(0, 64));

        var sectors = BinaryMapTestBuilder.Sector(0, 128, "FLOOR0_1", "CEIL1_1", 200);

        var sidedefs = BinaryMapTestBuilder.Sidedef(0, 0, "-", "-", "STARTAN2", 0);

        var linedefs = BinaryMapTestBuilder.Concat(
            BinaryMapTestBuilder.Linedef(0, 1, 0, 0, 0, 0, ushort.MaxValue),
            BinaryMapTestBuilder.Linedef(1, 2, 0, 0, 0, ushort.MaxValue, ushort.MaxValue),
            BinaryMapTestBuilder.Linedef(2, 3, 0, 0, 0, ushort.MaxValue, ushort.MaxValue),
            BinaryMapTestBuilder.Linedef(3, 0, 0, 0, 0, ushort.MaxValue, ushort.MaxValue));

        return WadFile.Read(new MemoryStream(BuildWad(linedefs, sidedefs, vertexes, sectors)));
    }

    [Fact]
    public void Read_SimpleRoom_ProducesExpectedMap()
    {
        var wad = SimpleSquareRoom();

        var (map, _) = ClassicMapReader.Read(wad, "MAP01");

        Assert.Equal(4, map.Vertices.Count);
        Assert.Equal(4, map.Linedefs.Count);
        var sector = Assert.Single(map.Sectors);
        Assert.Equal(0, sector.FloorHeight);
        Assert.Equal(128, sector.CeilingHeight);
        Assert.Equal("FLOOR0_1", sector.FloorTexture);
        Assert.Equal("CEIL1_1", sector.CeilingTexture);
        Assert.Equal(200, sector.Brightness);

        var linedef = map.Linedefs[0];
        Assert.NotNull(linedef.Front);
        Assert.Null(linedef.Back);
        Assert.Same(sector, linedef.Front!.Sector);
        Assert.Equal("STARTAN2", linedef.Front.MiddleTexture);
    }

    [Fact]
    public void Read_Things_ParsesPositionAngleTypeAndFlags()
    {
        var vertexes = BinaryMapTestBuilder.Vertex(0, 0);
        var things = BinaryMapTestBuilder.Concat(
            BinaryMapTestBuilder.Thing(64, 128, 90, 1, 7),
            BinaryMapTestBuilder.Thing(-32, 16, 180, 3001, 0));
        var wad = WadFile.Read(new MemoryStream(
            BuildWad(Array.Empty<byte>(), Array.Empty<byte>(), vertexes, Array.Empty<byte>(), things: things)));

        var (map, _) = ClassicMapReader.Read(wad, "MAP01");

        Assert.Equal(2, map.Things.Count);

        var first = map.Things[0];
        Assert.Equal(new System.Numerics.Vector2(64, 128), first.Position);
        Assert.Equal(90, first.Angle);
        Assert.Equal(1, first.Type);
        Assert.Equal(7, first.RawFlags);
        Assert.Equal(0, first.Height);

        var second = map.Things[1];
        Assert.Equal(new System.Numerics.Vector2(-32, 16), second.Position);
        Assert.Equal(180, second.Angle);
        Assert.Equal(3001, second.Type);
        Assert.Equal(0, second.RawFlags);
    }

    [Fact]
    public void Read_SidedefTextureFields_MapToTheCorrectSlots()
    {
        // The gotcha this test exists for: field order is
        // upper-then-lower-then-middle, not the commonly-assumed
        // upper-then-middle-then-lower.
        var vertexes = BinaryMapTestBuilder.Concat(
            BinaryMapTestBuilder.Vertex(0, 0), BinaryMapTestBuilder.Vertex(64, 0));
        var sectors = BinaryMapTestBuilder.Sector(0, 128, "-", "-", 160);
        var sidedefs = BinaryMapTestBuilder.Sidedef(0, 0, "UPPERTEX", "LOWERTEX", "MIDTEX", 0);
        var linedefs = BinaryMapTestBuilder.Linedef(0, 1, 0, 0, 0, 0, ushort.MaxValue);

        var wad = WadFile.Read(new MemoryStream(BuildWad(linedefs, sidedefs, vertexes, sectors)));
        var (map, _) = ClassicMapReader.Read(wad, "MAP01");

        var front = map.Linedefs[0].Front!;
        Assert.Equal("UPPERTEX", front.UpperTexture);
        Assert.Equal("LOWERTEX", front.LowerTexture);
        Assert.Equal("MIDTEX", front.MiddleTexture);
    }

    [Fact]
    public void Read_NoSidedefSentinel_LeavesBothSidesNull()
    {
        var vertexes = BinaryMapTestBuilder.Concat(
            BinaryMapTestBuilder.Vertex(0, 0), BinaryMapTestBuilder.Vertex(64, 0));
        var linedefs = BinaryMapTestBuilder.Linedef(0, 1, 0, 0, 0, ushort.MaxValue, ushort.MaxValue);

        var wad = WadFile.Read(new MemoryStream(BuildWad(linedefs, Array.Empty<byte>(), vertexes, Array.Empty<byte>())));
        var (map, _) = ClassicMapReader.Read(wad, "MAP01");

        var linedef = Assert.Single(map.Linedefs);
        Assert.Null(linedef.Front);
        Assert.Null(linedef.Back);
    }

    [Fact]
    public void Read_LinedefReferencingInvalidVertex_IsDropped()
    {
        var vertexes = BinaryMapTestBuilder.Vertex(0, 0);
        var linedefs = BinaryMapTestBuilder.Linedef(0, 5, 0, 0, 0, ushort.MaxValue, ushort.MaxValue);

        var wad = WadFile.Read(new MemoryStream(BuildWad(linedefs, Array.Empty<byte>(), vertexes, Array.Empty<byte>())));
        var (map, warnings) = ClassicMapReader.Read(wad, "MAP01");

        Assert.Empty(map.Linedefs);
        Assert.Contains(warnings, w => w.Contains("invalid") && w.Contains("vertices"));
    }

    [Fact]
    public void Read_ZeroLengthLinedef_IsDropped()
    {
        var vertexes = BinaryMapTestBuilder.Concat(
            BinaryMapTestBuilder.Vertex(0, 0), BinaryMapTestBuilder.Vertex(0, 0));
        var linedefs = BinaryMapTestBuilder.Linedef(0, 1, 0, 0, 0, ushort.MaxValue, ushort.MaxValue);

        var wad = WadFile.Read(new MemoryStream(BuildWad(linedefs, Array.Empty<byte>(), vertexes, Array.Empty<byte>())));
        var (map, warnings) = ClassicMapReader.Read(wad, "MAP01");

        Assert.Empty(map.Linedefs);
        Assert.Contains(warnings, w => w.Contains("zero-length"));
    }

    [Fact]
    public void Read_OutOfRangeSidefront_KeepsLinedefButDropsThatSide()
    {
        var vertexes = BinaryMapTestBuilder.Concat(
            BinaryMapTestBuilder.Vertex(0, 0), BinaryMapTestBuilder.Vertex(64, 0));
        var linedefs = BinaryMapTestBuilder.Linedef(0, 1, 0, 0, 0, 5, ushort.MaxValue);

        var wad = WadFile.Read(new MemoryStream(BuildWad(linedefs, Array.Empty<byte>(), vertexes, Array.Empty<byte>())));
        var (map, warnings) = ClassicMapReader.Read(wad, "MAP01");

        var linedef = Assert.Single(map.Linedefs);
        Assert.Null(linedef.Front);
        Assert.Contains(warnings, w => w.Contains("invalid sidedef"));
    }

    [Fact]
    public void Read_SidedefReferencingInvalidSector_IsDroppedButLinedefSurvives()
    {
        var vertexes = BinaryMapTestBuilder.Concat(
            BinaryMapTestBuilder.Vertex(0, 0), BinaryMapTestBuilder.Vertex(64, 0));
        var sidedefs = BinaryMapTestBuilder.Sidedef(0, 0, "-", "-", "-", 9);
        var linedefs = BinaryMapTestBuilder.Linedef(0, 1, 0, 0, 0, 0, ushort.MaxValue);

        var wad = WadFile.Read(new MemoryStream(BuildWad(linedefs, sidedefs, vertexes, Array.Empty<byte>())));
        var (map, warnings) = ClassicMapReader.Read(wad, "MAP01");

        var linedef = Assert.Single(map.Linedefs);
        Assert.Null(linedef.Front);
        Assert.Contains(warnings, w => w.Contains("invalid sector"));
    }

    [Fact]
    public void Read_LinedefAndSectorSpecialsAndTags_BecomeCustomFields()
    {
        var vertexes = BinaryMapTestBuilder.Concat(
            BinaryMapTestBuilder.Vertex(0, 0), BinaryMapTestBuilder.Vertex(64, 0));
        var sectors = BinaryMapTestBuilder.Sector(0, 128, "-", "-", 160, special: 9, tag: 3);
        var sidedefs = BinaryMapTestBuilder.Sidedef(0, 0, "-", "-", "-", 0);
        var linedefs = BinaryMapTestBuilder.Linedef(0, 1, flags: 1, special: 97, tag: 5, sidefront: 0, sideback: ushort.MaxValue);

        var wad = WadFile.Read(new MemoryStream(BuildWad(linedefs, sidedefs, vertexes, sectors)));
        var (map, _) = ClassicMapReader.Read(wad, "MAP01");

        var linedef = Assert.Single(map.Linedefs);
        Assert.Equal(1L, linedef.CustomFields["flags"]);
        Assert.Equal(97L, linedef.CustomFields["special"]);
        Assert.Equal(5L, linedef.CustomFields["id"]);

        var sector = Assert.Single(map.Sectors);
        Assert.Equal(9L, sector.CustomFields["special"]);
        Assert.Equal(3L, sector.CustomFields["id"]);
    }

    [Fact]
    public void Read_HexenFormatMap_ThrowsNotSupported()
    {
        var vertexes = BinaryMapTestBuilder.Vertex(0, 0);
        var wad = WadFile.Read(new MemoryStream(
            BuildWad(Array.Empty<byte>(), Array.Empty<byte>(), vertexes, Array.Empty<byte>(), includeBehavior: true)));

        Assert.Throws<NotSupportedException>(() => ClassicMapReader.Read(wad, "MAP01"));
    }

    [Fact]
    public void Read_MissingRequiredLump_ThrowsNotSupported()
    {
        var bytes = WadTestBuilder.Build(("MAP01", Array.Empty<byte>()), ("THINGS", Array.Empty<byte>()));
        var wad = WadFile.Read(new MemoryStream(bytes));

        Assert.Throws<NotSupportedException>(() => ClassicMapReader.Read(wad, "MAP01"));
    }

    [Fact]
    public void Read_MapNotFound_Throws()
    {
        var wad = SimpleSquareRoom();

        Assert.Throws<KeyNotFoundException>(() => ClassicMapReader.Read(wad, "MAP02"));
    }

    [Fact]
    public void FindClassicMapNames_FindsMarkersFollowedByThings()
    {
        var wad = SimpleSquareRoom();

        Assert.Equal(new[] { "MAP01" }, wad.FindClassicMapNames());
    }
}
