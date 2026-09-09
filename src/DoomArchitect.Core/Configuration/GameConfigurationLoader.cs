namespace DoomArchitect.Core.Configuration;

/// <summary>
/// Builds an <see cref="IGameConfiguration"/> from a fully
/// <c>include()</c>-resolved <see cref="CfgBlock"/> document. Doom2's own
/// bundled <c>Doom2.cfg</c> needs no special "fall back to Doom's table"
/// logic here at all - it <c>include()</c>s the shared base thing-type set
/// and layers its own exclusive monsters/items on top, and
/// <see cref="CfgLoader"/>'s merge already combined that into one
/// <c>thingtypes</c> block before this class ever sees it. Each numbered
/// entry inherits any field it doesn't set itself from its category's own
/// scalar defaults - matching UDB's real <c>thingtypes</c> inheritance
/// rule.
/// </summary>
public static class GameConfigurationLoader
{
    public static IGameConfiguration Load(CfgBlock document)
    {
        return new ParsedGameConfiguration(
            LoadThingTypes(document.FindBlock("thingtypes")),
            LoadLinedefActions(document.FindBlock("linedeftypes")),
            LoadSectorSpecials(document.FindBlock("sectortypes")));
    }

    private static Dictionary<int, ThingTypeInfo> LoadThingTypes(CfgBlock? thingTypes)
    {
        var result = new Dictionary<int, ThingTypeInfo>();
        if (thingTypes == null) return result;

        foreach (var category in thingTypes.Blocks)
        {
            foreach (var entry in category.Blocks)
            {
                if (!int.TryParse(entry.Key, out var doomEdNum)) continue;

                var title = entry.Find("title")?.AsString() ?? category.Find("title")?.AsString() ?? $"Thing {doomEdNum}";
                var sprite = entry.Find("sprite")?.AsString() ?? category.Find("sprite")?.AsString() ?? string.Empty;
                var width = entry.Find("width")?.AsFloat() ?? category.Find("width")?.AsFloat() ?? 20f;
                var height = entry.Find("height")?.AsFloat() ?? category.Find("height")?.AsFloat() ?? 16f;
                var hangs = entry.Find("hangs")?.AsBool() ?? category.Find("hangs")?.AsBool() ?? false;
                // Defaults (true / index 0) preserve this codebase's prior
                // behavior for any category that doesn't set these yet -
                // always a directional icon, a neutral color.
                var showsDirection = entry.Find("arrow")?.AsBool() ?? category.Find("arrow")?.AsBool() ?? true;
                var colorIndex = entry.Find("color")?.AsInt() ?? category.Find("color")?.AsInt() ?? 0;

                result[doomEdNum] = new ThingTypeInfo(doomEdNum, title, sprite, width, height, hangs, showsDirection, colorIndex);
            }
        }

        return result;
    }

    private static Dictionary<int, LinedefActionInfo> LoadLinedefActions(CfgBlock? linedefTypes)
    {
        var result = new Dictionary<int, LinedefActionInfo>();
        if (linedefTypes == null) return result;

        foreach (var category in linedefTypes.Blocks)
        {
            foreach (var entry in category.Blocks)
            {
                if (!int.TryParse(entry.Key, out var number)) continue;

                var title = entry.Find("title")?.AsString() ?? $"Action {number}";
                result[number] = new LinedefActionInfo(number, title, category.Key);
            }
        }

        return result;
    }

    /// <summary><c>sectortypes</c> is a flat <c>number = "description"</c> dictionary, not category-grouped like thing/linedef types - so this reads assignments, not nested blocks.</summary>
    private static Dictionary<int, SectorSpecialInfo> LoadSectorSpecials(CfgBlock? sectorTypes)
    {
        var result = new Dictionary<int, SectorSpecialInfo>();
        if (sectorTypes == null) return result;

        foreach (var assignment in sectorTypes.Assignments)
        {
            if (!int.TryParse(assignment.Key, out var number)) continue;
            result[number] = new SectorSpecialInfo(number, assignment.Value.AsString());
        }

        return result;
    }

    private sealed class ParsedGameConfiguration : IGameConfiguration
    {
        private readonly Dictionary<int, ThingTypeInfo> _thingTypes;
        private readonly Dictionary<int, LinedefActionInfo> _linedefActions;
        private readonly Dictionary<int, SectorSpecialInfo> _sectorSpecials;

        public ParsedGameConfiguration(
            Dictionary<int, ThingTypeInfo> thingTypes,
            Dictionary<int, LinedefActionInfo> linedefActions,
            Dictionary<int, SectorSpecialInfo> sectorSpecials)
        {
            _thingTypes = thingTypes;
            _linedefActions = linedefActions;
            _sectorSpecials = sectorSpecials;
        }

        public ThingTypeInfo? GetThingType(int doomEdNum) => _thingTypes.GetValueOrDefault(doomEdNum);

        public LinedefActionInfo? GetLinedefAction(int special) => _linedefActions.GetValueOrDefault(special);

        public SectorSpecialInfo? GetSectorSpecial(int type) => _sectorSpecials.GetValueOrDefault(type);
    }
}
