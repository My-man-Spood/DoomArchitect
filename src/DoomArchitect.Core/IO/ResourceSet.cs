using System.Text;

namespace DoomArchitect.Core.IO;

/// <summary>
/// An ordered list of resource containers - WADs, PK3s, or a mix of both -
/// treated as one layered resource pool. Later entries are higher priority:
/// a lookup searches the container list backwards, so the most recently
/// added resource always wins a name collision. The map currently being
/// edited is always the highest-priority entry (last in the list passed to
/// the constructor).
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
    /// The real UDB equivalent of <c>DataManager.GetTextResourceData</c>
    /// for a <c>#include</c>/<c>#import</c> target - tries each
    /// container's own <see cref="IResourceContainer.FindByPath"/> in
    /// priority order (an exact relative-path match first - e.g.
    /// <c>"acs/souls.acs"</c> inside a real on-disk subfolder - falling
    /// back to that same container's own bare-title match, same as a
    /// WAD/PK3 root-level entry is already matched elsewhere; a bare
    /// <see cref="FindLump"/>-only lookup here would have silently
    /// missed a real file sitting in a subfolder - confirmed live,
    /// against a real <c>#include "acs/souls.acs"</c>) and decodes the
    /// winning entry as UTF-8 text. Null if nothing in this set has a
    /// matching entry either way.
    /// </summary>
    public string? FindIncludeText(string path)
    {
        foreach (var resource in _byPriorityDescending)
        {
            var data = resource.FindByPath(path);
            if (data != null) return Encoding.UTF8.GetString(data);
        }

        return null;
    }

    /// <summary>
    /// Each resource's own namespace is scanned independently and the
    /// results concatenated in priority order - there's no single merged
    /// global range, each container just owns its own bounded range.
    /// Returning them in priority order means a plain
    /// <c>FirstOrDefault</c> name match on the result already respects
    /// precedence with no extra logic needed at the call site.
    /// </summary>
    public IReadOnlyList<WadLump> FindNamespaceLumps(ResourceNamespace ns) =>
        _byPriorityDescending.SelectMany(r => r.FindNamespaceLumps(ns)).ToList();
}
