using System.Text;
using DoomArchitect.Core.IO;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Tests.Textures;
using DoomArchitect.Core.Textures;

namespace DoomArchitect.Core.Tests.IO;

public class MapFileSaverTests
{
    [Fact]
    public void BuildLumpsForSave_NullOriginalLumps_ReturnsJustMarkerTextMapEndMap()
    {
        var result = MapFileSaver.BuildLumpsForSave(null, "MAP01", "namespace = \"doom\";");

        Assert.Equal(3, result.Count);
        Assert.Equal("MAP01", result[0].Name);
        Assert.Equal("TEXTMAP", result[1].Name);
        Assert.Equal("namespace = \"doom\";", Encoding.ASCII.GetString(result[1].Data));
        Assert.Equal("ENDMAP", result[2].Name);
    }

    [Fact]
    public void BuildLumpsForSave_MapNotPresent_AppendsFreshGroupLeavingEverythingElseUntouched()
    {
        var original = new[]
        {
            new WadLump("MAP01", Array.Empty<byte>()),
            new WadLump("TEXTMAP", Encoding.ASCII.GetBytes("old map01 text")),
            new WadLump("ENDMAP", Array.Empty<byte>()),
        };

        var result = MapFileSaver.BuildLumpsForSave(original, "MAP02", "namespace = \"doom\";");

        Assert.Equal(6, result.Count);
        Assert.Equal("MAP01", result[0].Name);
        Assert.Equal("TEXTMAP", result[1].Name);
        Assert.Equal("old map01 text", Encoding.ASCII.GetString(result[1].Data));
        Assert.Equal("ENDMAP", result[2].Name);
        Assert.Equal("MAP02", result[3].Name);
        Assert.Equal("TEXTMAP", result[4].Name);
        Assert.Equal("namespace = \"doom\";", Encoding.ASCII.GetString(result[4].Data));
        Assert.Equal("ENDMAP", result[5].Name);
    }

    [Fact]
    public void BuildLumpsForSave_ExistingUdmfGroup_ReplacesOnlyTextMapPreservingOtherLumpsByteForByte()
    {
        var behaviorData = new byte[] { 1, 2, 3, 4 };
        var original = new[]
        {
            new WadLump("MAP01", Array.Empty<byte>()),
            new WadLump("TEXTMAP", Encoding.ASCII.GetBytes("old text")),
            new WadLump("BEHAVIOR", behaviorData),
            new WadLump("ENDMAP", Array.Empty<byte>()),
        };

        var result = MapFileSaver.BuildLumpsForSave(original, "MAP01", "new text");

        Assert.Equal(4, result.Count);
        Assert.Equal("MAP01", result[0].Name);
        Assert.Equal("TEXTMAP", result[1].Name);
        Assert.Equal("new text", Encoding.ASCII.GetString(result[1].Data));
        Assert.Equal("BEHAVIOR", result[2].Name);
        Assert.Same(behaviorData, result[2].Data);
        Assert.Equal("ENDMAP", result[3].Name);
    }

    [Fact]
    public void BuildLumpsForSave_ExistingUdmfGroup_LeavesLumpsAfterTheGroupUntouched()
    {
        var original = new[]
        {
            new WadLump("MAP01", Array.Empty<byte>()),
            new WadLump("TEXTMAP", Encoding.ASCII.GetBytes("old text")),
            new WadLump("ENDMAP", Array.Empty<byte>()),
            new WadLump("MAP02", Array.Empty<byte>()),
            new WadLump("TEXTMAP", Encoding.ASCII.GetBytes("map02 text")),
            new WadLump("ENDMAP", Array.Empty<byte>()),
        };

        var result = MapFileSaver.BuildLumpsForSave(original, "MAP01", "new text");

        Assert.Equal(6, result.Count);
        Assert.Equal("MAP02", result[3].Name);
        Assert.Equal("map02 text", Encoding.ASCII.GetString(result[4].Data));
    }

