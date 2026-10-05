using DoomArchitect.Core.Configuration;

namespace DoomArchitect.Core.Tests.Configuration;

public class MapSettingsTests
{
    [Fact]
    public void GetGameConfiguration_NothingSet_ReturnsNull()
    {
        Assert.Null(MapSettings.Empty().GetGameConfiguration());
    }

    [Fact]
    public void WithMapSettings_ThenGet_RoundTripsGameConfigurationAndResources()
    {
        var settings = MapSettings.Empty()
            .WithMapSettings("MAP01", GameConfigurationKind.Doom2, new[] { "/iwads/doom2.wad" });

        Assert.Equal(GameConfigurationKind.Doom2, settings.GetGameConfiguration());
        Assert.Equal(new[] { "/iwads/doom2.wad" }, settings.GetResources("MAP01"));
    }

    [Fact]
    public void WithMapSettings_GameConfigIsSharedAcrossMapsInTheSameFile()
    {
        // The .dbs format's "gameconfig" is one top-level field for the
        // whole file, not per map - only "resources" is nested per map
        // header name.
        var settings = MapSettings.Empty()
            .WithMapSettings("MAP01", GameConfigurationKind.Doom, new[] { "/a.wad" })
            .WithMapSettings("MAP02", GameConfigurationKind.Doom2, new[] { "/b.wad" });

        Assert.Equal(GameConfigurationKind.Doom2, settings.GetGameConfiguration());
        Assert.Equal(new[] { "/a.wad" }, settings.GetResources("MAP01"));
        Assert.Equal(new[] { "/b.wad" }, settings.GetResources("MAP02"));
    }

    [Fact]
    public void WithMapSettings_UpdatingOneMap_DoesNotDisturbAnotherMapsResources()
    {
        var settings = MapSettings.Empty()
            .WithMapSettings("MAP01", GameConfigurationKind.Doom, new[] { "/a.wad" })
            .WithMapSettings("MAP02", GameConfigurationKind.Doom, new[] { "/b.wad" });

        var updated = settings.WithMapSettings("MAP01", GameConfigurationKind.Doom, new[] { "/a2.wad" });

        Assert.Equal(new[] { "/a2.wad" }, updated.GetResources("MAP01"));
        Assert.Equal(new[] { "/b.wad" }, updated.GetResources("MAP02"));
    }

    [Fact]
    public void Parse_UnknownFieldsFromARealDbsShapedFile_ArePreservedOnRoundTrip()
    {
        var handWritten =
            "type = \"Doom Builder Map Settings Configuration\"; gameconfig = \"Doom2\"; " +
            "strictpatches = 0; maps.MAP01 { resources { resource0 = \"/a.wad\"; } scriptcompiler = \"acc\"; }";

        var settings = MapSettings.Parse(handWritten);
        var updated = settings.WithMapSettings("MAP01", GameConfigurationKind.Doom2, new[] { "/a.wad", "/extra.wad" });

        var reparsed = MapSettings.Parse(updated.ToText());
        Assert.Equal(new[] { "/a.wad", "/extra.wad" }, reparsed.GetResources("MAP01"));
        // The unmodeled "scriptcompiler" field survives the round-trip untouched.
        Assert.Contains("scriptcompiler", updated.ToText());
    }

    [Fact]
    public void GetFolderResources_NothingSet_ReturnsEmpty()
    {
        Assert.Empty(MapSettings.Empty().GetFolderResources());
    }

    [Fact]
    public void WithFolderSettings_ThenGet_RoundTripsGameConfigurationAndResources()
    {
        var settings = MapSettings.Empty()
            .WithFolderSettings(GameConfigurationKind.GZDoomDoom2UDMF, new[] { "/mods/mymod" });

        Assert.Equal(GameConfigurationKind.GZDoomDoom2UDMF, settings.GetGameConfiguration());
        Assert.Equal(new[] { "/mods/mymod" }, settings.GetFolderResources());
    }

    [Fact]
    public void FolderSettings_ToText_ThenParse_RoundTrips()
    {
        var settings = MapSettings.Empty()
            .WithFolderSettings(GameConfigurationKind.Doom2, new[] { "/mods/mymod", "/iwads/doom2.wad" });

        var reloaded = MapSettings.Parse(settings.ToText());

        Assert.Equal(GameConfigurationKind.Doom2, reloaded.GetGameConfiguration());
        Assert.Equal(new[] { "/mods/mymod", "/iwads/doom2.wad" }, reloaded.GetFolderResources());
    }

    /// <summary>Folder-scoped and per-map-name resources coexist in the same .dbs shape without clobbering each other - only "gameconfig" is ever genuinely shared between them, same as it already is across different maps today.</summary>
    [Fact]
    public void FolderResources_AndPerMapResources_DoNotInterfere()
    {
        var settings = MapSettings.Empty()
            .WithMapSettings("MAP01", GameConfigurationKind.Doom, new[] { "/a.wad" })
            .WithFolderSettings(GameConfigurationKind.Doom2, new[] { "/mods/mymod" });

        Assert.Equal(new[] { "/a.wad" }, settings.GetResources("MAP01"));
        Assert.Equal(new[] { "/mods/mymod" }, settings.GetFolderResources());
        Assert.Equal(GameConfigurationKind.Doom2, settings.GetGameConfiguration());
    }
}
