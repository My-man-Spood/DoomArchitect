using DoomArchitect.Core.Configuration;

namespace DoomArchitect.Core.ZDoom;

/// <summary>
/// Merges DECORATE/ZScript-discovered actors into a static, `.cfg`-derived
/// `ThingTypeInfo` dictionary - a close port of UDB's real
/// <c>DataManager.ApplyZDoomThings</c>, mapped onto this project's own much
/// simpler <see cref="ThingTypeInfo"/> record (10 scalar fields, vs. UDB's
/// real one's rendering-property richness: alpha, renderstyle, per-cvar
/// distance checks, wallsprite/flatsprite/rollsprite, dynamic light type,
/// a 5-slot argument array). Every field this project's own
/// <see cref="ThingTypeInfo"/> actually has is extracted faithfully; the
/// rest has nothing to receive it and is dropped - tracked in TODO.md,
/// not silently lost, and consistent with this project's own established,
/// much simpler Thing-rendering scope.
///
/// Real UDB's own merge order and collision rules, preserved exactly:
/// DECORATE wins over ZScript on a classname collision (`ActorsByClass`
/// merge favors `decorate` first); "replaces" updates the replaced actor's
/// existing `ThingTypeInfo` *in place at its own DoomEdNum*, not by adding
/// a new entry; a real, positive `DoomEdNum` either updates an existing
/// entry at that number or creates a new one (inheriting defaults from the
/// static entry for `InheritsClass`, when one exists); MAPINFO
/// `DoomEdNums` overrides run last and can also delete an entry entirely
/// (`"none"`).
/// </summary>
public static class DiscoveredActorThingTypeMerge
{
    private const float ThingFixedSize = 14f; // UDB's own real THING_FIXED_SIZE - the minimum sane radius for anything that doesn't specify one

    public static IReadOnlyDictionary<int, ThingTypeInfo> Merge(
        IReadOnlyDictionary<int, ThingTypeInfo> staticThingTypes,
        DecorateParser decorate,
        ZScriptParser zscript,
        IReadOnlyDictionary<int, string>? mapinfoDoomEdNums = null)
    {
        var result = new Dictionary<int, ThingTypeInfo>(staticThingTypes);

        var byClassName = new Dictionary<string, ThingTypeInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in staticThingTypes.Values)
            if (!string.IsNullOrEmpty(t.ClassName)) byClassName[t.ClassName] = t;

        // DECORATE wins over ZScript on a classname collision, matching
        // UDB's own real merge order exactly (decorate.ActorsByClass first,
        // zscript only fills in names decorate doesn't already have).
        var mergedByClass = new Dictionary<string, ActorStructure>(decorate.ActorsByClass, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in zscript.ActorsByClass) mergedByClass.TryAdd(key, value);

        var mergedAllByClass = new Dictionary<string, ActorStructure>(decorate.AllActorsByClass, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in zscript.AllActorsByClass) mergedAllByClass.TryAdd(key, value);

        // Step 1: for every supported actor (ZScript first, then DECORATE - matches UDB's own zscript.Actors.Union(decorate.Actors) enumeration order).
        foreach (var actor in zscript.Actors.Concat(decorate.Actors))
        {
            if (!string.IsNullOrEmpty(actor.ReplacesClass) && byClassName.TryGetValue(actor.ReplacesClass, out var replaced))
            {
                result[replaced.DoomEdNum] = BuildThingTypeInfo(actor, replaced, replaced.DoomEdNum);
            }

            if (actor.DoomEdNum > 0)
            {
                if (result.TryGetValue(actor.DoomEdNum, out var existingAtSlot))
                {
                    result[actor.DoomEdNum] = BuildThingTypeInfo(actor, existingAtSlot, actor.DoomEdNum);
                }
                else
                {
                    var inheritedBase = byClassName.GetValueOrDefault(actor.InheritsClass);
                    result[actor.DoomEdNum] = BuildThingTypeInfo(actor, inheritedBase, actor.DoomEdNum);
                }
            }
        }

