using DoomArchitect.Core.Configuration;

namespace DoomArchitect.Core.ZDoom;

/// <summary>
/// Wraps a static, `.cfg`-driven <see cref="IGameConfiguration"/> and
/// layers in whatever <see cref="DecorateParser"/>/<see cref="ZScriptParser"/>
/// discovered from a map's own loaded resources - the real reason
/// <see cref="IGameConfiguration"/> is an interface, not a concrete class
/// (see the Game configuration system's own original doc comment). Built
/// fresh via <see cref="Load"/> exactly like <c>TextureSet.Load</c> -
/// rebuilt wholesale every time resources change, never updated
/// incrementally, matching this project's one established pattern for
/// every other resource-derived structure. Only Thing types differ from
/// the wrapped configuration; everything else (actions, sector specials,
/// flags, skills, Test Map settings) delegates straight through unchanged.
/// </summary>
public sealed class DiscoveredActorGameConfiguration : IGameConfiguration
{
    private readonly IGameConfiguration _base;
    private readonly IReadOnlyDictionary<int, ThingTypeInfo> _thingTypes;

    private DiscoveredActorGameConfiguration(IGameConfiguration baseConfiguration, IReadOnlyDictionary<int, ThingTypeInfo> thingTypes)
    {
        _base = baseConfiguration;
        _thingTypes = thingTypes;
    }

    public static IGameConfiguration Load(IGameConfiguration baseConfiguration, DecorateParser decorate, ZScriptParser zscript, IReadOnlyDictionary<int, string>? mapinfoDoomEdNums = null)
    {
        var staticThingTypes = baseConfiguration.GetThingTypes().ToDictionary(t => t.DoomEdNum);
        var merged = DiscoveredActorThingTypeMerge.Merge(staticThingTypes, decorate, zscript, mapinfoDoomEdNums);
        return new DiscoveredActorGameConfiguration(baseConfiguration, merged);
    }

    public ThingTypeInfo? GetThingType(int doomEdNum) => _thingTypes.GetValueOrDefault(doomEdNum);

    public IReadOnlyList<ThingTypeInfo> GetThingTypes() => _thingTypes.Values.OrderBy(t => t.DoomEdNum).ToList();

    public ActionInfo? GetAction(int special) => _base.GetAction(special);
    public IReadOnlyList<ActionInfo> GetActions() => _base.GetActions();
    public SectorSpecialInfo? GetSectorSpecial(int type) => _base.GetSectorSpecial(type);
    public IReadOnlyList<SectorSpecialInfo> GetSectorSpecials() => _base.GetSectorSpecials();
    public IReadOnlyList<SectorFlagInfo> GetSectorFlags() => _base.GetSectorFlags();
    public IReadOnlyList<SectorFlagInfo> GetLinedefFlags() => _base.GetLinedefFlags();
    public IReadOnlyList<SectorFlagInfo> GetLinedefActivations() => _base.GetLinedefActivations();
    public IReadOnlyList<SectorFlagInfo> GetThingFlags() => _base.GetThingFlags();
    public IReadOnlyList<string> GetDamageTypes() => _base.GetDamageTypes();
    public bool MixTexturesAndFlats => _base.MixTexturesAndFlats;
    public IReadOnlyList<SkillInfo> GetSkills() => _base.GetSkills();
    public string TestParameters => _base.TestParameters;
    public bool TestShortPaths => _base.TestShortPaths;
    public string DecorateGames => _base.DecorateGames;
    public IReadOnlyList<RequiredArchive> GetRequiredArchives() => _base.GetRequiredArchives();
}
