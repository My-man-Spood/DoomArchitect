using System.Numerics;
using DoomArchitect.Core.IO;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Tests.IO;

public class UdmfWriterTests
{
    private static UdmfDocument EmptyDocument(MapData map) =>
        new(map, "doom", Array.Empty<UdmfBlock>(), Array.Empty<string>());

    [Fact]
    public void Write_Vertex_UsesDoubleFormatWithAtLeastOneDecimal()
    {
        var map = new MapData();
        map.CreateVertex(new Vector2(128f, 0f));

        var text = UdmfWriter.Write(EmptyDocument(map));

        Assert.Contains("x = 128.0;", text);
        Assert.Contains("y = 0.0;", text);
    }

    [Fact]
    public void Write_Sector_AlwaysWritesAllFiveCoreFieldsEvenAtDefaults()
    {
        var map = new MapData();
        map.CreateSector(0, 0);

        var text = UdmfWriter.Write(EmptyDocument(map));

        Assert.Contains("heightfloor = 0;", text);
        Assert.Contains("heightceiling = 0;", text);
        Assert.Contains("texturefloor = \"-\";", text);
        Assert.Contains("textureceiling = \"-\";", text);
        Assert.Contains("lightlevel = 160;", text);
    }

    [Fact]
    public void Write_SidedefAtDefaults_OmitsOffsetsAndTextures()
    {
        var map = new MapData();
        var v0 = map.CreateVertex(new Vector2(0, 0));
        var v1 = map.CreateVertex(new Vector2(64, 0));
        var sector = map.CreateSector(0, 128);
        map.CreateLinedef(v0, v1, sector, null);

        var text = UdmfWriter.Write(EmptyDocument(map));

        Assert.DoesNotContain("offsetx", text);
        Assert.DoesNotContain("offsety", text);
        Assert.DoesNotContain("texturetop", text);
        Assert.DoesNotContain("texturebottom", text);
        Assert.DoesNotContain("texturemiddle", text);
    }

    [Fact]
    public void Write_SidedefWithNonDefaultValues_WritesThem()
    {
        var map = new MapData();
        var v0 = map.CreateVertex(new Vector2(0, 0));
        var v1 = map.CreateVertex(new Vector2(64, 0));
        var sector = map.CreateSector(0, 128);
        var linedef = map.CreateLinedef(v0, v1, sector, null);
        linedef.Front!.OffsetX = 8;
        linedef.Front.MiddleTexture = "STARTAN2";

        var text = UdmfWriter.Write(EmptyDocument(map));

        Assert.Contains("offsetx = 8;", text);
        Assert.Contains("texturemiddle = \"STARTAN2\";", text);
        Assert.DoesNotContain("offsety", text);
    }

    [Fact]
    public void Write_LinedefWithNoBackSidedef_WritesMinusOneForSideback()
    {
        var map = new MapData();
        var v0 = map.CreateVertex(new Vector2(0, 0));
        var v1 = map.CreateVertex(new Vector2(64, 0));
        var sector = map.CreateSector(0, 128);
        map.CreateLinedef(v0, v1, sector, null);

        var text = UdmfWriter.Write(EmptyDocument(map));

        Assert.Contains("sideback = -1;", text);
        Assert.Contains("sidefront = 0;", text);
    }

    [Fact]
    public void Write_CustomFields_AreWrittenAfterTypedFields()
    {
        // Custom fields only ever get populated via UdmfReader in
        // practice (SetCustomField is internal, matching the rest of the
        // codebase's convention of no InternalsVisibleTo for tests) - so
        // build the input by loading a small document rather than hand-
        // constructing one.
        var doc = UdmfReader.Read("namespace = \"doom\"; sector { id = 7; }");

        var text = UdmfWriter.Write(doc);

        Assert.Contains("id = 7;", text);
    }

    [Fact]
    public void Write_UnknownBlocks_AreReemittedVerbatim()
    {
        var map = new MapData();
        var thing = new UdmfBlock(
            "thing",
            new List<UdmfAssignment>
            {
                new("x", UdmfValue.OfDouble(0.0)),
                new("type", UdmfValue.OfInt(1)),
            },
            new List<UdmfBlock>());
        var doc = new UdmfDocument(map, "doom", new[] { thing }, Array.Empty<string>());

        var text = UdmfWriter.Write(doc);

        Assert.Contains("thing", text);
        Assert.Contains("type = 1;", text);
    }

    [Fact]
    public void RoundTrip_BuildWriteRead_ReproducesEquivalentMap()
    {
        var map = new MapData();
        var v0 = map.CreateVertex(new Vector2(0, 0));
        var v1 = map.CreateVertex(new Vector2(256, 0));
        var v2 = map.CreateVertex(new Vector2(256, 256));
        var v3 = map.CreateVertex(new Vector2(0, 256));
        var outer = map.CreateSector(0, 128);
        outer.FloorTexture = "FLOOR0_1";
        outer.CeilingTexture = "CEIL1_1";
        outer.Brightness = 200;

        var l0 = map.CreateLinedef(v0, v1, outer, null);
        l0.Front!.MiddleTexture = "STARTAN2";
        map.CreateLinedef(v1, v2, outer, null);
        map.CreateLinedef(v2, v3, outer, null);
        map.CreateLinedef(v3, v0, outer, null);

        var text = UdmfWriter.Write(EmptyDocument(map));
        var doc = UdmfReader.Read(text);

        Assert.Equal(4, doc.Map.Vertices.Count);
        Assert.Equal(4, doc.Map.Linedefs.Count);
        var readSector = Assert.Single(doc.Map.Sectors);
        Assert.Equal("FLOOR0_1", readSector.FloorTexture);
        Assert.Equal("CEIL1_1", readSector.CeilingTexture);
        Assert.Equal(200, readSector.Brightness);

        var readL0 = doc.Map.Linedefs[0];
        Assert.Equal(v0.Position, readL0.Start.Position);
        Assert.Equal(v1.Position, readL0.End.Position);
        Assert.NotNull(readL0.Front);
        Assert.Equal("STARTAN2", readL0.Front!.MiddleTexture);
        Assert.Same(readSector, readL0.Front.Sector);
    }

    [Fact]
    public void RoundTrip_CustomFields_SurviveLoadThenSave()
    {
        var doc = UdmfReader.Read(
            "namespace = \"doom\"; vertex { x = 0; y = 0; } vertex { x = 64; y = 0; } " +
            "linedef { v1 = 0; v2 = 1; special = 1; arg0 = 5; }");

        var text = UdmfWriter.Write(doc);
        var reloaded = UdmfReader.Read(text);

        var linedef = Assert.Single(reloaded.Map.Linedefs);
        Assert.Equal(1L, linedef.CustomFields["special"]);
        Assert.Equal(5L, linedef.CustomFields["arg0"]);
    }
}
