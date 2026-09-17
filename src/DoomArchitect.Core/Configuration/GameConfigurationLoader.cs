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
        var enums = LoadEnums(document.FindBlock("enums"));

        return new ParsedGameConfiguration(
            LoadThingTypes(document.FindBlock("thingtypes")),
            LoadLinedefActions(document.FindBlock("linedeftypes"), enums),
            LoadSectorSpecials(document.FindBlock("sectortypes")),
            LoadFlagInfoDictionary(document.FindBlock("sectorflags")),
            LoadFlagInfoDictionary(document.FindBlock("linedefflags")),
            LoadFlagInfoDictionary(document.FindBlock("linedefactivations")),
            LoadFlagInfoDictionary(document.FindBlock("thingflags")),
            LoadDamageTypes(document),
            document.Find("mixtexturesflats")?.AsBool() ?? false);
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

                result[doomEdNum] = new ThingTypeInfo(doomEdNum, title, sprite, width, height, hangs, showsDirection, colorIndex, category.Key);
            }
        }

        return result;
    }

    private static Dictionary<int, ActionInfo> LoadLinedefActions(CfgBlock? linedefTypes, Dictionary<string, List<ArgumentEnumOption>> enums)
    {
        var result = new Dictionary<int, ActionInfo>();
        if (linedefTypes == null) return result;

        foreach (var category in linedefTypes.Blocks)
        {
            foreach (var entry in category.Blocks)
            {
                if (!int.TryParse(entry.Key, out var number)) continue;

                var title = entry.Find("title")?.AsString() ?? $"Action {number}";
                result[number] = new ActionInfo(number, title, category.Key, LoadArguments(entry, enums));
            }
        }

        return result;
    }

    /// <summary>
    /// The fixed 5 <c>arg0</c>-<c>arg4</c> slots - a slot with no matching
    /// sub-block in the <c>.cfg</c> entry is <see cref="ArgumentInfo.Used"/>
    /// <c>false</c> with a generic placeholder title, matching UDB's real
    /// always-5-boxes-some-disabled layout rather than a variable-length list.
    /// </summary>
    private static IReadOnlyList<ArgumentInfo> LoadArguments(CfgBlock entry, Dictionary<string, List<ArgumentEnumOption>> enums)
    {
        var args = new List<ArgumentInfo>(5);

        for (var i = 0; i < 5; i++)
        {
            var argBlock = entry.FindBlock($"arg{i}");
            if (argBlock == null)
            {
                args.Add(new ArgumentInfo($"Argument {i + 1}", Used: false, EnumOptions: null));
                continue;
            }

            var title = argBlock.Find("title")?.AsString() ?? $"Argument {i + 1}";
            var enumName = argBlock.Find("enum")?.AsString();
            var enumOptions = enumName != null ? enums.GetValueOrDefault(enumName) : null;
            args.Add(new ArgumentInfo(title, Used: true, enumOptions));
        }

        return args;
    }

    /// <summary>The <c>enums { name { value = "label"; ... } ... }</c> block - a shared table an argument's own <c>enum</c> key references by name, so a speed/type enum reused by several actions is only authored once.</summary>
    private static Dictionary<string, List<ArgumentEnumOption>> LoadEnums(CfgBlock? enums)
    {
        var result = new Dictionary<string, List<ArgumentEnumOption>>();
        if (enums == null) return result;

        foreach (var enumBlock in enums.Blocks)
        {
            var options = new List<ArgumentEnumOption>();
            foreach (var assignment in enumBlock.Assignments)
            {
                if (long.TryParse(assignment.Key, out var value))
                {
                    options.Add(new ArgumentEnumOption(value, assignment.Value.AsString()));
                }
            }

            result[enumBlock.Key] = options;
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

    /// <summary>Flat <c>fieldname = "title"</c> dictionary shape shared by <c>sectorflags</c>, <c>linedefflags</c>, and <c>linedefactivations</c> - string keys (the literal UDMF field name) rather than parsed numbers.</summary>
    private static Dictionary<string, SectorFlagInfo> LoadFlagInfoDictionary(CfgBlock? block)
    {
        var result = new Dictionary<string, SectorFlagInfo>();
        if (block == null) return result;

        foreach (var assignment in block.Assignments)
        {
            result[assignment.Key] = new SectorFlagInfo(assignment.Key, assignment.Value.AsString());
        }

        return result;
    }

    /// <summary>A plain space-separated scalar setting at the document root (<c>damagetypes = "Fire Slime ...";</c>), not a block - matches how UDB's own real cfg stores this list.</summary>
    private static List<string> LoadDamageTypes(CfgBlock document)
    {
        var value = document.Find("damagetypes")?.AsString();
        if (string.IsNullOrWhiteSpace(value)) return new List<string>();

        return value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private sealed class ParsedGameConfiguration : IGameConfiguration
    {
        private readonly Dictionary<int, ThingTypeInfo> _thingTypes;
        private readonly Dictionary<int, ActionInfo> _linedefActions;
        private readonly Dictionary<int, SectorSpecialInfo> _sectorSpecials;
        private readonly Dictionary<string, SectorFlagInfo> _sectorFlags;
        private readonly Dictionary<string, SectorFlagInfo> _linedefFlags;
        private readonly Dictionary<string, SectorFlagInfo> _linedefActivations;
        private readonly Dictionary<string, SectorFlagInfo> _thingFlags;
        private readonly List<string> _damageTypes;
        private readonly bool _mixTexturesAndFlats;

        public ParsedGameConfiguration(
            Dictionary<int, ThingTypeInfo> thingTypes,
            Dictionary<int, ActionInfo> linedefActions,
            Dictionary<int, SectorSpecialInfo> sectorSpecials,
            Dictionary<string, SectorFlagInfo> sectorFlags,
            Dictionary<string, SectorFlagInfo> linedefFlags,
            Dictionary<string, SectorFlagInfo> linedefActivations,
            Dictionary<string, SectorFlagInfo> thingFlags,
            List<string> damageTypes,
            bool mixTexturesAndFlats)
        {
            _thingTypes = thingTypes;
            _linedefActions = linedefActions;
            _sectorSpecials = sectorSpecials;
            _sectorFlags = sectorFlags;
            _linedefFlags = linedefFlags;
            _linedefActivations = linedefActivations;
            _thingFlags = thingFlags;
            _damageTypes = damageTypes;
            _mixTexturesAndFlats = mixTexturesAndFlats;
        }

        public ThingTypeInfo? GetThingType(int doomEdNum) => _thingTypes.GetValueOrDefault(doomEdNum);

        public IReadOnlyList<ThingTypeInfo> GetThingTypes() => _thingTypes.Values.OrderBy(t => t.DoomEdNum).ToList();

        public ActionInfo? GetAction(int special) => _linedefActions.GetValueOrDefault(special);

        public IReadOnlyList<ActionInfo> GetActions() => _linedefActions.Values.OrderBy(a => a.Number).ToList();

        public SectorSpecialInfo? GetSectorSpecial(int type) => _sectorSpecials.GetValueOrDefault(type);

        public IReadOnlyList<SectorSpecialInfo> GetSectorSpecials() => _sectorSpecials.Values.OrderBy(s => s.Number).ToList();

        public IReadOnlyList<SectorFlagInfo> GetSectorFlags() => _sectorFlags.Values.ToList();

        public IReadOnlyList<SectorFlagInfo> GetLinedefFlags() => _linedefFlags.Values.ToList();

        public IReadOnlyList<SectorFlagInfo> GetLinedefActivations() => _linedefActivations.Values.ToList();

        public IReadOnlyList<SectorFlagInfo> GetThingFlags() => _thingFlags.Values.ToList();

        public IReadOnlyList<string> GetDamageTypes() => _damageTypes;

        public bool MixTexturesAndFlats => _mixTexturesAndFlats;
    }
}
