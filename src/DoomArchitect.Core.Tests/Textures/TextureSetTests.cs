using DoomArchitect.Core.IO;
using DoomArchitect.Core.Tests.IO;
using DoomArchitect.Core.Textures;
using static DoomArchitect.Core.Tests.Textures.TextureLumpTestBuilder;

namespace DoomArchitect.Core.Tests.Textures;

public class TextureSetTests
{
    private static WadFile BuildWad(params (string Name, byte[] Data)[] lumps) =>
        WadFile.Read(new MemoryStream(WadTestBuilder.Build(lumps)));

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
}
