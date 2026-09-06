using System.Numerics;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Geometry;

/// <summary>
/// Sectors/linedefs found near a ray's path - a candidate set for
/// <see cref="MapRaycaster"/> to run its own precise (and more expensive)
/// intersection tests against, rather than every piece of geometry in the
/// whole map.
/// </summary>
public readonly record struct SpatialQueryResult(IReadOnlyCollection<Sector> Sectors, IReadOnlyCollection<Linedef> Linedefs);

/// <summary>
/// Spatially indexes a map's sectors/linedefs. Deliberately an interface,
/// not a concrete class, so the initial implementation
/// (<see cref="UniformGridSpatialIndex"/>) can be swapped later for a more
/// sophisticated structure (e.g. a quadtree closer to UDB's own
/// <c>VisualBlockMap</c>) without changing anything else, if profiling on
/// real content ever shows it's needed.
/// </summary>
public interface IMapSpatialIndex
{
    void Rebuild(MapData map);

    SpatialQueryResult QueryAlongRay(Vector2 origin, Vector2 direction);
}
