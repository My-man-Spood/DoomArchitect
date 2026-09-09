using System.Reflection;

namespace DoomArchitect.Core.Configuration;

/// <summary>
/// The two game configurations DoomArchitect bundles by default, loaded
/// once from embedded <c>.cfg</c> resources (see
/// <c>Configuration/GameConfigs/*.cfg</c>) and cached for the process
/// lifetime - these never change at runtime. Authored fresh from public,
/// decades-established vanilla Doom knowledge rather than copied from
/// UDB's own actual <c>.cfg</c> files (UDB's are GPLv3; this repository is
/// MIT) - see TODO.md for the full reasoning. A user-supplied external
/// <c>.cfg</c> file (including a real UDB one) is a separate, not-yet-
/// built entry point that would use the same <see cref="CfgLoader"/>/
/// <see cref="GameConfigurationLoader"/> pipeline with a
/// <see cref="FileSystemCfgFileSource"/> instead of this class's embedded
/// one.
/// </summary>
public static class GameConfigurations
{
    private const string ResourceRootPrefix = "DoomArchitect.Core.Configuration.GameConfigs";

    private static readonly Dictionary<GameConfigurationKind, IGameConfiguration> Cache = new();

    public static IGameConfiguration Get(GameConfigurationKind kind)
    {
        if (Cache.TryGetValue(kind, out var cached)) return cached;

        var loader = new CfgLoader(new EmbeddedResourceCfgFileSource(Assembly.GetExecutingAssembly(), ResourceRootPrefix));
        var fileName = kind == GameConfigurationKind.Doom ? "Doom.cfg" : "Doom2.cfg";
        var configuration = GameConfigurationLoader.Load(loader.Load(fileName));

        Cache[kind] = configuration;
        return configuration;
    }
}
