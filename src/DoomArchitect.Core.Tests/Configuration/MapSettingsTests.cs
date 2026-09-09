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
        // Matches UDB's own real .dbs shape exactly: "gameconfig" is one
        // top-level field for the whole file, not per map - only
        // "resources" is nested per map header name.
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
}
