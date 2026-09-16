using System.Linq;
using DoomArchitect.Core.IO;
using DoomArchitect.Core.Tests.IO;
using DoomArchitect.Core.Textures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using static DoomArchitect.Core.Tests.Textures.TextureLumpTestBuilder;

namespace DoomArchitect.Core.Tests.Textures;

public class TextureSetTests
{
    private static WadFile BuildWad(params (string Name, byte[] Data)[] lumps) =>
        WadFile.Read(new MemoryStream(WadTestBuilder.Build(lumps)));

    private static byte[] PngBytes()
    {
        using var image = new Image<Rgba32>(1, 1);
        image[0, 0] = new Rgba32(1, 2, 3, 255);
        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        return stream.ToArray();
    }

    [Fact]
    public void GetFlatTexture_UnknownName_ReturnsPlaceholderAndWarns()
    {
        var wad = BuildWad();
        var set = TextureSet.Load(wad);

        var image = set.GetFlatTexture("NOSUCHFLAT");

        Assert.NotNull(image);
        Assert.Contains(set.Warnings, w => w.Contains("NOSUCHFLAT"));
    }

    [Fact]
    public void GetFlatTexture_SameNameRequestedTwice_ReturnsSameCachedInstance()
    {
        var wad = BuildWad(("MYFLAT", Flat(64, 64, 5)));
        var set = TextureSet.Load(wad);

        var first = set.GetFlatTexture("MYFLAT");
        var second = set.GetFlatTexture("MYFLAT");

        Assert.Same(first, second);
    }

    [Fact]
    public void GetWallTexture_KnownComposite_BuildsFromPatches()
    {
        var patch = Patch(height: 8, new (byte, byte[])[] { (0, new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 }) });
        var entries = new[]
        {
            new TextureEntryDef("MYWALL", 1, 8, new[] { new TexturePatchDef(0, 0, 0) }),
        };

        // TEXTURE1's index-0 entry is always dropped, so this single-entry
        // definition list is put in TEXTURE2 to keep it intact.
        var wad = BuildWad(
            ("PLAYPAL", Playpal((9, 9, 9))),
            ("PNAMES", PatchNames("MYPATCH")),
            ("TEXTURE2", TextureDefinitions(entries)),
            ("MYPATCH", patch));
        var set = TextureSet.Load(wad);

        var image = set.GetWallTexture("MYWALL");

