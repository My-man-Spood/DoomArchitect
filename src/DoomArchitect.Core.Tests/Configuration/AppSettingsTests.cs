using DoomArchitect.Core.Configuration;

namespace DoomArchitect.Core.Tests.Configuration;

public class AppSettingsTests
{
    [Fact]
    public void GetDefaultResources_NothingSet_ReturnsEmpty()
    {
        var settings = AppSettings.Empty();

        Assert.Empty(settings.GetDefaultResources(GameConfigurationKind.Doom));
    }

    [Fact]
    public void WithDefaultResources_ThenGet_RoundTripsInOrder()
    {
        var settings = AppSettings.Empty()
            .WithDefaultResources(GameConfigurationKind.Doom2, new[] { "/iwads/doom2.wad" });

        Assert.Equal(new[] { "/iwads/doom2.wad" }, settings.GetDefaultResources(GameConfigurationKind.Doom2));
    }

    [Fact]
    public void WithDefaultResources_DifferentGameConfigs_DontOverwriteEachOther()
    {
        var settings = AppSettings.Empty()
            .WithDefaultResources(GameConfigurationKind.Doom, new[] { "/iwads/doom.wad" })
            .WithDefaultResources(GameConfigurationKind.Doom2, new[] { "/iwads/doom2.wad" });

        Assert.Equal(new[] { "/iwads/doom.wad" }, settings.GetDefaultResources(GameConfigurationKind.Doom));
        Assert.Equal(new[] { "/iwads/doom2.wad" }, settings.GetDefaultResources(GameConfigurationKind.Doom2));
    }

    [Fact]
    public void ToText_ThenParse_RoundTrips()
    {
        var settings = AppSettings.Empty()
            .WithDefaultResources(GameConfigurationKind.Doom, new[] { "/iwads/doom.wad", "/extra.wad" });

        var reloaded = AppSettings.Parse(settings.ToText());

        Assert.Equal(new[] { "/iwads/doom.wad", "/extra.wad" }, reloaded.GetDefaultResources(GameConfigurationKind.Doom));
    }
}
