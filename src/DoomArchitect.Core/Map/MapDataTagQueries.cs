namespace DoomArchitect.Core.Map;

/// <summary>
/// Backs the Sector/Linedef/Thing dialogs' "New" (map-wide) and "Unused"
/// (this element type only) tag-allocation buttons. Only scans what this
/// project's Core actually models today - each sector's, linedef's, and
/// thing's own <c>id</c>/<c>moreids</c> fields - not linedef/thing
/// action-argument tag slots (e.g. a Teleport's destination tag), since
/// neither <see cref="Linedef"/> nor <see cref="Thing"/> has typed
/// argument accessors and <c>IGameConfiguration</c> has no per-argument
/// "this is a tag" metadata to identify them by; a real gap against UDB's
/// own broader search, not an oversight.
/// </summary>
public static class MapDataTagQueries
{
    public static IReadOnlySet<long> GetUsedTags(this MapData map)
    {
        var used = new HashSet<long>();
        CollectSectorTags(map, used);
        CollectLinedefTags(map, used);
        CollectThingTags(map, used);
        return used;
    }

    public static IReadOnlySet<long> GetUsedSectorTags(this MapData map)
    {
        var used = new HashSet<long>();
        CollectSectorTags(map, used);
        return used;
    }

    /// <summary>The "this element type only" scope for a linedef's own tag editor - mirrors <see cref="GetUsedSectorTags"/>.</summary>
    public static IReadOnlySet<long> GetUsedLinedefTags(this MapData map)
    {
        var used = new HashSet<long>();
        CollectLinedefTags(map, used);
        return used;
    }

    /// <summary>The "this element type only" scope for a thing's own tag editor - mirrors <see cref="GetUsedSectorTags"/>/<see cref="GetUsedLinedefTags"/>.</summary>
    public static IReadOnlySet<long> GetUsedThingTags(this MapData map)
    {
        var used = new HashSet<long>();
        CollectThingTags(map, used);
        return used;
    }

    /// <summary>
    /// Every sector whose own tag(s) include <paramref name="tag"/> - the
    /// reverse of <see cref="ParseTags"/>, needed by the 2D tag-arrow
    /// indicator (find what a hovered linedef's tag actually points at).
    /// A plain linear scan, matching every other "find" operation in this
    /// codebase (<c>MapData</c> keeps no index of any kind over its own
    /// element lists).
    /// </summary>
    public static IEnumerable<Sector> GetSectorsWithTag(this MapData map, long tag) =>
        map.Sectors.Where(sector => ParseTags(sector.Fields).Contains(tag));

    /// <summary>Mirrors <see cref="GetSectorsWithTag"/> for linedefs - the reverse direction (hovering a tagged sector, finding what points at it).</summary>
    public static IEnumerable<Linedef> GetLinedefsWithTag(this MapData map, long tag) =>
        map.Linedefs.Where(linedef => ParseTags(linedef.Fields).Contains(tag));

    private static void CollectSectorTags(MapData map, HashSet<long> used)
    {
        foreach (var sector in map.Sectors)
        {
            foreach (var tag in ParseTags(sector.Fields)) used.Add(tag);
        }
    }

    private static void CollectLinedefTags(MapData map, HashSet<long> used)
    {
        foreach (var linedef in map.Linedefs)
        {
            foreach (var tag in ParseTags(linedef.Fields)) used.Add(tag);
        }
    }

    private static void CollectThingTags(MapData map, HashSet<long> used)
    {
        foreach (var thing in map.Things)
        {
            foreach (var tag in ParseTags(thing.Fields)) used.Add(tag);
        }
    }

    /// <summary>The same <c>id</c> (primary) + space-separated <c>moreids</c> (extras) shape the Sector dialog's tag editor reads/writes. Tag <c>0</c> means "no tag" and is never yielded.</summary>
    public static IEnumerable<long> ParseTags(UniFields fields)
    {
        var primary = fields.GetInteger("id", 0);
        if (primary != 0) yield return primary;

        var moreIds = fields.GetString("moreids", "");
        foreach (var token in moreIds.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (long.TryParse(token, out var extra) && extra != 0) yield return extra;
        }
    }
}
