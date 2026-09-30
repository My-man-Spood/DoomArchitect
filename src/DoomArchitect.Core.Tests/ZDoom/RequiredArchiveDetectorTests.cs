using System.Text;
using DoomArchitect.Core.Configuration;
using DoomArchitect.Core.Tests.IO;
using DoomArchitect.Core.ZDoom;

namespace DoomArchitect.Core.Tests.ZDoom;

public class RequiredArchiveDetectorTests
{
    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    // Mirrors the real bundled GZDoom_common.cfg entry exactly.
    private static readonly RequiredArchive GzdoomPk3 = new(
        "gzdoom", "gzdoom.pk3", true,
        new[] { new RequiredArchiveEntry("actor", null), new RequiredArchiveEntry(null, "x11r6rgb.txt") });

    [Fact]
    public void Matches_RealGzdoomPk3Shape_MatchesOnClassAndLump()
    {
        var pk3 = Pk3TestBuilder.Build(
            ("zscript.txt", Ascii("""
                #include "zscript/actors/actor.zs"
                """)),
            ("zscript/actors/actor.zs", Ascii("class Actor : Thinker native {}")),
            ("x11r6rgb.txt", Ascii("255 0 0 red")));

        Assert.True(RequiredArchiveDetector.Matches(GzdoomPk3, pk3));
    }

    [Fact]
    public void Matches_MissingLump_DoesNotMatchEvenIfClassIsDefined()
    {
        var pk3 = Pk3TestBuilder.Build(("zscript.txt", Ascii("class Actor : Thinker native {}")));

        Assert.False(RequiredArchiveDetector.Matches(GzdoomPk3, pk3));
    }

    [Fact]
    public void Matches_MissingClass_DoesNotMatchEvenIfLumpIsPresent()
    {
        var pk3 = Pk3TestBuilder.Build(("x11r6rgb.txt", Ascii("255 0 0 red")));

        Assert.False(RequiredArchiveDetector.Matches(GzdoomPk3, pk3));
    }

    [Fact]
    public void Matches_AnOrdinaryModPk3WithOnlyItsOwnActors_DoesNotFalselyMatchGzdoomsRealFingerprint()
    {
        // A regular mod pk3 with its own custom actor(s) but no x11r6rgb.txt
        // and no root "Actor" declaration of its own should never be
        // mistaken for gzdoom.pk3, even though it has real ZScript content too.
        var pk3 = Pk3TestBuilder.Build(("zscript.txt", Ascii("class MyMonster : Actor {}")));

        Assert.False(RequiredArchiveDetector.Matches(GzdoomPk3, pk3));
    }

    [Fact]
    public void Matches_ClassDefinedInDecorateInstead_StillMatches()
    {
        var fingerprint = new RequiredArchive("id", "file.pk3", true, new[] { new RequiredArchiveEntry("mymonster", null) });
        var pk3 = Pk3TestBuilder.Build(("decorate.txt", Ascii("actor MyMonster 5000 {}")));

        Assert.True(RequiredArchiveDetector.Matches(fingerprint, pk3));
    }

    [Fact]
    public void Matches_UnrelatedClassFailsFullInheritanceResolution_DoesNotPreventTheFingerprintFromMatching()
    {
        // The fingerprint check reads ZScriptParser.DeclaredClassNames
        // directly (no CompleteParsing needed) specifically so a totally
        // unrelated class's own broken inheritance elsewhere in the same
        // file can't cause a false negative here.
        var pk3 = Pk3TestBuilder.Build(("zscript.txt", Ascii("""
            class Actor : Thinker native {}
            class Broken : DoesNotExistAnywhere {}
            """)));

        var fingerprint = new RequiredArchive("id", "file.pk3", true, new[] { new RequiredArchiveEntry("actor", null) });

        Assert.True(RequiredArchiveDetector.Matches(fingerprint, pk3));
    }

    [Fact]
    public void Matches_LumpNameWithExtension_MatchesRootEntryIgnoringItsOwnExtension()
    {
        var pk3 = Pk3TestBuilder.Build(("x11r6rgb.txt", Ascii("data")));
        var fingerprint = new RequiredArchive("id", "file.pk3", true, new[] { new RequiredArchiveEntry(null, "x11r6rgb.txt") });

        Assert.True(RequiredArchiveDetector.Matches(fingerprint, pk3));
    }
}
