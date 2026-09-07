using System.Numerics;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Geometry;

/// <summary>
/// Whether a point falls inside a sector's actual floor area - correctly
/// excluding holes (and re-including islands nested inside those holes,
/// arbitrarily deep) by summing even-odd containment across every one of
/// the sector's traced loops. This doesn't need the loops nested into a
/// tree or bridged into simple polygons first (that's only required for
/// triangulation) - the even-odd rule handles nesting on its own.
/// </summary>
public static class SectorHitTest
{
    public static bool Contains(Sector sector, Vector2 point)
    {
        var inside = false;
        foreach (var loop in SectorTracer.Trace(sector))
        {
            if (loop.Contains(point)) inside = !inside;
        }

        return inside;
    }

    /// <summary>
    /// Which of <paramref name="sectors"/> contains <paramref name="point"/>,
    /// or <c>null</c> if none do (e.g. a Thing placed outside the map's
    /// geometry). A brute-force scan is fine here - callers only need this
    /// once per Thing at load/rebuild time, never per-frame.
    /// </summary>
    public static Sector? FindContaining(IEnumerable<Sector> sectors, Vector2 point) =>
        sectors.FirstOrDefault(sector => Contains(sector, point));
}
