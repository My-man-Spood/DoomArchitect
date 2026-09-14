using System.Linq;
using DoomArchitect.Core.Configuration;

namespace DoomArchitect.Core.Tests.Configuration;

/// <summary>
/// Loads DoomArchitect's own real bundled <c>Doom.cfg</c>/<c>Doom2.cfg</c>
/// end to end (parse, resolve every <c>include()</c>, build the final
/// lookup tables) and spot-checks a handful of well-known entries - this
/// is as much a regression test for the embedded-resource wiring and the
/// authored data itself as it is for <see cref="GameConfigurationLoader"/>.
/// </summary>
public class GameConfigurationLoaderTests
{
    [Fact]
    public void Doom_PlayerStart_ResolvesWithSharedPlayerDefaults()
    {
        var doom = GameConfigurations.Get(GameConfigurationKind.Doom);

        var playerStart = doom.GetThingType(1);

        Assert.NotNull(playerStart);
        Assert.Equal("PLAYA2A8", playerStart!.SpriteName);
        Assert.Equal(16f, playerStart.Radius);
        Assert.Equal(56f, playerStart.Height);
    }

    [Fact]
    public void Doom_Zombieman_ResolvesWithACategoryOverrideOnTopOfDefaults()
    {
        var doom = GameConfigurations.Get(GameConfigurationKind.Doom);

        var zombieman = doom.GetThingType(3004);

        Assert.NotNull(zombieman);
        Assert.Equal("Zombieman", zombieman!.Title);
        Assert.Equal("POSSA2A8", zombieman.SpriteName);
        Assert.Equal(20f, zombieman.Radius);
        Assert.Equal(56f, zombieman.Height); // inherited from the "monsters" category default, not restated per entry
    }

    [Fact]
    public void Doom_HasNoDoom2ExclusiveMonster()
    {
        var doom = GameConfigurations.Get(GameConfigurationKind.Doom);

        Assert.Null(doom.GetThingType(64)); // Arch-vile
    }

    [Fact]
    public void Doom2_HasBothTheSharedRosterAndItsOwnExclusiveMonsters()
    {
        var doom2 = GameConfigurations.Get(GameConfigurationKind.Doom2);

        var zombieman = doom2.GetThingType(3004);
        var archvile = doom2.GetThingType(64);

        Assert.NotNull(zombieman);
        Assert.NotNull(archvile);
        Assert.Equal("Arch-vile", archvile!.Title);
        Assert.Equal("VILEA2D8", archvile.SpriteName);
    }

    [Fact]
    public void Doom2_SuperShotgun_InheritsWidthAndHeightFromTheSharedWeaponsCategory()
    {
        var doom2 = GameConfigurations.Get(GameConfigurationKind.Doom2);

        var superShotgun = doom2.GetThingType(82);

        Assert.NotNull(superShotgun);
        Assert.Equal(20f, superShotgun!.Radius);
        Assert.Equal(25f, superShotgun.Height); // weapons category default, corrected from an earlier wrong guess of 16

    }

    [Fact]
    public void Doom_EvilEye_DoesNotHang()
    {
        // Corrected against UDB's real data: despite the name, vanilla's
        // Evil Eye/Floating skull rock are not ceiling-relative - an
        // earlier, uncorrected guess had this backwards.
        var doom = GameConfigurations.Get(GameConfigurationKind.Doom);

        var evilEye = doom.GetThingType(41);

        Assert.NotNull(evilEye);
        Assert.False(evilEye!.Hangs);
    }

    [Fact]
    public void Doom_Zombieman_ShowsDirectionAndUsesTheMonsterColor()
    {
        var doom = GameConfigurations.Get(GameConfigurationKind.Doom);

        var zombieman = doom.GetThingType(3004);

        Assert.NotNull(zombieman);
        Assert.True(zombieman!.ShowsDirection);
        Assert.Equal(3, zombieman.ColorIndex);
    }

    [Fact]
    public void Doom_Stimpack_DoesNotShowDirectionAndUsesTheHealthColor()
    {
        var doom = GameConfigurations.Get(GameConfigurationKind.Doom);

        var stimpack = doom.GetThingType(2011);

        Assert.NotNull(stimpack);
        Assert.False(stimpack!.ShowsDirection);
        Assert.Equal(6, stimpack.ColorIndex);
    }

    [Fact]
    public void Doom_TeleportLanding_IsItsOwnCategoryAndShowsDirection()
    {
        // Matches UDB's real category split, cross-checked directly: a
        // teleport landing gets its own "teleports" category, not
        // "players" - an earlier version of this file had it wrong. It
        // shows direction since the player actually faces that way on
        // arrival.
        var doom = GameConfigurations.Get(GameConfigurationKind.Doom);

        var landing = doom.GetThingType(14);

        Assert.NotNull(landing);
        Assert.True(landing!.ShowsDirection);
        Assert.NotEqual(doom.GetThingType(1)!.ColorIndex, landing.ColorIndex);
    }

