using System.Numerics;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Geometry;

/// <summary>
/// Buckets a map's sectors and linedefs into fixed-size cells over the
/// map's own bounds, and answers a ray query by walking only the cells the
/// ray's 2D (X,Y) path actually passes through - "fast voxel traversal"
/// (Amanatides &amp; Woo, 1987), a standard, well-known algorithm for
/// visiting the sequence of grid cells a line crosses, in order, without
/// testing every cell in the grid.
///
/// Deliberately simpler than UDB's own quadtree (<c>VisualBlockMap</c>):
/// no recursive subdivision, no node-splitting logic, just a flat
/// dictionary keyed by cell coordinates. This trades handling pathological
/// density (a tiny cluster of geometry lost in a huge empty map) for a much
/// smaller, easier-to-verify implementation - a reasonable trade for the
/// fairly evenly-distributed geometry typical of real Doom maps. Swappable
/// behind <see cref="IMapSpatialIndex"/> for a quadtree later if profiling
/// on real content ever shows this mattering.
/// </summary>
public sealed class UniformGridSpatialIndex : IMapSpatialIndex
{
    private const float CellSize = 256f;

    private readonly Dictionary<(int X, int Y), List<Sector>> _sectorCells = new();
    private readonly Dictionary<(int X, int Y), List<Linedef>> _linedefCells = new();

    private bool _hasContent;
    private Vector2 _boundsMin;
    private Vector2 _boundsMax;

    public void Rebuild(MapData map)
    {
        _sectorCells.Clear();
        _linedefCells.Clear();
        _hasContent = false;
        _boundsMin = Vector2.Zero;
        _boundsMax = Vector2.Zero;

        foreach (var sector in map.Sectors)
        {
            if (TryGetSectorBounds(sector, out var min, out var max))
            {
                ExpandBounds(min, max);
                InsertIntoCells(_sectorCells, sector, min, max);
            }
        }

        foreach (var linedef in map.Linedefs)
        {
            var min = Vector2.Min(linedef.Start.Position, linedef.End.Position);
            var max = Vector2.Max(linedef.Start.Position, linedef.End.Position);
            ExpandBounds(min, max);
            InsertIntoCells(_linedefCells, linedef, min, max);
        }
    }

    public SpatialQueryResult QueryAlongRay(Vector2 origin, Vector2 direction)
    {
        var sectors = new HashSet<Sector>();
        var linedefs = new HashSet<Linedef>();

        if (!_hasContent || !TryIntersectBounds(origin, direction, out var tEntry, out var tExit))
        {
            return new SpatialQueryResult(sectors, linedefs);
        }

        WalkCells(origin, direction, Math.Max(tEntry, 0), tExit, cell =>
        {
            if (_sectorCells.TryGetValue(cell, out var cellSectors))
            {
                foreach (var sector in cellSectors) sectors.Add(sector);
            }

            if (_linedefCells.TryGetValue(cell, out var cellLinedefs))
            {
                foreach (var linedef in cellLinedefs) linedefs.Add(linedef);
            }
        });

        return new SpatialQueryResult(sectors, linedefs);
    }

    private static bool TryGetSectorBounds(Sector sector, out Vector2 min, out Vector2 max)
    {
        min = default;
        max = default;
        var any = false;

        foreach (var sidedef in sector.Sidedefs)
        {
            foreach (var position in new[] { sidedef.Linedef.Start.Position, sidedef.Linedef.End.Position })
            {
                if (!any)
                {
                    min = max = position;
                    any = true;
                }
                else
                {
                    min = Vector2.Min(min, position);
                    max = Vector2.Max(max, position);
                }
            }
        }

        return any;
    }

    private void ExpandBounds(Vector2 min, Vector2 max)
    {
        if (!_hasContent)
        {
            _boundsMin = min;
            _boundsMax = max;
            _hasContent = true;
        }
        else
        {
            _boundsMin = Vector2.Min(_boundsMin, min);
            _boundsMax = Vector2.Max(_boundsMax, max);
        }
    }