        Assert.Equal(1, image.Width);
        Assert.Equal(8, image.Height);
        Assert.Empty(set.Warnings);
    }

    [Fact]
    public void GetWallTexture_UnknownName_ReturnsPlaceholderAndWarns()
    {
        var wad = BuildWad();
        var set = TextureSet.Load(wad);

        var image = set.GetWallTexture("NOSUCHTEX");

        Assert.NotNull(image);
        Assert.Contains(set.Warnings, w => w.Contains("NOSUCHTEX"));
    }

    [Fact]
    public void TryGetSpriteTexture_LumpWithinTheSpriteRange_DecodesIt()
    {
        var sprite = Patch(height: 1, new (byte, byte[])[] { (0, new byte[] { 0 }) });
        var wad = BuildWad(
            ("PLAYPAL", Playpal((9, 9, 9))),
            ("S_START", Array.Empty<byte>()),
            ("POSSA1", sprite),
            ("S_END", Array.Empty<byte>()));
        var set = TextureSet.Load(wad);

        var image = set.TryGetSpriteTexture("POSSA1");

        Assert.NotNull(image);
        Assert.Equal(1, image!.Width);
    }

    [Fact]
    public void TryGetSpriteTexture_NameNotInTheSpriteRange_ReturnsNull()
    {
        var wad = BuildWad(("S_START", Array.Empty<byte>()), ("S_END", Array.Empty<byte>()));
        var set = TextureSet.Load(wad);

        Assert.Null(set.TryGetSpriteTexture("POSSA1"));
    }

    [Fact]
    public void TryGetSpriteTexture_NoSpriteMarkersAtAll_ReturnsNull()
    {
        var wad = BuildWad();
        var set = TextureSet.Load(wad);

        Assert.Null(set.TryGetSpriteTexture("POSSA1"));
    }

    [Fact]
    public void TryGetSpriteTexture_SameNameRequestedTwice_ReturnsSameCachedInstance()
    {
        var sprite = Patch(height: 1, new (byte, byte[])[] { (0, new byte[] { 0 }) });
        var wad = BuildWad(
            ("PLAYPAL", Playpal((9, 9, 9))),
            ("S_START", Array.Empty<byte>()),
            ("POSSA1", sprite),
            ("S_END", Array.Empty<byte>()));
        var set = TextureSet.Load(wad);

        var first = set.TryGetSpriteTexture("POSSA1");
        var second = set.TryGetSpriteTexture("POSSA1");

        Assert.Same(first, second);
    }

    [Fact]
    public void ResolveSpriteRotations_EightSeparateLumps_MapsEachRotationDigitToItsOwnLumpNoMirror()
    {
        var wad = BuildWad(new[] { ("S_START", Array.Empty<byte>()) }
            .Concat(Enumerable.Range(1, 8).Select(r => ($"TROOA{r}", Array.Empty<byte>())))
            .Append(("S_END", Array.Empty<byte>()))
            .ToArray());
        var set = TextureSet.Load(wad);

        var rotations = set.ResolveSpriteRotations("TROOA1");

        Assert.Equal(8, rotations.Count);
        for (var i = 0; i < 8; i++)
        {
            Assert.Equal($"TROOA{i + 1}", rotations[i].LumpName);
            Assert.False(rotations[i].Mirror);
        }
    }

    [Fact]
    public void ResolveSpriteRotations_MirroredPairLumps_ResolvesBothRotationsWithCorrectMirrorFlag()
    {
        var wad = BuildWad(
            ("S_START", Array.Empty<byte>()),
            ("TROOA1", Array.Empty<byte>()),
            ("TROOA2A8", Array.Empty<byte>()),
            ("TROOA3A7", Array.Empty<byte>()),
            ("TROOA4A6", Array.Empty<byte>()),
            ("TROOA5", Array.Empty<byte>()),
            ("S_END", Array.Empty<byte>()));
        var set = TextureSet.Load(wad);

        var rotations = set.ResolveSpriteRotations("TROOA1");

        Assert.Equal("TROOA1", rotations[0].LumpName);
        Assert.False(rotations[0].Mirror);
        Assert.Equal("TROOA2A8", rotations[1].LumpName);
        Assert.False(rotations[1].Mirror);
        Assert.Equal("TROOA2A8", rotations[7].LumpName);
        Assert.True(rotations[7].Mirror);
        Assert.Equal("TROOA4A6", rotations[3].LumpName);
        Assert.False(rotations[3].Mirror);
        Assert.Equal("TROOA4A6", rotations[5].LumpName);
        Assert.True(rotations[5].Mirror);
        Assert.Equal("TROOA5", rotations[4].LumpName);
        Assert.False(rotations[4].Mirror);
    }

    [Fact]
    public void ResolveSpriteRotations_SingleNonRotatingLump_FillsEverySlotWithItNoMirror()
    {
        var wad = BuildWad(("S_START", Array.Empty<byte>()), ("PLASA0", Array.Empty<byte>()), ("S_END", Array.Empty<byte>()));
        var set = TextureSet.Load(wad);

        var rotations = set.ResolveSpriteRotations("PLASA0");

        Assert.All(rotations, r => Assert.Equal("PLASA0", r.LumpName));
        Assert.All(rotations, r => Assert.False(r.Mirror));
    }

    [Fact]
    public void ResolveSpriteRotations_NoMatchingLumpsAtAll_FallsBackToTheRepresentativeNameForEverySlot()
    {
        var wad = BuildWad(("S_START", Array.Empty<byte>()), ("S_END", Array.Empty<byte>()));
        var set = TextureSet.Load(wad);

        var rotations = set.ResolveSpriteRotations("TROOA2A8");

        Assert.All(rotations, r => Assert.Equal("TROOA2A8", r.LumpName));
        Assert.All(rotations, r => Assert.False(r.Mirror));
    }

    [Fact]
    public void Load_ResourceSet_ResolvesEverythingFromALowerPriorityResourceWad()
    {
        // The exact shape of the real bug this fixes: a "PWAD" with none
        // of its own embedded resources, layered over an "IWAD" resource
        // that has everything - palette, flat, and sprite should all
        // resolve from the IWAD, not just fall back to placeholders.
        var sprite = Patch(height: 1, new (byte, byte[])[] { (0, new byte[] { 0 }) });
        var iwad = BuildWad(
            ("PLAYPAL", Playpal((9, 9, 9))),
            ("MYFLAT", Flat(64, 64, 5)),
            ("S_START", Array.Empty<byte>()),
            ("POSSA1", sprite),
            ("S_END", Array.Empty<byte>()));
        var pwad = BuildWad(("MAP01", Array.Empty<byte>()));

        var resources = new DoomArchitect.Core.IO.ResourceSet(new DoomArchitect.Core.IO.IResourceContainer[] { iwad, pwad });
        var set = TextureSet.Load(resources);

        Assert.NotNull(set.TryGetSpriteTexture("POSSA1"));
        Assert.Empty(set.Warnings);
        set.GetFlatTexture("MYFLAT");
        Assert.DoesNotContain(set.Warnings, w => w.Contains("MYFLAT"));
    }

    [Fact]
    public void GetWallTextureNames_ReturnsEveryDefinedWallTexture()
    {
        var entries = new[] { new TextureEntryDef("MYWALL", 1, 8, new[] { new TexturePatchDef(0, 0, 0) }) };
        var wad = BuildWad(("TEXTURE2", TextureDefinitions(entries)));
        var set = TextureSet.Load(wad);

        Assert.Equal(new[] { "MYWALL" }, set.GetWallTextureNames());
    }

    [Fact]
    public void GetWallTexture_NameOnlyInPk3TexturesFolder_DecodesViaModernImageFallback()
    {
        var pk3 = Pk3TestBuilder.Build(("textures/MODERNWALL.png", PngBytes()));
        var set = TextureSet.Load(pk3);

        var image = set.GetWallTexture("MODERNWALL");

        Assert.Equal(1, image.Width);
        Assert.Contains("MODERNWALL", set.GetWallTextureNames());
        Assert.DoesNotContain(set.Warnings, w => w.Contains("MODERNWALL"));
    }

    [Fact]
    public void GetWallTexture_NameInBothTexture1AndFolderImage_Texture1DefinitionWins()
    {
        var patch = Patch(height: 8, new (byte, byte[])[] { (0, new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 }) });
        var entries = new[] { new TextureEntryDef("SAMENAME", 1, 8, new[] { new TexturePatchDef(0, 0, 0) }) };
        var wad = BuildWad(
            ("PLAYPAL", Playpal((9, 9, 9))),
            ("PNAMES", PatchNames("MYPATCH")),
            ("TEXTURE2", TextureDefinitions(entries)),
            ("MYPATCH", patch));
        var pk3 = Pk3TestBuilder.Build(("textures/SAMENAME.png", PngBytes()));
        var set = TextureSet.Load(new ResourceSet(new IResourceContainer[] { pk3, wad }));

        var image = set.GetWallTexture("SAMENAME");

        // The composited TEXTURE1/2 definition is 1x8 (per GetWallTexture_KnownComposite_BuildsFromPatches);
        // the folder PNG is 1x1 - confirms the classic definition resolved, not the folder image.
        Assert.Equal(8, image.Height);
    }

    [Fact]
    public void GetWallTextureNames_ForOneResource_ScopedToThatResourceOwnFolderImagesPlusClassicNamesIfItIsTheSource()
    {
        var entries = new[] { new TextureEntryDef("CLASSIC1", 1, 8, new[] { new TexturePatchDef(0, 0, 0) }) };
        var wad = BuildWad(("TEXTURE2", TextureDefinitions(entries)));
        var pk3 = Pk3TestBuilder.Build(("textures/PK3WALL.png", PngBytes()));
        var resources = new ResourceSet(new IResourceContainer[] { pk3, wad });
        var set = TextureSet.Load(resources);

        Assert.Equal(new[] { "CLASSIC1" }, set.GetWallTextureNames(wad));
        Assert.Equal(new[] { "PK3WALL" }, set.GetWallTextureNames(pk3));
    }

    [Fact]
    public void WallTextureSource_ReturnsTheContainerThatDefinesTexture1()
    {
        var entries = new[] { new TextureEntryDef("MYWALL", 1, 8, Array.Empty<TexturePatchDef>()) };
        var wadWithTexture1 = BuildWad(("TEXTURE1", TextureDefinitions(entries)));
        var wadWithout = BuildWad(("SOMETHINGELSE", Array.Empty<byte>()));
        var resources = new ResourceSet(new IResourceContainer[] { wadWithout, wadWithTexture1 });
        var set = TextureSet.Load(resources);

        Assert.Same(wadWithTexture1, set.WallTextureSource);
    }

    [Fact]
    public void WallTextureSource_NullWhenNeitherTexture1NorTexture2Defined()
    {
        var wad = BuildWad(("SOMETHINGELSE", Array.Empty<byte>()));
        var set = TextureSet.Load(wad);

        Assert.Null(set.WallTextureSource);
    }

    [Fact]
    public void GetFlatTexture_PngFormatFlatFromPk3_DecodesViaModernImageFallback()
    {
        var pk3 = Pk3TestBuilder.Build(("flats/MODERNFLAT.png", PngBytes()));
        var set = TextureSet.Load(pk3);

        var image = set.GetFlatTexture("MODERNFLAT");

        Assert.Equal(1, image.Width);
        Assert.DoesNotContain(set.Warnings, w => w.Contains("MODERNFLAT"));
    }

    [Fact]
    public void GetFlatNames_MergesAcrossLayeredResources_Deduplicated()
    {
        var lower = Pk3TestBuilder.Build(("flats/SHARED.png", PngBytes()), ("flats/ONLYLOWER.png", PngBytes()));
        var higher = Pk3TestBuilder.Build(("flats/SHARED.png", PngBytes()), ("flats/ONLYHIGHER.png", PngBytes()));
        var set = TextureSet.Load(new ResourceSet(new IResourceContainer[] { lower, higher }));

        var names = set.GetFlatNames();

        Assert.Equal(new[] { "SHARED", "ONLYLOWER", "ONLYHIGHER" }.OrderBy(n => n), names.OrderBy(n => n));
    }

    [Fact]
    public void GetFlatNames_ForOneResource_ScopedToThatResourceOnly()
    {
        var wad = Pk3TestBuilder.Build(("flats/WADFLAT.png", PngBytes()));
        var pk3 = Pk3TestBuilder.Build(("flats/PK3FLAT.png", PngBytes()));
        var set = TextureSet.Load(new ResourceSet(new IResourceContainer[] { wad, pk3 }));

        Assert.Equal(new[] { "WADFLAT" }, set.GetFlatNames(wad));
        Assert.Equal(new[] { "PK3FLAT" }, set.GetFlatNames(pk3));
    }
}
