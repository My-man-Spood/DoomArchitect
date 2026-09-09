namespace DoomArchitect.Core.Configuration;

/// <summary>
/// DoomArchitect's own global, app-wide settings - not tied to any
/// specific map/WAD (see <see cref="MapSettings"/> for that). Currently
/// just a default resource list per game configuration, so a user only
/// has to point at their IWAD once rather than for every single map of
/// the same game - mirrors UDB's own real per-configuration default
/// resources (<c>ConfigurationInfo.Resources</c>), minus the settings
/// this project doesn't have an equivalent concept for yet (no UI theme/
/// keybind/plugin settings live here).
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
}