    private static void InsertIntoCells<T>(Dictionary<(int X, int Y), List<T>> cells, T value, Vector2 min, Vector2 max)
    {
        var (minCellX, minCellY) = ToCell(min);
        var (maxCellX, maxCellY) = ToCell(max);

        for (var cellX = minCellX; cellX <= maxCellX; cellX++)
        {
            for (var cellY = minCellY; cellY <= maxCellY; cellY++)
            {
                if (!cells.TryGetValue((cellX, cellY), out var list))
                {
                    list = new List<T>();
                    cells[(cellX, cellY)] = list;
                }

                list.Add(value);
            }
        }
    }

    private static (int X, int Y) ToCell(Vector2 position) =>
        ((int)Math.Floor(position.X / CellSize), (int)Math.Floor(position.Y / CellSize));

    /// <summary>
    /// Standard "slab method" 2D ray-vs-axis-aligned-box intersection.
    /// Bounds the grid walk to the actual populated area, so a ray that
    /// never comes near any geometry (pointed away from the map entirely)
    /// terminates immediately instead of needing a separate max-distance
    /// parameter or an unbounded cell-index heuristic.
    /// </summary>
    private bool TryIntersectBounds(Vector2 origin, Vector2 direction, out double tEntry, out double tExit)
    {
        var tMin = double.NegativeInfinity;
        var tMax = double.PositiveInfinity;

        if (!IntersectSlab(origin.X, direction.X, _boundsMin.X, _boundsMax.X, ref tMin, ref tMax) ||
            !IntersectSlab(origin.Y, direction.Y, _boundsMin.Y, _boundsMax.Y, ref tMin, ref tMax))
        {
            tEntry = 0;
            tExit = 0;
            return false;
        }

        tEntry = tMin;
        tExit = tMax;
        return tMax >= 0 && tMin <= tMax;
    }

    private static bool IntersectSlab(double origin, double direction, double min, double max, ref double tMin, ref double tMax)
    {
        if (direction == 0)
        {
            return origin >= min && origin <= max;
        }

        var t1 = (min - origin) / direction;
        var t2 = (max - origin) / direction;
        if (t1 > t2) (t1, t2) = (t2, t1);

        tMin = Math.Max(tMin, t1);
        tMax = Math.Min(tMax, t2);
        return tMin <= tMax;
    }

    /// <summary>
    /// The Amanatides-Woo traversal itself: from the cell containing
    /// <c>origin + tStart*direction</c>, repeatedly step to whichever
    /// neighboring cell (in X or Y) the ray crosses into next, until
    /// <c>t</c> passes <paramref name="tEnd"/>.
    /// </summary>
    private static void WalkCells(
        Vector2 origin, Vector2 direction, double tStart, double tEnd, Action<(int X, int Y)> visit)
    {
        var start = origin + direction * (float)tStart;
        var (cellX, cellY) = ToCell(start);

        var (tMaxX, tDeltaX, stepX) = AxisTraversal(start.X, direction.X);
        var (tMaxY, tDeltaY, stepY) = AxisTraversal(start.Y, direction.Y);

        var t = tStart;
        while (t <= tEnd)
        {
            visit((cellX, cellY));

            if (tMaxX < tMaxY)
            {
                cellX += stepX;
                t = tStart + tMaxX;
                tMaxX += tDeltaX;
            }
            else
            {
                cellY += stepY;
                t = tStart + tMaxY;
                tMaxY += tDeltaY;
            }

            if (stepX == 0 && stepY == 0) break; // ray has no direction at all - nowhere else to go
        }
    }

    /// <summary>
    /// Per-axis Amanatides-Woo setup: how far (in ray-t) to the next cell
    /// boundary crossing (<c>tMax</c>), how much further each full cell
    /// crossing adds (<c>tDelta</c>), and which way cell indices move
    /// (<c>step</c>). A zero-direction axis never crosses a boundary, so
    /// its tMax/tDelta are +infinity - the traversal then advances purely
    /// on the other axis, exactly as it should for an axis-aligned ray.
    /// </summary>
    private static (double TMax, double TDelta, int Step) AxisTraversal(float position, float direction)
    {
        if (direction == 0) return (double.PositiveInfinity, double.PositiveInfinity, 0);

        var cell = Math.Floor(position / CellSize);
        if (direction > 0)
        {
            var nextBoundary = (cell + 1) * CellSize;
            return ((nextBoundary - position) / direction, CellSize / direction, 1);
        }
        else
        {
            var nextBoundary = cell * CellSize;
            return ((nextBoundary - position) / direction, CellSize / -direction, -1);
        }
    }
}
