using System.Numerics;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Geometry;

public enum TargetSurfaceKind
{
    Floor,
    Ceiling,
    Wall,
    Thing,
}

/// <summary>
/// One ray-cast result: which surface was hit, closest to the ray's
/// origin. <see cref="WallSegment"/> is only set for
/// <see cref="TargetSurfaceKind.Wall"/>, <see cref="Thing"/> only for
/// <see cref="TargetSurfaceKind.Thing"/>. <see cref="Sector"/> is the
/// sector itself for a floor/ceiling hit, <c>WallSegment.Value.Side.Sector</c>
/// for a wall hit, and the Thing's own containing sector (possibly
/// <c>null</c> - a Thing sitting outside every sector is a real, if
/// degenerate, editing state UDB itself allows) for a Thing hit - so a
/// caller that only cares about "which sector" (e.g. a brightness-
/// adjustment action) doesn't need to branch on <see cref="Kind"/> at all
/// for the first three kinds, but must still null-check for a Thing.
/// </summary>
public readonly record struct MapTarget(TargetSurfaceKind Kind, Sector? Sector, WallSegment? WallSegment, Vector3 HitPoint, double Distance, Thing? Thing = null);

/// <summary>
/// What <see cref="MapRaycaster"/> needs to hit-test (and
/// <c>TargetHighlight</c> needs to draw a highlight box for) one Thing -
/// resolved by the caller, since it needs game-configuration data
/// (a type's real radius/height/hangs-from-ceiling) Core.Geometry has no
/// access to. Matches UDB's own real <c>BaseVisualThing</c> pick-box setup
/// exactly: an axis-aligned box, <see cref="Radius"/> out from the Thing's
/// own X/Y position on every side, spanning <see cref="WorldZ"/> (the
/// Thing's own resolved floor-standing/ceiling-hanging origin - the same
/// value actually used to position its rendered billboard) up by
/// <see cref="Height"/> - deliberately not oriented to the camera the way
/// the billboard itself is rendered; UDB's own pick box isn't either.
/// </summary>
public readonly record struct ThingPickBounds(double Radius, double Height, double WorldZ, Sector? Sector);

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
    private readonly Func<Thing, ThingPickBounds>? _thingPickBoundsLookup;

    /// <param name="index">Spatially indexes sectors/linedefs/things to narrow candidates.</param>
    /// <param name="middleTextureHeightLookup">See <see cref="LinedefWallBuilder.Build"/>'s own remarks.</param>
    /// <param name="thingPickBoundsLookup">
    /// Resolves a Thing's real pick box (see <see cref="ThingPickBounds"/>'s
    /// own remarks) - needs game-configuration data this pure-geometry
    /// layer has no access to, so it's supplied by the caller. Passing
    /// <c>null</c> (the default) skips Thing picking entirely, a safe
    /// default for any caller not yet wired to real game-configuration
    /// data - every other target kind is unaffected.
    /// </param>
    public MapRaycaster(
        IMapSpatialIndex index, Func<string, double>? middleTextureHeightLookup = null,
        Func<Thing, ThingPickBounds>? thingPickBoundsLookup = null)
    {
        _index = index;
        _middleTextureHeightLookup = middleTextureHeightLookup;
        _thingPickBoundsLookup = thingPickBoundsLookup;
    }

    public MapTarget? FindTarget(Vector3 origin, Vector3 direction)
    {
        direction = Vector3.Normalize(direction);
        var origin2D = new Vector2(origin.X, origin.Y);
        var candidates = _index.QueryAlongRay(origin2D, new Vector2(direction.X, direction.Y));

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
                TryWall(segment, origin, direction, origin2D, ref best);
            }
        }

        if (_thingPickBoundsLookup != null)
        {
            foreach (var thing in candidates.Things)
            {
                TryThing(thing, _thingPickBoundsLookup(thing), origin, direction, ref best);
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

    /// <summary>
    /// Standard "slab method" ray-vs-axis-aligned-box intersection (the
    /// same real algorithm UDB's own <c>BaseVisualThing.PickAccurate</c>
    /// uses, ported directly rather than approximated with a bounding
    /// sphere/cylinder) - deliberately axis-aligned, not oriented to the
    /// camera the way the Thing's own rendered billboard is; that's UDB's
    /// own real behavior too, not a simplification made here.
    /// </summary>
    private static void TryThing(Thing thing, ThingPickBounds bounds, Vector3 origin, Vector3 direction, ref MapTarget? best)
    {
        var boxMin = new Vector3(
            (float)(thing.Position.X - bounds.Radius), (float)(thing.Position.Y - bounds.Radius), (float)bounds.WorldZ);
        var boxMax = new Vector3(
            (float)(thing.Position.X + bounds.Radius), (float)(thing.Position.Y + bounds.Radius), (float)(bounds.WorldZ + bounds.Height));

        if (!TryIntersectAabb(origin, direction, boxMin, boxMax, out var t) || t <= 0) return;
        if (best != null && t >= best.Value.Distance) return;

        var hitPoint = origin + direction * (float)t;
        best = new MapTarget(TargetSurfaceKind.Thing, bounds.Sector, null, hitPoint, t, thing);
    }

    private static bool TryIntersectAabb(Vector3 origin, Vector3 direction, Vector3 boxMin, Vector3 boxMax, out double t)
    {
        var tMin = double.NegativeInfinity;
        var tMax = double.PositiveInfinity;

        if (!IntersectSlab(origin.X, direction.X, boxMin.X, boxMax.X, ref tMin, ref tMax) ||
            !IntersectSlab(origin.Y, direction.Y, boxMin.Y, boxMax.Y, ref tMin, ref tMax) ||
            !IntersectSlab(origin.Z, direction.Z, boxMin.Z, boxMax.Z, ref tMin, ref tMax))
        {
            t = 0;
            return false;
        }

        // tMin is the entry point (the near face) - the one a caller
        // actually wants to report as "where the ray hit the box", unless
        // the origin already starts inside it, in which case there's no
        // real near-face crossing to report and tMax (the far/exit face)
        // is the only intersection that exists at all.
        t = tMin >= 0 ? tMin : tMax;
        return tMax >= 0;
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
    /// A two-sided masked middle builds one segment per side, front and
    /// back, both spanning the exact same <see cref="WallSegment.Bottom"/>/
    /// <see cref="WallSegment.Top"/> along the exact same line (they share
    /// the same "opening" by construction) - a ray hitting that position
    /// hits both at the identical distance, a genuine tie a plain "closer
    /// wins" comparison can't resolve at all; it used to just keep
    /// whichever was tried first, which is always the front (see
    /// <c>LinedefWallBuilder.BuildTwoSided</c>'s own call order) -
    /// regardless of which face the viewer was actually looking at. A
    /// real bug, caught by the user: texture-offset nudging silently
    /// edited the front sidedef's own fields while aiming at the back.
    ///
    /// Ties are broken by which side of the wall's own line
    /// (<paramref name="origin2D"/>) the viewer is actually standing on -
    /// matching <c>SectorTracer</c>'s own real, already-established
    /// "front is on the walker's right walking Start-&gt;End" convention
    /// (<see cref="GeometryMath.SideOfLine"/> negative = right = front) -
    /// rather than by sector identity, which was tried first and is
    /// genuinely wrong for a real, common case: a self-referencing-sector
    /// decoration (e.g. a flag or curtain hanging inside a single room)
    /// puts front *and* back on the exact same sector, where a sector-
    /// based tie-break can never tell them apart at all - confirmed
    /// against the user's own real map, which has 105 two-sided masked
    /// middles, several of them exactly this self-referencing shape.
    /// </summary>
    private static void TryWall(WallSegment segment, Vector3 origin, Vector3 direction, Vector2 origin2D, ref MapTarget? best)
    {
        // Bounded 2-line intersection: the ray's 2D line against the wall
        // segment's own fixed 2D line, bounded to s in [0,1] along it.
        var rayDirection2D = new Vector2(direction.X, direction.Y);
        var wallDirection2D = segment.End.Position - segment.Start.Position;
        var originToStart = segment.Start.Position - origin2D;

        var denominator = Cross(rayDirection2D, wallDirection2D);
        if (denominator == 0) return; // ray runs exactly parallel to this wall

        var t = Cross(originToStart, wallDirection2D) / denominator;
        var s = Cross(originToStart, rayDirection2D) / denominator;
        if (t <= 0 || s < 0 || s > 1) return;

        var hitPoint = origin + direction * (float)t;
        if (hitPoint.Z < segment.Bottom || hitPoint.Z > segment.Top) return;

        if (!IsBetterWallCandidate(t, segment, origin2D, best)) return;

        best = new MapTarget(TargetSurfaceKind.Wall, segment.Side.Sector, segment, hitPoint, t);
    }

    private static bool IsBetterWallCandidate(double t, WallSegment segment, Vector2 origin2D, MapTarget? best)
    {
        if (best == null) return true;

        const double tieEpsilon = 0.01;
        if (t > best.Value.Distance + tieEpsilon) return false;
        if (t < best.Value.Distance - tieEpsilon) return true;

        // Genuine tie (see TryWall's own remarks) - prefer whichever
        // segment actually faces the viewer's own current position.
        // "Front is on the walker's right walking Start->End" is the
        // real, already-shipped convention this codebase relies on
        // elsewhere - WallMeshBuilder's own face-winding and
        // LinedefOverlayHandler.DrawFrontIndicator's visible 2D front
        // tick both use it directly (right = (direction.Y, -direction.X)),
        // and both are proven correct by the user's own actual use of the
        // program, not just a unit test. Working the same derivation
        // through GeometryMath.SideOfLine's own formula: a point offset
        // along that exact right vector always yields a *negative*
        // result, so Front's own sector sits on the negative side, not
        // positive. An earlier version of this check used `&gt; 0`,
        // "verified" only against a hand-built regression test whose own
        // Front/Back sector assignment had been picked to match that
        // code rather than checked against the convention above - a
        // circular check that passed while still being backwards, caught
        // only once real live testing kept finding the wrong side no
        // matter which face was targeted.
        var viewerFacesFront = GeometryMath.SideOfLine(segment.Start.Position, segment.End.Position, origin2D) < 0;
        var currentIsFront = best.Value.WallSegment?.Side.IsFront ?? false;
        if (currentIsFront == viewerFacesFront) return false;
        return segment.Side.IsFront == viewerFacesFront;
    }

    private static double Cross(Vector2 a, Vector2 b) => (double)a.X * b.Y - (double)a.Y * b.X;
}