    [Fact]
    public void BuildLumpsForSave_ExistingClassicGroup_RemovesItWhollyAndSplicesInAFreshUdmfGroup()
    {
        var original = new[]
        {
            new WadLump("MAP01", Array.Empty<byte>()),
            new WadLump("THINGS", new byte[10]),
            new WadLump("LINEDEFS", new byte[14]),
            new WadLump("SIDEDEFS", new byte[30]),
            new WadLump("VERTEXES", new byte[4]),
            new WadLump("SECTORS", new byte[26]),
            new WadLump("MAP02", Array.Empty<byte>()),
        };

        var result = MapFileSaver.BuildLumpsForSave(original, "MAP01", "namespace = \"doom\";");

        Assert.Equal(4, result.Count);
        Assert.Equal("MAP01", result[0].Name);
        Assert.Equal("TEXTMAP", result[1].Name);
        Assert.Equal("namespace = \"doom\";", Encoding.ASCII.GetString(result[1].Data));
        Assert.Equal("ENDMAP", result[2].Name);
        Assert.Equal("MAP02", result[3].Name);
    }

    /// <summary>
    /// Reproduces a real, common single-map PWAD layout: a classic
    /// (pre-upgrade) map group immediately followed by the WAD's own
    /// embedded PNAMES/TEXTURE1 (a composited wall texture built from one
    /// patch), a P_START/P_END-wrapped patch, and an F_START/F_END-wrapped
    /// flat - none of which are map-group lump names, but all of which sit
    /// directly adjacent to the group being removed/replaced. A real
    /// end-to-end check (round-tripped through <see cref="TextureSet.Load"/>,
    /// not just raw lump survival) that saving a map never drops or
    /// corrupts a WAD's own embedded texture resources - a user-reported
    /// concern this test exists to pin down.
    /// </summary>
    [Fact]
    public void SaveUdmfMap_WithEmbeddedResourceLumpsAfterAClassicGroup_PreservesThemFullyDecodable()
    {
        var palette = TextureLumpTestBuilder.Playpal((10, 20, 30));
        var patchData = TextureLumpTestBuilder.Patch(
            height: 2, new (byte, byte[])[] { (0, new byte[] { 0, 0 }) });
        var pnamesData = TextureLumpTestBuilder.PatchNames("MYPATCH1");
        var texture1Data = TextureLumpTestBuilder.TextureDefinitions(new[]
        {
            new TextureLumpTestBuilder.TextureEntryDef(
                "MYWALL01", 2, 2, new[] { new TextureLumpTestBuilder.TexturePatchDef(0, 0, 0) }),
        });
        var flatData = TextureLumpTestBuilder.Flat(2, 2, fillIndex: 0);

        var original = new[]
        {
            new WadLump("PLAYPAL", palette),
            new WadLump("MAP01", Array.Empty<byte>()),
            new WadLump("THINGS", Array.Empty<byte>()),
            new WadLump("LINEDEFS", Array.Empty<byte>()),
            new WadLump("SIDEDEFS", Array.Empty<byte>()),
            new WadLump("VERTEXES", Array.Empty<byte>()),
            new WadLump("SECTORS", Array.Empty<byte>()),
            new WadLump("PNAMES", pnamesData),
            new WadLump("TEXTURE2", texture1Data),
            new WadLump("P_START", Array.Empty<byte>()),
            new WadLump("MYPATCH1", patchData),
            new WadLump("P_END", Array.Empty<byte>()),
            new WadLump("F_START", Array.Empty<byte>()),
            new WadLump("MYFLAT1", flatData),
            new WadLump("F_END", Array.Empty<byte>()),
        };

        var map = new MapData();
        var document = new UdmfDocument(map, "doom", Array.Empty<UdmfBlock>(), Array.Empty<string>());

        var bytes = MapFileSaver.SaveUdmfMap(original, document, "MAP01");
        var wad = WadFile.Read(new MemoryStream(bytes));

        Assert.Equal(pnamesData, wad.FindLump("PNAMES")?.Data);
        Assert.Equal(texture1Data, wad.FindLump("TEXTURE2")?.Data);
        Assert.Equal(patchData, wad.FindLump("MYPATCH1")?.Data);
        Assert.Equal(flatData, wad.FindLump("MYFLAT1")?.Data);

        var textures = TextureSet.Load(wad);
        Assert.Contains("MYWALL01", textures.GetWallTextureNames());
        Assert.Contains("MYFLAT1", textures.GetFlatNames());

        var wallImage = textures.GetWallTexture("MYWALL01");
        Assert.Equal(2, wallImage.Width);
        Assert.Equal(2, wallImage.Height);
        Assert.Empty(textures.Warnings);
    }

