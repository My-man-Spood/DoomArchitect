namespace DoomArchitect.Core.Configuration;

/// <summary>
/// One WAD's real UDB-equivalent <c>.dbs</c> sidecar content - verified
/// directly against UDB's own <c>Source/Core/Map/MapOptions.cs</c>
/// (both the read constructor and <c>WriteConfiguration</c>), not
/// guessed. Two real, easy-to-miss shape details carried over exactly:
/// <c>gameconfig</c> is a single top-level field shared by every map in
/// the WAD's <c>.dbs</c> (not per-map, even though a WAD can hold several
/// maps), while <c>resources</c> genuinely is nested per map header name
/// (<c>maps.&lt;name&gt;.resources</c>), since different maps in one WAD
/// can reasonably want different extra resources. Only these two fields
/// are modeled - every other real UDB <c>.dbs</c> field (script document
/// state, tag labels, sector-drawing overrides, etc.) is preserved-but-
/// uninterpreted on a load-then-save round-trip (see
/// <see cref="CfgBlock.WithAssignment"/>/<see cref="CfgBlock.WithBlock"/>'s
/// own remarks) - the same "model what's used, preserve the rest"
/// principle already used for UDMF <c>CustomFields</c>.
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
}
