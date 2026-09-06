using DoomArchitect.Core.Textures;
using static DoomArchitect.Core.Tests.Textures.TextureLumpTestBuilder;

namespace DoomArchitect.Core.Tests.Textures;

public class TextureDefinitionReaderTests
{
    private static readonly string[] Patches = { "PATCH1", "PATCH2" };

    [Fact]
    public void Read_Texture1_DropsFirstEntryOnly()
    {
        var entries = new[]
        {
            new TextureEntryDef("FIRST", 64, 64, new[] { new TexturePatchDef(0, 0, 0) }),
            new TextureEntryDef("SECOND", 64, 64, new[] { new TexturePatchDef(0, 0, 0) }),
        };
        var data = TextureDefinitions(entries);
        var warnings = new List<string>();

        var result = TextureDefinitionReader.Read(data, Patches, isTexture1: true, warnings);

        var definition = Assert.Single(result);
        Assert.Equal("SECOND", definition.Name);
    }

    [Fact]
    public void Read_Texture2_DoesNotDropFirstEntry()
    {
        var entries = new[]
        {
            new TextureEntryDef("FIRST", 64, 64, new[] { new TexturePatchDef(0, 0, 0) }),
            new TextureEntryDef("SECOND", 64, 64, new[] { new TexturePatchDef(0, 0, 0) }),
        };
        var data = TextureDefinitions(entries);
        var warnings = new List<string>();

        var result = TextureDefinitionReader.Read(data, Patches, isTexture1: false, warnings);

        Assert.Equal(new[] { "FIRST", "SECOND" }, result.Select(d => d.Name));
    }

    [Fact]
    public void Read_StrifeFormatEntry_ParsesPatchesWithoutTrailingFields()
    {
        var entries = new[]
        {
            new TextureEntryDef(
                "STRIFETEX", 64, 64, new[] { new TexturePatchDef(3, 4, 1) }, Strife: true),
        };
        var data = TextureDefinitions(entries);
        var warnings = new List<string>();

        // isTexture1: false so the single entry isn't dropped by the index-0 rule.
        var result = TextureDefinitionReader.Read(data, Patches, isTexture1: false, warnings);

        var definition = Assert.Single(result);
        var patch = Assert.Single(definition.Patches);
        Assert.Equal(3, patch.OriginX);
        Assert.Equal(4, patch.OriginY);
        Assert.Equal("PATCH2", patch.PatchName);
    }

    [Fact]
    public void Read_NonSequentialOffsets_StillResolvesEachEntryCorrectly()
    {
        var entries = new[]
        {
            new TextureEntryDef("ALPHA", 32, 32, new[] { new TexturePatchDef(0, 0, 0) }),
            new TextureEntryDef("BETA", 48, 48, new[] { new TexturePatchDef(0, 0, 1) }),
        };
        var data = TextureDefinitions(entries, sequential: false);
        var warnings = new List<string>();

        var result = TextureDefinitionReader.Read(data, Patches, isTexture1: false, warnings);

        Assert.Equal(new[] { "ALPHA", "BETA" }, result.Select(d => d.Name));
        Assert.Equal(32, result[0].Width);
        Assert.Equal(48, result[1].Width);
    }

    [Fact]
    public void Read_InvalidDimensionsOrPatchCount_SkipsEntryWithWarning()
    {
        var entries = new[]
        {
            new TextureEntryDef("BAD", 0, 64, new[] { new TexturePatchDef(0, 0, 0) }),
            new TextureEntryDef("GOOD", 64, 64, new[] { new TexturePatchDef(0, 0, 0) }),
        };
        var data = TextureDefinitions(entries);
        var warnings = new List<string>();

        var result = TextureDefinitionReader.Read(data, Patches, isTexture1: false, warnings);

        var definition = Assert.Single(result);
        Assert.Equal("GOOD", definition.Name);
        Assert.Contains(warnings, w => w.Contains("BAD") && w.Contains("invalid"));
    }

    [Fact]
    public void Read_OutOfRangePatchIndex_SkipsThatPatchButKeepsOthers()
    {
        var entries = new[]
        {
            new TextureEntryDef(
                "MIXED", 64, 64,
                new[] { new TexturePatchDef(0, 0, 99), new TexturePatchDef(4, 4, 0) }),
        };
        var data = TextureDefinitions(entries);
        var warnings = new List<string>();

        var result = TextureDefinitionReader.Read(data, Patches, isTexture1: false, warnings);

        var definition = Assert.Single(result);
        var patch = Assert.Single(definition.Patches);
        Assert.Equal("PATCH1", patch.PatchName);
        Assert.Contains(warnings, w => w.Contains("invalid patch index"));
    }
}
