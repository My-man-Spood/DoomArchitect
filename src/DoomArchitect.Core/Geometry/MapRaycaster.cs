using System.Numerics;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Geometry;

public enum TargetSurfaceKind
{
    Floor,
    Ceiling,
    Wall,
}

/// <summary>
/// One ray-cast result: which surface was hit, closest to the ray's
/// origin. <see cref="WallSegment"/> is only set for
/// <see cref="TargetSurfaceKind.Wall"/>; for a floor/ceiling hit,
/// <see cref="Sector"/> is the sector itself, and for a wall hit it's
/// <c>WallSegment.Value.Side.Sector</c> - so callers that only care about
/// "which sector" (e.g. a future brightness-adjustment action) don't need
/// to branch on <see cref="Kind"/> at all.
/// </summary>
public readonly record struct MapTarget(TargetSurfaceKind Kind, Sector Sector, WallSegment? WallSegment, Vector3 HitPoint, double Distance);

/// <summary>
/// Finds whichever map surface a ray currently points at - the "what am I
/// looking at" primitive every future picking feature (texture picking,
/// selection, highlighting) builds on. Deliberately an interface so a
/// different implementation (e.g. one backed by real Godot physics
/// colliders) could be swapped in later without any caller changing -
/// see <see cref="MapRaycaster"/>'s own remarks for what that would and
/// wouldn't buy.
/// </summary>
public interface IMapTargetFinder
{
    MapTarget? FindTarget(Vector3 origin, Vector3 direction);
}

/// <summary>
/// A close port of UDB's own visual-mode picking (<c>VisualMode.PickObject</c>),
/// built on pure Core geometry rather than Godot physics - fully unit-
/// testable without Godot running at all, matching this project's whole
/// existing testing culture. A hand-rolled equivalent using real Godot
/// collision shapes and <c>PhysicsDirectSpaceState3D.IntersectRay</c> was
/// considered instead: it would need meaningfully less custom math (a
/// mature physics engine's own broad-phase acceleration instead of
/// <see cref="IMapSpatialIndex"/>) and would become slope-aware for free
/// once slopes exist (the collision shape would just be the rendered
/// mesh) - but it would also need an entirely separate collision-shape
/// lifecycle kept in sync with every mesh rebuild, on top of the existing
/// dirty-rebuild pipeline, and would be untestable outside a running
/// Godot instance. This class is what's actually built; <see cref="IMapTargetFinder"/>
/// exists specifically so that alternative stays possible later without
/// touching any caller.
///
/// Floor/ceiling hit-testing is already slope-general (see
/// <see cref="PlaneMath"/>'s own remarks) - only how a sector's plane gets
/// built would need to change once slopes exist. Wall hit-testing is
/// NOT: <see cref="Geometry.WallSegment.Bottom"/>/<see cref="Geometry.WallSegment.Top"/>
/// are flat doubles derived from a sector's constant floor/ceiling
/// heights, and the Z-bounds check below assumes that. Making walls
/// slope-aware is real new work belonging to <c>LinedefWallBuilder</c>
/// itself (a wall's height becoming a function of position along it, not
/// a constant) - flagged here rather than left as a silent trap for
/// whenever slope support is actually built.
/// </summary>
public sealed class MapRaycaster : IMapTargetFinder
{
    private readonly IMapSpatialIndex _index;
    private readonly Func<string, double>? _middleTextureHeightLookup;

    public MapRaycaster(IMapSpatialIndex index, Func<string, double>? middleTextureHeightLookup = null)
    {
        _index = index;
        _middleTextureHeightLookup = middleTextureHeightLookup;
    }

    public MapTarget? FindTarget(Vector3 origin, Vector3 direction)
    {
        direction = Vector3.Normalize(direction);
        var candidates = _index.QueryAlongRay(new Vector2(origin.X, origin.Y), new Vector2(direction.X, direction.Y));

        MapTarget? best = null;

        foreach (var sector in candidates.Sectors)
        {
            TryPlane(sector, TargetSurfaceKind.Floor, PlaneMath.Horizontal(sector.FloorHeight), origin, direction, ref best);
            TryPlane(sector, TargetSurfaceKind.Ceiling, PlaneMath.Horizontal(sector.CeilingHeight), origin, direction, ref best);
        }

        foreach (var linedef in candidates.Linedefs)
        {
            foreach (var segment in LinedefWallBuilder.Build(linedef, _middleTextureHeightLookup))
            {
                TryWall(segment, origin, direction, ref best);
            }
        }

        return best;
    }

    private static void TryPlane(
        Sector sector, TargetSurfaceKind kind, Plane plane, Vector3 origin, Vector3 direction, ref MapTarget? best)
    {
        if (!plane.GetIntersection(origin, direction, out var t) || t <= 0) return;
        if (best != null && t >= best.Value.Distance) return;

        var hitPoint = origin + direction * (float)t;
        if (!SectorHitTest.Contains(sector, new Vector2(hitPoint.X, hitPoint.Y))) return;

        best = new MapTarget(kind, sector, null, hitPoint, t);
    }

    private static void TryWall(WallSegment segment, Vector3 origin, Vector3 direction, ref MapTarget? best)
    {
        // Bounded 2-line intersection: the ray's 2D line against the wall
        // segment's own fixed 2D line, bounded to s in [0,1] along it.
        var rayDirection2D = new Vector2(direction.X, direction.Y);
        var wallDirection2D = segment.End.Position - segment.Start.Position;
        var originToStart = segment.Start.Position - new Vector2(origin.X, origin.Y);

        var denominator = Cross(rayDirection2D, wallDirection2D);
        if (denominator == 0) return; // ray runs exactly parallel to this wall

        var t = Cross(originToStart, wallDirection2D) / denominator;
        var s = Cross(originToStart, rayDirection2D) / denominator;
        if (t <= 0 || s < 0 || s > 1) return;
        if (best != null && t >= best.Value.Distance) return;

        var hitPoint = origin + direction * (float)t;
        if (hitPoint.Z < segment.Bottom || hitPoint.Z > segment.Top) return;

        best = new MapTarget(TargetSurfaceKind.Wall, segment.Side.Sector, segment, hitPoint, t);
    }

    private static double Cross(Vector2 a, Vector2 b) => (double)a.X * b.Y - (double)a.Y * b.X;
}
