using System.Reflection;

namespace DoomArchitect.Core.Configuration;

/// <summary>
/// The three game configurations DoomArchitect bundles by default, loaded
/// once from embedded <c>.cfg</c> resources (see
/// <c>Configuration/GameConfigs/*.cfg</c>) and cached for the process
/// lifetime - these never change at runtime. Real UDB game-configuration
/// files (<c>Doom_DoomDoom.cfg</c>/<c>Doom_Doom2Doom.cfg</c>/
/// <c>GZDoom_DoomUDMF.cfg</c> and their own full <c>include()</c> chain,
/// unmodified apart from renaming the three top-level files), not a
/// hand-authored subset - see TODO.md for why this project didn't start
/// out this way (the GPLv3/MIT license mismatch that no longer applies).
/// A user-supplied external <c>.cfg</c> file is a separate, not-yet-built
/// entry point that would use the same <see cref="CfgLoader"/>/
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
        var fileName = kind switch
        {
            GameConfigurationKind.Doom => "Doom.cfg",
            GameConfigurationKind.Doom2 => "Doom2.cfg",
            GameConfigurationKind.GZDoomDoom2UDMF => "GZDoomDoom2UDMF.cfg",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var configuration = GameConfigurationLoader.Load(loader.Load(fileName));

        Cache[kind] = configuration;
        return configuration;
    }
}
