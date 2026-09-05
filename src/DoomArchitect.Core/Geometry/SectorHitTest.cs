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
}
