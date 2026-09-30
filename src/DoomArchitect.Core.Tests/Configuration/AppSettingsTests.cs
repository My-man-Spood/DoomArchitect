using DoomArchitect.Core.Configuration;
using DoomArchitect.Core.Input;

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

    [Fact]
    public void GetTestEngines_NothingSet_ReturnsEmptyAndNoActiveIndex()
    {
        var settings = AppSettings.Empty();

        Assert.Empty(settings.GetTestEngines(GameConfigurationKind.Doom));
        Assert.Equal(-1, settings.GetActiveTestEngineIndex(GameConfigurationKind.Doom));
    }

    [Fact]
    public void WithTestEngines_ThenGet_RoundTripsInOrderWithActiveIndex()
    {
        var engines = new[]
        {
            new TestEngine("GZDoom", "/opt/gzdoom/gzdoom", UseCustomParameters: false, CustomParameters: ""),
            new TestEngine("GZDoom (software)", "/opt/gzdoom/gzdoom", UseCustomParameters: true, CustomParameters: "-nogl"),
        };
        var settings = AppSettings.Empty().WithTestEngines(GameConfigurationKind.GZDoomDoom2UDMF, engines, activeIndex: 1);

        Assert.Equal(engines, settings.GetTestEngines(GameConfigurationKind.GZDoomDoom2UDMF));
        Assert.Equal(1, settings.GetActiveTestEngineIndex(GameConfigurationKind.GZDoomDoom2UDMF));
    }

    [Fact]
    public void WithTestEngines_DifferentGameConfigs_DontOverwriteEachOther()
    {
        var doomEngine = new TestEngine("Chocolate Doom", "/usr/bin/chocolate-doom", false, "");
        var gzdoomEngine = new TestEngine("GZDoom", "/usr/bin/gzdoom", false, "");
        var settings = AppSettings.Empty()
            .WithTestEngines(GameConfigurationKind.Doom, new[] { doomEngine }, 0)
            .WithTestEngines(GameConfigurationKind.GZDoomDoom2UDMF, new[] { gzdoomEngine }, 0);

        Assert.Equal(new[] { doomEngine }, settings.GetTestEngines(GameConfigurationKind.Doom));
        Assert.Equal(new[] { gzdoomEngine }, settings.GetTestEngines(GameConfigurationKind.GZDoomDoom2UDMF));
    }

    /// <summary>An out-of-range stored active index (e.g. the active engine was since removed) clamps into range rather than crashing or returning garbage.</summary>
    [Fact]
    public void GetActiveTestEngineIndex_StoredIndexOutOfRange_ClampsToLastEngine()
    {
        var engines = new[]
        {
            new TestEngine("A", "/a", false, ""),
            new TestEngine("B", "/b", false, ""),
        };
        var settings = AppSettings.Empty().WithTestEngines(GameConfigurationKind.Doom, engines, activeIndex: 5);

        Assert.Equal(1, settings.GetActiveTestEngineIndex(GameConfigurationKind.Doom));
    }

    [Fact]
    public void TestEngines_ToText_ThenParse_RoundTrips()
    {
        var engines = new[]
        {
            new TestEngine("GZDoom", "/opt/gzdoom/gzdoom", UseCustomParameters: false, CustomParameters: ""),
            new TestEngine("Custom", "/opt/engine", UseCustomParameters: true, CustomParameters: "-file \"%AP\" \"%F\""),
        };
        var settings = AppSettings.Empty().WithTestEngines(GameConfigurationKind.GZDoomDoom2UDMF, engines, activeIndex: 1);

        var reloaded = AppSettings.Parse(settings.ToText());

        Assert.Equal(engines, reloaded.GetTestEngines(GameConfigurationKind.GZDoomDoom2UDMF));
        Assert.Equal(1, reloaded.GetActiveTestEngineIndex(GameConfigurationKind.GZDoomDoom2UDMF));
    }

    [Fact]
    public void GetKeyBindingOverrides_NothingSet_ReturnsEmpty()
    {
        var settings = AppSettings.Empty();

        Assert.Empty(settings.GetKeyBindingOverrides());
    }

    [Fact]
    public void WithKeyBindingOverride_ThenGet_RoundTrips()
    {
        var settings = AppSettings.Empty()
            .WithKeyBindingOverride("undo", new KeyBinding("Y", Ctrl: true));

        var overrides = settings.GetKeyBindingOverrides();

        Assert.Equal(new KeyBinding("Y", Ctrl: true), overrides["undo"]);
    }

    [Fact]
    public void WithKeyBindingOverride_DifferentActions_DontOverwriteEachOther()
    {
        var settings = AppSettings.Empty()
            .WithKeyBindingOverride("undo", new KeyBinding("Y", Ctrl: true))
            .WithKeyBindingOverride("redo", new KeyBinding("Z", Ctrl: true, Shift: true));

        var overrides = settings.GetKeyBindingOverrides();

        Assert.Equal(new KeyBinding("Y", Ctrl: true), overrides["undo"]);
        Assert.Equal(new KeyBinding("Z", Ctrl: true, Shift: true), overrides["redo"]);
    }

    [Fact]
    public void WithKeyBindingReset_RemovesTheOverride()
    {
        var settings = AppSettings.Empty()
            .WithKeyBindingOverride("undo", new KeyBinding("Y", Ctrl: true))
            .WithKeyBindingReset("undo");

        Assert.Empty(settings.GetKeyBindingOverrides());
    }

    [Fact]
    public void KeyBindingOverride_ToText_ThenParse_RoundTrips()
    {
        var settings = AppSettings.Empty()
            .WithKeyBindingOverride("texture_nudge_amount_grid_modifier", new KeyBinding("Alt"))
            .WithKeyBindingOverride("draw_cardinal_lock_modifier", new KeyBinding("Ctrl", Shift: true, Alt: true));

        var reloaded = AppSettings.Parse(settings.ToText());

        var overrides = reloaded.GetKeyBindingOverrides();
        Assert.Equal(new KeyBinding("Alt"), overrides["texture_nudge_amount_grid_modifier"]);
        Assert.Equal(new KeyBinding("Ctrl", Shift: true, Alt: true), overrides["draw_cardinal_lock_modifier"]);
    }
}
