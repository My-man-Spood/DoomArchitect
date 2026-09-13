namespace DoomArchitect.Core.IO;

/// <summary>
/// An ordered list of resource containers - WADs, PK3s, or a mix of both -
/// treated as one layered resource pool. Later entries are higher priority,
/// exactly matching UDB's own real <c>DataManager</c> precedence (confirmed
/// via source: its single-item lookups search the container list
/// backwards, so the most recently added resource always wins a name
/// collision). The map currently being edited is always the highest-
/// priority entry (last in the list passed to the constructor) - the same
/// forced ordering UDB's own <c>MapManager</c> applies.
///
/// Was <c>WadResourceSet</c>, WAD-only, before PK3 support existed - widened
/// to hold any <see cref="IResourceContainer"/> once a PK3 reader
/// (<see cref="Pk3File"/>) existed to put alongside <see cref="WadFile"/>.
/// Priority logic is unchanged; only the element type is wider.
/// </summary>
public sealed class ResourceSet
{
    private readonly IReadOnlyList<IResourceContainer> _byPriorityDescending;

    public ResourceSet(IReadOnlyList<IResourceContainer> resourcesByAscendingPriority)
    {
        _byPriorityDescending = resourcesByAscendingPriority.Reverse().ToList();
    }

    public static ResourceSet Single(IResourceContainer resource) => new(new[] { resource });

    /// <summary>Every container in priority order, highest first - lets a caller (e.g. a texture browser's per-resource tree) enumerate the actual resources this set is layering, not just query merged results.</summary>
    public IReadOnlyList<IResourceContainer> Containers => _byPriorityDescending;

    /// <summary>The single highest-priority container that would satisfy <see cref="FindLump"/> for <paramref name="name"/> - lets a caller answer "which one resource actually won this lump" (e.g. TEXTURE1/PNAMES's real winner-take-all precedence) without duplicating <see cref="FindLump"/>'s own search.</summary>
    public IResourceContainer? FindLumpSource(string name) =>
        _byPriorityDescending.FirstOrDefault(r => r.FindLump(name) != null);

    public WadLump? FindLump(string name)
    {
        foreach (var resource in _byPriorityDescending)
        {
            var lump = resource.FindLump(name);
            if (lump != null) return lump;
        }

        return null;
    }

    /// <summary>
    /// Each resource's own namespace is scanned independently and the
    /// results concatenated in priority order - matching UDB's real
    /// sprite-range handling (each container owns its own bounded range;
    /// <c>DataManager</c> has no single merged global range, it just tries
    /// each resource in priority order). Returning them in priority order
    /// means a plain <c>FirstOrDefault</c> name match on the result already
    /// respects precedence with no extra logic needed at the call site.
    /// </summary>
    public IReadOnlyList<WadLump> FindNamespaceLumps(ResourceNamespace ns) =>
        _byPriorityDescending.SelectMany(r => r.FindNamespaceLumps(ns)).ToList();
}