    [Fact]
    public void Doom_SoulSphere_IsAPowerupNotHealth()
    {
        // Matches UDB's real category split: soul sphere/invulnerability/
        // berserk/partial invisibility/radiation suit/computer area map/
        // light amp visor are "powerups", distinct from "health" (stimpack/
        // medikit/health+armor bonus/green+blue armor) - an earlier version
        // of this file lumped them all into "health".
        var doom = GameConfigurations.Get(GameConfigurationKind.Doom);

        var soulSphere = doom.GetThingType(2013);
        var stimpack = doom.GetThingType(2011);

        Assert.NotNull(soulSphere);
        Assert.NotNull(stimpack);
        Assert.NotEqual(stimpack!.ColorIndex, soulSphere!.ColorIndex);
    }

    [Fact]
    public void Doom2_Megasphere_IsAPowerupLikeDoomsSoulSphere()
    {
        var doom2 = GameConfigurations.Get(GameConfigurationKind.Doom2);

        var megasphere = doom2.GetThingType(83);
        var soulSphere = doom2.GetThingType(2013);

        Assert.NotNull(megasphere);
        Assert.NotNull(soulSphere);
        Assert.Equal(soulSphere!.ColorIndex, megasphere!.ColorIndex);
    }

    [Fact]
    public void Doom_Keys_AllShareOneCategoryColorRegardlessOfTheirOwnKeyColor()
    {
        // Deliberately NOT tinted by each key's own blue/yellow/red -
        // that's ambiguous at a glance against other categories that also
        // use blue-ish/yellow-ish tones. One shared "keys" color instead.
        var doom = GameConfigurations.Get(GameConfigurationKind.Doom);

        var blueKeycard = doom.GetThingType(5);
        var yellowKeycard = doom.GetThingType(6);
        var redKeycard = doom.GetThingType(13);

        Assert.NotNull(blueKeycard);
        Assert.NotNull(yellowKeycard);
        Assert.NotNull(redKeycard);
        Assert.False(blueKeycard!.ShowsDirection);
        Assert.Equal(blueKeycard.ColorIndex, yellowKeycard!.ColorIndex);
        Assert.Equal(blueKeycard.ColorIndex, redKeycard!.ColorIndex);
    }

    [Fact]
    public void Doom2_CommanderKeen_Hangs()
    {
        var doom2 = GameConfigurations.Get(GameConfigurationKind.Doom2);

        var keen = doom2.GetThingType(72);

        Assert.NotNull(keen);
        Assert.True(keen!.Hangs);
    }

    [Fact]
    public void Doom_UnknownThingType_ReturnsNull()
    {
        var doom = GameConfigurations.Get(GameConfigurationKind.Doom);

        Assert.Null(doom.GetThingType(999999));
    }

    [Fact]
    public void Doom_LinedefAction_ResolvesWithItsCategory()
    {
        var doom = GameConfigurations.Get(GameConfigurationKind.Doom);

        var action = doom.GetLinedefAction(1);

        Assert.NotNull(action);
        Assert.Equal("doors", action!.Category);
    }

    [Fact]
    public void Doom_SectorSpecial_ResolvesItsDescription()
    {
        var doom = GameConfigurations.Get(GameConfigurationKind.Doom);

        var special = doom.GetSectorSpecial(9);

        Assert.NotNull(special);
        Assert.Equal("Secret area", special!.Title);
    }

    [Fact]
    public void Doom_GetSectorSpecials_ReturnsAllSixteenVanillaEntriesSortedByNumber()
    {
        var doom = GameConfigurations.Get(GameConfigurationKind.Doom);

        var specials = doom.GetSectorSpecials();

        Assert.Equal(16, specials.Count);
        Assert.Equal(specials.OrderBy(s => s.Number).Select(s => s.Number), specials.Select(s => s.Number));
        Assert.Contains(specials, s => s.Number == 9 && s.Title == "Secret area");
    }

    [Fact]
    public void Doom2_LinedefTypesAndSectorTypes_AreSharedWithDoom()
    {
        var doom = GameConfigurations.Get(GameConfigurationKind.Doom);
        var doom2 = GameConfigurations.Get(GameConfigurationKind.Doom2);

        Assert.Equal(doom.GetSectorSpecial(9)!.Title, doom2.GetSectorSpecial(9)!.Title);
        Assert.Equal(doom.GetLinedefAction(1)!.Category, doom2.GetLinedefAction(1)!.Category);
    }

