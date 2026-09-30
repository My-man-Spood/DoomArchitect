using System.Globalization;

namespace DoomArchitect.Core.ZDoom;

/// <summary>
/// A parsed DECORATE or ZScript actor definition - the shared base both
/// <c>ZScriptActorStructure</c> and <c>DecorateActorStructure</c> (Phase 2,
/// not yet ported) extend, ported from UDB's real <c>ActorStructure</c>.
/// Two real, tracked scope cuts from the original: no <c>uservars</c>/
/// <c>uservar_defaults</c> tracking (custom `user_*` ZScript field editing -
/// this project has no Thing property-editing UI to attach it to, same
/// reasoning as <see cref="ActorArgumentInfo"/>'s own reduced shape), and
/// <see cref="CheckActorSupported"/> takes the game's `decorategames`
/// value as a parameter instead of reading it from an ambient singleton
/// (this project doesn't have UDB's <c>General.Map</c> global).
/// </summary>
public class ActorStructure
{
    private static readonly string[] SpriteCheckStates = { "idle", "see", "inactive", "spawn" };
    internal const string ActorClassSpecialTokens = ":{}\n;,";

    // These are `internal set` in UDB's own source - `public set` here only
    // because this project has no InternalsVisibleTo for its test project
    // (same reasoning applied throughout this port, e.g.
    // TestLaunchCommandBuilder.SplitDigitRuns).
    public string ClassName { get; set; } = string.Empty;
    public string InheritsClass { get; set; } = "actor";
    public string? ReplacesClass { get; set; }
    public int DoomEdNum { get; set; } = -1;

    public ActorStructure? BaseClass { get; set; }
    public bool SkipSuper;

