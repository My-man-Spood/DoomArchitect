namespace DoomArchitect.Core.IO;

/// <summary>
/// An ordered list of WADs treated as one layered resource pool - later
/// entries are higher priority, exactly matching UDB's own real
/// <c>DataManager</c> precedence (confirmed via source: its single-item
/// lookups search the container list backwards, so the most recently
/// added resource always wins a name collision). The map currently being
/// edited is always the highest-priority entry (last in the list passed
/// to the constructor) - the same forced ordering UDB's own
/// <c>MapManager</c> applies.
///
/// Deliberately WAD-only, unlike UDB's own <c>DataReader</c> hierarchy
/// (which also covers PK3 archives and plain directories) - DoomArchitect's
/// texture pipeline doesn't support those resource kinds at all yet, so
/// this is scoped to what's actually usable today rather than a
/// speculative full abstraction.
/// </summary>
public sealed class WadResourceSet
{
    private readonly IReadOnlyList<WadFile> _byPriorityDescending;

    public WadResourceSet(IReadOnlyList<WadFile> wadsByAscendingPriority)
    {
        _byPriorityDescending = wadsByAscendingPriority.Reverse().ToList();
    }

    public static WadResourceSet Single(WadFile wad) => new(new[] { wad });

    public WadLump? FindLump(string name)
    {
        foreach (var wad in _byPriorityDescending)
        {
            var lump = wad.FindLump(name);
            if (lump != null) return lump;
        }

        return null;
    }

    /// <summary>
    /// Each resource's own marker-bounded range is scanned independently
    /// and the results concatenated in priority order - matching UDB's
    /// real sprite-range handling (each <c>WADReader</c> owns its own
    /// <c>S_START</c>/<c>S_END</c> ranges; <c>DataManager</c> has no
    /// single merged global range, it just tries each resource in
    /// priority order). Returning them in priority order means a plain
    /// <c>FirstOrDefault</c> name match on the result already respects
    /// precedence with no extra logic needed at the call site.
    /// </summary>
    public IReadOnlyList<WadLump> FindLumpsBetweenMarkers(string startMarker, string endMarker) =>
        _byPriorityDescending.SelectMany(w => w.FindLumpsBetweenMarkers(startMarker, endMarker)).ToList();
}