    [Fact]
    public void GZDoomDoom2UDMF_GetSectorFlags_ReturnsAllElevenRealUdmfFlags()
    {
        var gzdoom = GameConfigurations.Get(GameConfigurationKind.GZDoomDoom2UDMF);

        var flags = gzdoom.GetSectorFlags();

        Assert.Equal(11, flags.Count);
        Assert.Contains(flags, f => f.Key == "silent");
        Assert.Contains(flags, f => f.Key == "hurtmonsters"); // GZDoom-specific, not baseline UDMF
        Assert.Contains(flags, f => f.Key == "harminair"); // GZDoom-specific, not baseline UDMF
    }

    [Fact]
    public void Doom_GetSectorFlags_ReturnsNoneSinceVanillaConfigsDoNotDefineAny()
    {
        var doom = GameConfigurations.Get(GameConfigurationKind.Doom);

        Assert.Empty(doom.GetSectorFlags());
    }

    [Fact]
    public void GZDoomDoom2UDMF_GetDamageTypes_ReturnsAllTwentyRealGZDoomDamageTypes()
    {
        var gzdoom = GameConfigurations.Get(GameConfigurationKind.GZDoomDoom2UDMF);

        var damageTypes = gzdoom.GetDamageTypes();

        Assert.Equal(20, damageTypes.Count);
        Assert.Contains("Fire", damageTypes);
        Assert.Contains("InstantDeath", damageTypes);
    }

    [Fact]
    public void GZDoomDoom2UDMF_GetSectorSpecials_ReturnsTheLargerRealUdmfListNotVanillas()
    {
        var gzdoom = GameConfigurations.Get(GameConfigurationKind.GZDoomDoom2UDMF);

        var specials = gzdoom.GetSectorSpecials();

        Assert.Equal(94, specials.Count);
        Assert.Contains(specials, s => s.Number == 244);
        Assert.DoesNotContain(specials, s => s.Number == 10); // vanilla-only "Door close and stay" special, replaced wholesale
    }

    [Fact]
    public void GZDoomDoom2UDMF_ThingTypes_AreReusedFromDoom2()
    {
        var gzdoom = GameConfigurations.Get(GameConfigurationKind.GZDoomDoom2UDMF);
        var doom2 = GameConfigurations.Get(GameConfigurationKind.Doom2);

        var archvile = gzdoom.GetThingType(64);

        Assert.NotNull(archvile);
        Assert.Equal(doom2.GetThingType(64)!.Title, archvile!.Title);
    }

    [Fact]
    public void GZDoomDoom2UDMF_LinedefTypes_UseGenericHexenStyleActions()
    {
        var gzdoom = GameConfigurations.Get(GameConfigurationKind.GZDoomDoom2UDMF);

        var doorRaise = gzdoom.GetLinedefAction(12);

        Assert.NotNull(doorRaise);
        Assert.Equal("doors", doorRaise!.Category);
        Assert.Equal(5, doorRaise.Args.Count);
        Assert.True(doorRaise.Args[0].Used);
        Assert.Equal("Sector Tag", doorRaise.Args[0].Title);
        Assert.Null(doorRaise.Args[0].EnumOptions);
        Assert.True(doorRaise.Args[1].Used);
        Assert.NotNull(doorRaise.Args[1].EnumOptions);
        Assert.Contains(doorRaise.Args[1].EnumOptions!, o => o.Value == 16 && o.Title == "Slow");
        Assert.False(doorRaise.Args[4].Used);
    }

    [Fact]
    public void GZDoomDoom2UDMF_LinedefTypes_DoNotReuseVanillaClassicActionNumbers()
    {
        var gzdoom = GameConfigurations.Get(GameConfigurationKind.GZDoomDoom2UDMF);

        var actionOne = gzdoom.GetLinedefAction(1);

        Assert.Null(actionOne);
    }

    [Fact]
    public void GetLinedefActions_ReturnsAllActionsSortedByNumber()
    {
        var gzdoom = GameConfigurations.Get(GameConfigurationKind.GZDoomDoom2UDMF);

        var actions = gzdoom.GetLinedefActions();

        Assert.NotEmpty(actions);
        Assert.Equal(actions.OrderBy(a => a.Number).Select(a => a.Number), actions.Select(a => a.Number));
    }

    [Fact]
    public void GetLinedefFlags_ReturnsRealUdmfFlags()
    {
        var gzdoom = GameConfigurations.Get(GameConfigurationKind.GZDoomDoom2UDMF);

        var flags = gzdoom.GetLinedefFlags();

        Assert.Contains(flags, f => f.Key == "blocking");
        Assert.Contains(flags, f => f.Key == "twosided");
        Assert.Contains(flags, f => f.Key == "zoneboundary");
    }

    [Fact]
    public void GetLinedefActivations_ReturnsRealUdmfActivations()
    {
        var gzdoom = GameConfigurations.Get(GameConfigurationKind.GZDoomDoom2UDMF);

        var activations = gzdoom.GetLinedefActivations();

        Assert.Contains(activations, a => a.Key == "playercross");
        Assert.Contains(activations, a => a.Key == "repeatspecial");
    }
}