    public Dictionary<string, bool> Flags { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<string>> Properties { get; } = new(StringComparer.OrdinalIgnoreCase) { ["game"] = new() };

    public DecorateCategoryInfo? CategoryInfo;

    private readonly ActorArgumentInfo?[] _args = new ActorArgumentInfo?[5];

    public Dictionary<string, StateStructure> States { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool HasProperty(string propname)
    {
        if (Properties.ContainsKey(propname)) return true;
        return !SkipSuper && BaseClass != null && BaseClass.HasProperty(propname);
    }

    public bool HasPropertyWithValue(string propname)
    {
        if (Properties.TryGetValue(propname, out var values) && values.Count > 0) return true;
        return !SkipSuper && BaseClass != null && BaseClass.HasPropertyWithValue(propname);
    }

    /// <summary>Every value of a property joined by a space. Empty string when the property has no values.</summary>
    public string GetPropertyAllValues(string propname)
    {
        if (Properties.TryGetValue(propname, out var values) && values.Count > 0) return string.Join(" ", values);
        return !SkipSuper && BaseClass != null ? BaseClass.GetPropertyAllValues(propname) : "";
    }

    public string GetPropertyValueString(string propname, int valueindex, bool stripquotes = true)
    {
        if (Properties.TryGetValue(propname, out var values) && values.Count > valueindex)
            return stripquotes ? ZDTextParser.StripQuotes(values[valueindex]) : values[valueindex];
        return !SkipSuper && BaseClass != null ? BaseClass.GetPropertyValueString(propname, valueindex, stripquotes) : "";
    }

    public int GetPropertyValueInt(string propname, int valueindex)
    {
        var str = GetPropertyValueString(propname, valueindex, false);
        if (str == "-" && Properties.Count > valueindex + 1) str += GetPropertyValueString(propname, valueindex + 1, false); // it can be negative

        return int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    public float GetPropertyValueFloat(string propname, int valueindex)
    {
        var str = GetPropertyValueString(propname, valueindex, false);
        if (str == "-" && Properties.Count > valueindex + 1) str += GetPropertyValueString(propname, valueindex + 1, false);

        return float.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0f;
    }

    public bool HasFlagValue(string flag)
    {
        if (Flags.ContainsKey(flag)) return true;
        return !SkipSuper && BaseClass != null && BaseClass.HasFlagValue(flag);
    }

    public bool GetFlagValue(string flag, bool defaultvalue)
    {
        if (Flags.TryGetValue(flag, out var value)) return value;
        return !SkipSuper && BaseClass != null ? BaseClass.GetFlagValue(flag, defaultvalue) : defaultvalue;
    }

    /// <summary>
    /// True if this class (or, unless <see cref="SkipSuper"/>, its base
    /// class chain) defines <paramref name="statename"/>. The bare `Actor`
    /// root class is special-cased to answer only for `Spawn` - otherwise
    /// every actor in existence would inherit `Actor`'s own generic states,
    /// crowding out a more specific fallback sprite lookup (matches UDB's
    /// own real comment/behavior here exactly).
    /// </summary>
    public bool HasState(string statename)
    {
        if (ClassName.Equals("actor", StringComparison.OrdinalIgnoreCase) && !statename.Equals("spawn", StringComparison.OrdinalIgnoreCase))
            return false;

        if (States.ContainsKey(statename)) return true;
        return !SkipSuper && BaseClass != null && BaseClass.HasState(statename);
    }

    public StateStructure? GetState(string statename)
    {
        if (ClassName.Equals("actor", StringComparison.OrdinalIgnoreCase) && !statename.Equals("spawn", StringComparison.OrdinalIgnoreCase))
            return null;

        if (States.TryGetValue(statename, out var state)) return state;
        return !SkipSuper && BaseClass != null ? BaseClass.GetState(statename) : null;
    }

    public Dictionary<string, StateStructure> GetAllStates()
    {
        if (ClassName.Equals("actor", StringComparison.OrdinalIgnoreCase))
        {
            var actorOnly = new Dictionary<string, StateStructure>(StringComparer.OrdinalIgnoreCase);
            if (States.TryGetValue("spawn", out var spawn)) actorOnly["spawn"] = spawn;
            return actorOnly;
        }

        var result = new Dictionary<string, StateStructure>(States, StringComparer.OrdinalIgnoreCase);
        if (!SkipSuper && BaseClass != null)
        {
            foreach (var (name, state) in BaseClass.GetAllStates())
            {
                result.TryAdd(name, state);
            }
        }

        return result;
    }

    /// <summary>Whether this actor applies to a game whose `.cfg` declares <paramref name="decorateGames"/> - an actor with no `$game` property always applies.</summary>
    public bool CheckActorSupported(string decorateGames)
    {
        var includeGames = decorateGames.ToLowerInvariant();
        var games = Properties["game"];
        var includeActor = games.Count == 0;
        foreach (var g in games) includeActor |= includeGames.Contains(g);
        return includeActor;
    }

    /// <summary>
    /// The best sprite to represent this actor with (Thing browser icon,
    /// 3D billboard). <paramref name="lookupActor"/> resolves a state's
    /// cross-actor `goto` - see <see cref="StateStructure.GetSprite"/>'s own
    /// remark on why Phase 2 can leave it null. Unlike UDB's own real
    /// version, this doesn't verify the resolved sprite name actually
    /// exists in any loaded resource - this project's existing sprite
    /// lookup (<c>TextureSet</c>/<c>SpriteIconCache</c>) already degrades
    /// to a placeholder gracefully for any missing sprite, static `.cfg`
    /// things included, so a second check here would be redundant.
    /// </summary>
    public StateStructure.FrameInfo? FindSuitableSprite(Func<string, ActorStructure?>? lookupActor = null)
    {
        if (HasPropertyWithValue("$sprite"))
        {
            var sprite = GetPropertyValueString("$sprite", 0);
            return new StateStructure.FrameInfo { Sprite = sprite };
        }

        StateStructure.FrameInfo? firstNonTnt = null;
        StateStructure.FrameInfo? first = null;
        foreach (var state in GetAllStates().Values)
        {
            var info = state.GetSprite(0, lookupActor);
            if (string.IsNullOrEmpty(info.Sprite)) continue;

            if (!info.IsEmpty()) firstNonTnt = info;
            first ??= info;
            if (firstNonTnt != null) break;
        }

        StateStructure.FrameInfo? lastNonTnt = null;
        StateStructure.FrameInfo? last = null;
        foreach (var stateName in SpriteCheckStates)
        {
            if (!HasState(stateName)) continue;

            var info = GetState(stateName)!.GetSprite(0, lookupActor);
            if (string.IsNullOrEmpty(info.Sprite)) continue;

            if (!info.IsEmpty()) lastNonTnt = info;
            last ??= info;
            if (lastNonTnt != null) break;
        }

        // Priority order, preferring a non-TNT1 (non-empty) frame.
        return lastNonTnt ?? last ?? firstNonTnt ?? first;
    }

    /// <summary>Reads `$arg0`-`$arg4` into <see cref="GetArgumentInfo"/> - a reduced shape of UDB's real per-argument metadata, see <see cref="ActorArgumentInfo"/>.</summary>
    public void ParseCustomArguments()
    {
        for (var i = 0; i < 5; i++)
        {
            if (HasPropertyWithValue($"$arg{i}"))
            {
                var title = ZDTextParser.StripQuotes(GetPropertyAllValues($"$arg{i}"));
                _args[i] = new ActorArgumentInfo(true, title);
            }
            else
            {
                _args[i] = null;
            }
        }
    }

    public ActorArgumentInfo? GetArgumentInfo(int idx)
    {
        if (_args[idx] != null) return _args[idx];
        if (Properties.ContainsKey("$clearargs")) return null; // don't inherit anything past an explicit $clearargs
        return BaseClass?.GetArgumentInfo(idx);
    }
}