    /// <summary>Same real-world concern as <see cref="SaveUdmfMap_WithEmbeddedResourceLumpsAfterAClassicGroup_PreservesThemFullyDecodable"/>, but for the more likely repro shape: re-saving a map that's already UDMF (a second Save, or a GZDoom UDMF PWAD opened directly) with its own embedded resource lumps following the TEXTMAP/ENDMAP group.</summary>
    [Fact]
    public void SaveUdmfMap_WithEmbeddedResourceLumpsAfterAnExistingUdmfGroup_PreservesThemFullyDecodable()
    {
        var palette = TextureLumpTestBuilder.Playpal((10, 20, 30));
        var patchData = TextureLumpTestBuilder.Patch(
            height: 2, new (byte, byte[])[] { (0, new byte[] { 0, 0 }) });
        var pnamesData = TextureLumpTestBuilder.PatchNames("MYPATCH1");
        var texture2Data = TextureLumpTestBuilder.TextureDefinitions(new[]
        {
            new TextureLumpTestBuilder.TextureEntryDef(
                "MYWALL01", 2, 2, new[] { new TextureLumpTestBuilder.TexturePatchDef(0, 0, 0) }),
        });
        var flatData = TextureLumpTestBuilder.Flat(2, 2, fillIndex: 0);

        var original = new[]
        {
            new WadLump("PLAYPAL", palette),
            new WadLump("MAP01", Array.Empty<byte>()),
            new WadLump("TEXTMAP", Encoding.ASCII.GetBytes("namespace = \"zdoom\";")),
            new WadLump("ENDMAP", Array.Empty<byte>()),
            new WadLump("PNAMES", pnamesData),
            new WadLump("TEXTURE2", texture2Data),
            new WadLump("P_START", Array.Empty<byte>()),
            new WadLump("MYPATCH1", patchData),
            new WadLump("P_END", Array.Empty<byte>()),
            new WadLump("F_START", Array.Empty<byte>()),
            new WadLump("MYFLAT1", flatData),
            new WadLump("F_END", Array.Empty<byte>()),
        };

        var map = new MapData();
        var document = new UdmfDocument(map, "zdoom", Array.Empty<UdmfBlock>(), Array.Empty<string>());

        var bytes = MapFileSaver.SaveUdmfMap(original, document, "MAP01");
        var wad = WadFile.Read(new MemoryStream(bytes));

        Assert.Equal(pnamesData, wad.FindLump("PNAMES")?.Data);
        Assert.Equal(texture2Data, wad.FindLump("TEXTURE2")?.Data);
        Assert.Equal(patchData, wad.FindLump("MYPATCH1")?.Data);
        Assert.Equal(flatData, wad.FindLump("MYFLAT1")?.Data);

        var textures = TextureSet.Load(wad);
        Assert.Contains("MYWALL01", textures.GetWallTextureNames());
        Assert.Contains("MYFLAT1", textures.GetFlatNames());
        Assert.Empty(textures.Warnings);
    }

    [Fact]
    public void SaveUdmfMap_RoundTripsThroughWadWriterAndWadFileRead()
    {
        var map = new MapData();
        map.CreateVertex(new System.Numerics.Vector2(0f, 0f));
        var document = new UdmfDocument(map, "doom", Array.Empty<UdmfBlock>(), Array.Empty<string>());

        var bytes = MapFileSaver.SaveUdmfMap(null, document, "MAP01");
        var wad = WadFile.Read(new MemoryStream(bytes));

        Assert.Equal(new[] { "MAP01" }, wad.FindUdmfMapNames());
        Assert.Contains("x = 0.0;", wad.ReadMapTextMap("MAP01"));
    }
}
