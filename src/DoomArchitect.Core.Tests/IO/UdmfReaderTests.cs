using DoomArchitect.Core.IO;

namespace DoomArchitect.Core.Tests.IO;

public class UdmfReaderTests
{
    [Fact]
    public void Read_Vertex_DefaultsMissingCoordinatesToZero()
    {
        var doc = UdmfReader.Read("namespace = \"doom\"; vertex { }");

        var vertex = Assert.Single(doc.Map.Vertices);
        Assert.Equal(0f, vertex.Position.X);
        Assert.Equal(0f, vertex.Position.Y);
    }

    [Fact]
    public void Read_Vertex_ReadsCoordinates()
    {
        var doc = UdmfReader.Read("namespace = \"doom\"; vertex { x = 12.5; y = -3.0; }");

        var vertex = Assert.Single(doc.Map.Vertices);
        Assert.Equal(12.5f, vertex.Position.X);
        Assert.Equal(-3.0f, vertex.Position.Y);
    }

    [Fact]
    public void Read_Vertex_AcceptsIntegerLiteralForDoubleField()
    {
        // UDMF's own coercion rule: a bare integer is valid where a
        // double is expected.
        var doc = UdmfReader.Read("namespace = \"doom\"; vertex { x = 5; y = 10; }");

        var vertex = Assert.Single(doc.Map.Vertices);
        Assert.Equal(5f, vertex.Position.X);
        Assert.Equal(10f, vertex.Position.Y);
    }

    [Fact]
    public void Read_Sector_UsesUdbDefaultsForMissingFields()
    {
        var doc = UdmfReader.Read("namespace = \"doom\"; sector { }");

        var sector = Assert.Single(doc.Map.Sectors);
        Assert.Equal(0, sector.FloorHeight);
        Assert.Equal(0, sector.CeilingHeight);
        Assert.Equal("-", sector.FloorTexture);
        Assert.Equal("-", sector.CeilingTexture);
        Assert.Equal(160, sector.Brightness);
    }

    [Fact]
    public void Read_Sector_ReadsAllTypedFields()
    {
        var doc = UdmfReader.Read(
            "namespace = \"doom\"; sector { heightfloor = 0; heightceiling = 128; " +
            "texturefloor = \"FLOOR0_1\"; textureceiling = \"CEIL1_1\"; lightlevel = 200; }");

        var sector = Assert.Single(doc.Map.Sectors);
        Assert.Equal(0, sector.FloorHeight);
        Assert.Equal(128, sector.CeilingHeight);
        Assert.Equal("FLOOR0_1", sector.FloorTexture);
        Assert.Equal("CEIL1_1", sector.CeilingTexture);
        Assert.Equal(200, sector.Brightness);
    }

    [Fact]
    public void Read_LinedefWithoutSidedefs_UsesMinusOneSentinelForBothSides()
    {
        var text = "namespace = \"doom\"; " +
            "vertex { x = 0; y = 0; } vertex { x = 64; y = 0; } " +
            "linedef { v1 = 0; v2 = 1; }";

        var doc = UdmfReader.Read(text);

        var linedef = Assert.Single(doc.Map.Linedefs);
        Assert.Null(linedef.Front);
        Assert.Null(linedef.Back);
    }

    [Fact]
    public void Read_LinedefWithFrontSidedef_ResolvesSectorAndTextures()
    {
        var text = "namespace = \"doom\"; " +
            "vertex { x = 0; y = 0; } vertex { x = 64; y = 0; } " +
            "sector { } " +
            "sidedef { sector = 0; texturemiddle = \"STARTAN2\"; offsetx = 8; offsety = 16; } " +
            "linedef { v1 = 0; v2 = 1; sidefront = 0; }";

        var doc = UdmfReader.Read(text);

        var linedef = Assert.Single(doc.Map.Linedefs);
        Assert.NotNull(linedef.Front);
        Assert.Same(doc.Map.Sectors[0], linedef.Front!.Sector);
        Assert.Equal("STARTAN2", linedef.Front.MiddleTexture);
        Assert.Equal(8, linedef.Front.OffsetX);
        Assert.Equal(16, linedef.Front.OffsetY);
        Assert.Null(linedef.Back);
    }