        // Step 2: MAPINFO DoomEdNums overrides - the only way a ZScript-only actor (DoomEdNum always -1 from parsing) gets a real editor number.
        if (mapinfoDoomEdNums != null)
        {
            foreach (var (id, className) in mapinfoDoomEdNums)
            {
                if (className == "none")
                {
                    result.Remove(id);
                    continue;
                }

                if (result.TryGetValue(id, out var existing) && existing.ClassName.Equals(className, StringComparison.OrdinalIgnoreCase))
                    continue; // already applied

                if (mergedByClass.TryGetValue(className, out var actor) || mergedAllByClass.TryGetValue(className, out actor))
                {
                    result[id] = BuildThingTypeInfo(actor, byClassName.GetValueOrDefault(actor.InheritsClass), id);
                }
                else if (byClassName.TryGetValue(className, out var staticBase))
                {
                    result[id] = staticBase with { DoomEdNum = id };
                }
                // Else: no matching actor found anywhere - real UDB logs a
                // warning here; no diagnostics surface exists yet to show
                // one (tracked in TODO.md), so this silently no-ops.
            }
        }

        return result;
    }

    private static ThingTypeInfo BuildThingTypeInfo(ActorStructure actor, ThingTypeInfo? baseInfo, int doomEdNum)
    {
        var title = ResolveTitle(actor, baseInfo);
        var sprite = actor.FindSuitableSprite()?.Sprite ?? baseInfo?.SpriteName ?? "";

        var radius = actor.HasPropertyWithValue("radius") ? actor.GetPropertyValueFloat("radius", 0) : baseInfo?.Radius ?? 0f;
        if (radius < 4f) radius = ThingFixedSize;

        var height = actor.HasPropertyWithValue("height") ? actor.GetPropertyValueFloat("height", 0) : baseInfo?.Height ?? 0f;
        var hangs = actor.GetFlagValue("spawnceiling", baseInfo?.Hangs ?? false);

        bool showsDirection;
        if (actor.HasProperty("$angled")) showsDirection = true;
        else if (actor.HasProperty("$notangled")) showsDirection = false;
        else showsDirection = baseInfo?.ShowsDirection ?? true;

        var colorIndex = baseInfo?.ColorIndex ?? 0;
        if (actor.HasPropertyWithValue("$color"))
        {
            var ci = actor.GetPropertyValueInt("$color", 0);
            colorIndex = ci == 0 || ci > 19 ? 18 : ci;
        }

        var category = ResolveCategory(actor) ?? baseInfo?.Category ?? "User-defined";

        return new ThingTypeInfo(doomEdNum, title, sprite, radius, height, hangs, showsDirection, colorIndex, category, actor.ClassName);
    }

    private static string ResolveTitle(ActorStructure actor, ThingTypeInfo? baseInfo)
    {
        string title;
        if (actor.HasPropertyWithValue("$title"))
        {
            title = actor.GetPropertyAllValues("$title");
        }
        else if (actor.HasPropertyWithValue("tag") && !actor.GetPropertyAllValues("tag").StartsWith("\"$"))
        {
            title = actor.GetPropertyAllValues("tag");
        }
        else
        {
            title = baseInfo?.Title ?? "";
        }

        if (string.IsNullOrEmpty(title)) title = actor.ClassName;
        return ZDTextParser.StripQuotes(title);
    }

    private static string? ResolveCategory(ActorStructure actor)
    {
        if (actor.HasPropertyWithValue("$category"))
        {
            var raw = ZDTextParser.StripQuotes(actor.GetPropertyAllValues("$category")).Trim();
            if (!string.IsNullOrEmpty(raw)) return raw;
        }

        if (actor.CategoryInfo is { Category.Count: > 0 }) return string.Join("/", actor.CategoryInfo.Category);

        return null;
    }
}
