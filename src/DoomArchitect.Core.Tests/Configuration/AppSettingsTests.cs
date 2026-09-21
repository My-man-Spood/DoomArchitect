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