    [Fact]
    public void Read_LinedefReferencingNonexistentVertex_IsDropped()
    {
        var text = "namespace = \"doom\"; vertex { x = 0; y = 0; } linedef { v1 = 0; v2 = 5; }";

        var doc = UdmfReader.Read(text);

        Assert.Empty(doc.Map.Linedefs);
        Assert.Contains(doc.Warnings, w => w.Contains("invalid") && w.Contains("vertices"));
    }

    [Fact]
    public void Read_ZeroLengthLinedef_IsDropped()
    {
        var text = "namespace = \"doom\"; vertex { x = 0; y = 0; } vertex { x = 0; y = 0; } " +
            "linedef { v1 = 0; v2 = 1; }";

        var doc = UdmfReader.Read(text);

        Assert.Empty(doc.Map.Linedefs);
        Assert.Contains(doc.Warnings, w => w.Contains("zero-length"));
    }

    [Fact]
    public void Read_LinedefWithOutOfRangeSidefront_KeepsLinedefButDropsThatSide()
    {
        var text = "namespace = \"doom\"; vertex { x = 0; y = 0; } vertex { x = 64; y = 0; } " +
            "linedef { v1 = 0; v2 = 1; sidefront = 5; }";

        var doc = UdmfReader.Read(text);

        var linedef = Assert.Single(doc.Map.Linedefs);
        Assert.Null(linedef.Front);
        Assert.Contains(doc.Warnings, w => w.Contains("invalid front sidedef"));
    }

    [Fact]
    public void Read_SidedefReferencingInvalidSector_IsDroppedButLinedefSurvives()
    {
        var text = "namespace = \"doom\"; vertex { x = 0; y = 0; } vertex { x = 64; y = 0; } " +
            "sidedef { sector = 9; } " +
            "linedef { v1 = 0; v2 = 1; sidefront = 0; }";

        var doc = UdmfReader.Read(text);

        var linedef = Assert.Single(doc.Map.Linedefs);
        Assert.Null(linedef.Front);
        Assert.Contains(doc.Warnings, w => w.Contains("invalid sector"));
    }

    [Fact]
    public void Read_UnknownTopLevelBlock_IsPreservedVerbatim()
    {
        var text = "namespace = \"doom\"; zone { id = 1; }";

        var doc = UdmfReader.Read(text);

        var zone = Assert.Single(doc.UnknownBlocks);
        Assert.Equal("zone", zone.Name);
        Assert.Equal(1L, zone.Find("id")!.Value.AsLong());
    }

    [Fact]
    public void Read_Thing_IsNoLongerSweptIntoUnknownBlocks()
    {
        var doc = UdmfReader.Read("namespace = \"doom\"; thing { x = 0; y = 0; type = 1; }");

        Assert.Empty(doc.UnknownBlocks);
        Assert.Single(doc.Map.Things);
    }

    [Fact]
    public void Read_Thing_UsesDefaultsForMissingOptionalFields()
    {
        var doc = UdmfReader.Read("namespace = \"doom\"; thing { x = 12.5; y = -3.0; type = 3001; }");

        var thing = Assert.Single(doc.Map.Things);
        Assert.Equal(12.5f, thing.Position.X);
        Assert.Equal(-3.0f, thing.Position.Y);
        Assert.Equal(3001, thing.Type);
        Assert.Equal(0.0, thing.Height);
        Assert.Equal(0, thing.Angle);
    }

