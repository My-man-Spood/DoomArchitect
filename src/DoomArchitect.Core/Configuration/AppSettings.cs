using DoomArchitect.Core.Input;

namespace DoomArchitect.Core.Configuration;

/// <summary>
/// DoomArchitect's own global, app-wide settings - not tied to any
/// specific map/WAD (see <see cref="MapSettings"/> for that): a default
/// resource list per game configuration (so a user only has to point at
/// their IWAD once rather than for every single map of the same game),
/// that same per-game-configuration keying for a user's own named Test Map
/// engines (<see cref="TestEngineSettings"/>), and a user's own keybind
/// overrides (see <see cref="KeyBindingOverrides"/>) - minus the settings
/// this project doesn't have an equivalent concept for yet (no UI
/// theme/plugin settings live here).
/// </summary>
public sealed class AppSettings
{
    private readonly CfgBlock _root;

    private AppSettings(CfgBlock root)
    {
        _root = root;
    }

    public static AppSettings Empty() => new(CfgBlock.Empty());

    public static AppSettings Parse(string text) => new(CfgLoader.Parse(text));

    public string ToText() =>
        CfgWriter.Write(_root.WithAssignment("type", CfgValue.OfString("DoomArchitect App Settings")));

    public IReadOnlyList<string> GetDefaultResources(GameConfigurationKind kind) =>
        OrderedResourceList.Read(_root.FindBlock("gameconfigs")?.FindBlock(kind.ToString())?.FindBlock("resources"));

    public AppSettings WithDefaultResources(GameConfigurationKind kind, IReadOnlyList<string> paths)
    {
        var gameConfigs = _root.FindBlock("gameconfigs") ?? CfgBlock.Empty("gameconfigs");
        var entry = gameConfigs.FindBlock(kind.ToString()) ?? CfgBlock.Empty(kind.ToString());
        var updatedEntry = entry.WithBlock("resources", OrderedResourceList.Write(paths));
        var updatedGameConfigs = gameConfigs.WithBlock(kind.ToString(), updatedEntry);
        return new AppSettings(_root.WithBlock("gameconfigs", updatedGameConfigs));
    }

    public IReadOnlyList<TestEngine> GetTestEngines(GameConfigurationKind kind) =>
        TestEngineSettings.Read(_root.FindBlock("gameconfigs")?.FindBlock(kind.ToString())?.FindBlock("testengines"));

    /// <summary>-1 when <paramref name="kind"/> has no configured test engines at all - Test Map's own "nothing configured yet" signal.</summary>
    public int GetActiveTestEngineIndex(GameConfigurationKind kind)
    {
        var testEngines = _root.FindBlock("gameconfigs")?.FindBlock(kind.ToString())?.FindBlock("testengines");
        return TestEngineSettings.ReadActiveIndex(testEngines, GetTestEngines(kind).Count);
    }

    public AppSettings WithTestEngines(GameConfigurationKind kind, IReadOnlyList<TestEngine> engines, int activeIndex)
    {
        var gameConfigs = _root.FindBlock("gameconfigs") ?? CfgBlock.Empty("gameconfigs");
        var entry = gameConfigs.FindBlock(kind.ToString()) ?? CfgBlock.Empty(kind.ToString());
        var updatedEntry = entry.WithBlock("testengines", TestEngineSettings.Write(engines, activeIndex));
        var updatedGameConfigs = gameConfigs.WithBlock(kind.ToString(), updatedEntry);
        return new AppSettings(_root.WithBlock("gameconfigs", updatedGameConfigs));
    }

    /// <summary>Every action whose binding differs from <see cref="KeyBindingRegistry"/>'s own compiled-in default - an action with no entry here just uses that default.</summary>
    public IReadOnlyDictionary<string, KeyBinding> GetKeyBindingOverrides() =>
        KeyBindingOverrides.Read(_root.FindBlock("keybinds"));

    public AppSettings WithKeyBindingOverride(string action, KeyBinding binding)
    {
        var overrides = new Dictionary<string, KeyBinding>(GetKeyBindingOverrides()) { [action] = binding };
        return new AppSettings(_root.WithBlock("keybinds", KeyBindingOverrides.Write(overrides)));
    }

    /// <summary>Reverts a single action back to its compiled-in default by dropping its override, if any.</summary>
    public AppSettings WithKeyBindingReset(string action)
    {
        var overrides = new Dictionary<string, KeyBinding>(GetKeyBindingOverrides());
        if (!overrides.Remove(action)) return this;

        return new AppSettings(_root.WithBlock("keybinds", KeyBindingOverrides.Write(overrides)));
    }
}
