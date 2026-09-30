using DoomArchitect.Core.Configuration;
using DoomArchitect.Core.IO;

namespace DoomArchitect.Core.ZDoom;

/// <summary>
/// The actual Phase 5 entry point: scans every loaded resource for its own
/// ZSCRIPT/DECORATE/MAPINFO content and returns a merged
/// <see cref="IGameConfiguration"/> - the one place that ties Phases 1-4
/// together into something <c>OpenMapMenu</c> can call. ZScript/DECORATE
/// content is genuinely cumulative across a real GZDoom/UDB resource stack
/// (every loaded PK3/WAD's own root entry contributes, unlike a texture
/// lookup's "highest priority wins" - real mod stacking relies on this), so
/// every layered <see cref="IResourceContainer"/> gets its own root entry
/// fed into one shared parser instance rather than only checking the
/// highest-priority resource. `#include` resolution stays scoped to
/// whichever single container a root entry came from, matching UDB's own
/// real `DataManager.LoadZScriptFromLocation` (a `#include` can't reach
/// into a completely different, separately-loaded archive).
/// </summary>
public static class ResourceActorScanner
{
    public static IGameConfiguration Scan(IGameConfiguration baseConfiguration, ResourceSet resources)
    {
        var zscript = new ZScriptParser { GameConfiguration = baseConfiguration };
        ScanEachContainer(resources, "ZSCRIPT", zscript.Parse, container => zscript.OnInclude = container.FindByPath);
        if (!zscript.HasError) zscript.CompleteParsing();

        var decorate = new DecorateParser { GameConfiguration = baseConfiguration, ZScriptActors = zscript.AllActorsByClass };
        ScanEachContainer(resources, "DECORATE", decorate.Parse, container => decorate.OnInclude = container.FindByPath);

        var mapinfo = new MapinfoParser();
        ScanEachContainer(resources, "MAPINFO", mapinfo.Parse, container => mapinfo.OnInclude = container.FindByPath);

        return DiscoveredActorGameConfiguration.Load(baseConfiguration, decorate, zscript, mapinfo.DoomEdNums);
    }

    /// <summary>Feeds every container's own root entry named <paramref name="rootLumpName"/> (if it has one) into <paramref name="parse"/>, in priority order - a parse failure stops feeding further containers into that parser but doesn't affect the other formats, matching this project's "a resource we can't fully parse just doesn't contribute" posture.</summary>
    private static void ScanEachContainer(ResourceSet resources, string rootLumpName, Func<byte[], string, bool> parse, Action<IResourceContainer> setIncludeResolver)
    {
        foreach (var container in resources.Containers)
        {
            var root = container.FindLump(rootLumpName);
            if (root == null) continue;

            setIncludeResolver(container);
            if (!parse(root.Data, rootLumpName)) break;
        }
    }
}