    [Fact]
    public void Read_Thing_ReadsAllTypedFields()
    {
        var doc = UdmfReader.Read(
            "namespace = \"doom\"; thing { x = 64; y = 128; height = 16; angle = 90; type = 1; }");

        var thing = Assert.Single(doc.Map.Things);
        Assert.Equal(64f, thing.Position.X);
        Assert.Equal(128f, thing.Position.Y);
        Assert.Equal(16.0, thing.Height);
        Assert.Equal(90, thing.Angle);
        Assert.Equal(1, thing.Type);
    }

    [Fact]
    public void Read_UnrecognizedThingField_BecomesCustomField()
    {
        var doc = UdmfReader.Read(
            "namespace = \"doom\"; thing { x = 0; y = 0; type = 1; ambush = true; skill3 = true; id = 7; }");

        var thing = Assert.Single(doc.Map.Things);
        Assert.Equal(true, thing.CustomFields["ambush"]);
        Assert.Equal(true, thing.CustomFields["skill3"]);
        Assert.Equal(7L, thing.CustomFields["id"]);
    }

    [Fact]
    public void Read_UnrecognizedLinedefField_BecomesCustomField()
    {
        var text = "namespace = \"doom\"; vertex { x = 0; y = 0; } vertex { x = 64; y = 0; } " +
            "linedef { v1 = 0; v2 = 1; special = 1; arg0 = 5; }";

        var doc = UdmfReader.Read(text);

        var linedef = Assert.Single(doc.Map.Linedefs);
        Assert.Equal(1L, linedef.CustomFields["special"]);
        Assert.Equal(5L, linedef.CustomFields["arg0"]);
    }

    [Fact]
    public void Read_UnrecognizedSectorField_BecomesCustomField()
    {
        var doc = UdmfReader.Read("namespace = \"doom\"; sector { id = 7; }");

        var sector = Assert.Single(doc.Map.Sectors);
        Assert.Equal(7L, sector.CustomFields["id"]);
    }

    [Fact]
    public void Read_UnrecognizedVertexField_BecomesCustomField()
    {
        var doc = UdmfReader.Read("namespace = \"doom\"; vertex { x = 0; y = 0; zfloor = 12.0; }");

        var vertex = Assert.Single(doc.Map.Vertices);
        Assert.Equal(12.0, vertex.CustomFields["zfloor"]);
    }

    [Fact]
    public void Read_UnrecognizedSidedefField_BecomesCustomFieldOnTheSidedef()
    {
        var text = "namespace = \"doom\"; vertex { x = 0; y = 0; } vertex { x = 64; y = 0; } " +
            "sector { } sidedef { sector = 0; wrapmidtex = true; } " +
            "linedef { v1 = 0; v2 = 1; sidefront = 0; }";

        var doc = UdmfReader.Read(text);

        var linedef = Assert.Single(doc.Map.Linedefs);
        Assert.Equal(true, linedef.Front!.CustomFields["wrapmidtex"]);
    }

    [Fact]
    public void Read_MissingNamespace_DefaultsToDoomWithWarning()
    {
        var doc = UdmfReader.Read("vertex { x = 0; y = 0; }");

        Assert.Equal("doom", doc.Namespace);
        Assert.Contains(doc.Warnings, w => w.Contains("namespace"));
    }

    [Fact]
    public void Read_NanCoordinateField_IsDroppedAndDefaultsToZeroWithWarning()
    {
        // The tokenizer drops a "nan" keyword value before it ever
        // becomes a stored double, so there's no actual NaN left for the
        // reader to detect by the time it reads "x" - it just sees a
        // missing field and applies the ordinary default. Only the
        // affected coordinate defaults; an unrelated sibling field like
        // "y" here is unaffected.
        var doc = UdmfReader.Read("namespace = \"doom\"; vertex { x = nan; y = 5.0; }");

        var vertex = Assert.Single(doc.Map.Vertices);
        Assert.Equal(0f, vertex.Position.X);
        Assert.Equal(5f, vertex.Position.Y);
        Assert.Contains(doc.Warnings, w => w.Contains("NaN"));
    }
}
