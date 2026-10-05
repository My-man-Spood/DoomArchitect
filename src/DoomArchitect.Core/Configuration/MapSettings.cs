namespace DoomArchitect.Core.Configuration;

/// <summary>
/// One mod's <c>.dbs</c>-shaped settings file - a real UDB sidecar
/// convention when it sits next to a true standalone WAD (the mod root
/// *is* that WAD - see <see cref="DoomArchitect.Core.IO.ModRootDetector"/>),
/// or this project's own extension of it when it sits next to a pk3-style
/// mod folder instead (no UDB precedent for that case at all). Two
/// easy-to-miss shape details carried over exactly from the real format:
/// <c>gameconfig</c> is a single top-level field shared by every map in
/// one <c>.dbs</c> (not per-map, even though a WAD can hold several
/// maps), while <c>resources</c> genuinely is nested per map header name
/// (<c>maps.&lt;name&gt;.resources</c>) for a true multi-map WAD, since
/// different maps in *that* WAD can reasonably want different extra
/// resources - <see cref="GetFolderResources"/>/<see cref="WithFolderSettings"/>
/// are this project's own addition for the folder-rooted case, where
/// that per-map flexibility doesn't apply at all (one shared list for
/// every map the folder holds). Only these fields are modeled - every
/// other <c>.dbs</c> field (script document state, tag labels, sector-
/// drawing overrides, etc.) is preserved-but-uninterpreted on a load-
/// then-save round-trip (see <see cref="CfgBlock.WithAssignment"/>/
/// <see cref="CfgBlock.WithBlock"/>'s own remarks) - the same "model
/// what's used, preserve the rest" principle already used for UDMF
/// <c>CustomFields</c>.
/// </summary>
public sealed class MapSettings
{
    private readonly CfgBlock _root;

    private MapSettings(CfgBlock root)
    {
        _root = root;
    }

    public static MapSettings Empty() => new(CfgBlock.Empty());

    public static MapSettings Parse(string text) => new(CfgLoader.Parse(text));

    public string ToText() =>
        CfgWriter.Write(_root.WithAssignment("type", CfgValue.OfString("DoomArchitect Map Settings")));

    public GameConfigurationKind? GetGameConfiguration()
    {
        var value = _root.Find("gameconfig")?.AsString();
        return value != null && Enum.TryParse<GameConfigurationKind>(value, out var kind) ? kind : null;
    }

    public IReadOnlyList<string> GetResources(string mapName) =>
        OrderedResourceList.Read(_root.FindBlock("maps")?.FindBlock(mapName)?.FindBlock("resources"));

    public MapSettings WithMapSettings(string mapName, GameConfigurationKind kind, IReadOnlyList<string> resources)
    {
        var maps = _root.FindBlock("maps") ?? CfgBlock.Empty("maps");
        var mapEntry = maps.FindBlock(mapName) ?? CfgBlock.Empty(mapName);
        var updatedEntry = mapEntry.WithBlock("resources", OrderedResourceList.Write(resources));
        var updatedMaps = maps.WithBlock(mapName, updatedEntry);

        return new MapSettings(_root
            .WithBlock("maps", updatedMaps)
            .WithAssignment("gameconfig", CfgValue.OfString(kind.ToString())));
    }

    /// <summary>
    /// The shared resource list for every map sourced from the same mod
    /// root, when that root is a folder rather than a single WAD -
    /// deliberately *not* nested per map name the way <see cref="GetResources"/>
    /// is, since there's no legitimate case for two maps from the same
    /// folder wanting different resources (unlike a true multi-map WAD,
    /// where the per-map nesting above is real, confirmed UDB behavior
    /// this project deliberately preserves). A folder-sourced mod has no
    /// UDB precedent to match at all - this is this project's own,
    /// genuinely new convention for it.
    /// </summary>
    public IReadOnlyList<string> GetFolderResources() =>
        OrderedResourceList.Read(_root.FindBlock("folderresources"));

    /// <summary>Folder-scoped counterpart of <see cref="WithMapSettings"/> - same shared <c>gameconfig</c> field (already whole-file-scoped either way), but <paramref name="resources"/> goes into the flat <see cref="GetFolderResources"/> list instead of one map's own nested entry.</summary>
    public MapSettings WithFolderSettings(GameConfigurationKind kind, IReadOnlyList<string> resources) =>
        new(_root
            .WithBlock("folderresources", OrderedResourceList.Write(resources))
            .WithAssignment("gameconfig", CfgValue.OfString(kind.ToString())));
}
