using DoomArchitect.Core.Map;
using DoomArchitect.Core.Undo;

namespace DoomArchitect.Core.Geometry;

/// <summary>
/// "Turn a traced boundary into a real, populated Sector" - extracted out
/// of <see cref="DrawLoopCommand"/> once a genuine second consumer arrived
/// (<c>DissolveLinedefsCommand</c>'s own port of UDB's real
/// <c>Tools.MergeInvalidSectors</c>, which re-creates a sector from
/// scratch at an arbitrary point the same way a freshly drawn loop's own
/// interior claim does) - matching this project's own "one parser per
/// format... reconsider factoring out once a second real consumer shows
/// up" precedent (see <c>TODO/architecture-notes.md</c>), applied here to
/// sector-creation orchestration instead of text parsing. A direct,
/// mechanical extraction - every method here behaves exactly as it did as
/// a private <see cref="DrawLoopCommand"/> member, just taking
/// <c>map</c>/<c>undoActions</c> explicitly instead of reading them off
/// <c>this</c>, matching <see cref="GeometryStitcher"/>'s own established
/// calling convention for shared geometry orchestration.
/// </summary>
public static class SectorMaker
{
    /// <param name="nearbyLines">
    /// UDB's <c>MakeSector</c> "nearbylines" fallback - consulted only
    /// when <paramref name="source"/> is null (the trace touches nothing
    /// existing at all). Finds the nearest of these to a point just off
    /// the trace's own first entry and reads whichever sector lies on the
    /// matching side of *that* line - proximity-based inheritance, not a
    /// point-in-polygon "which sector contains this shape" containment
    /// test (UDB has no such thing here either).
    /// </param>
    public static Sector CreateAndPopulateSector(
        MapData map, List<Action> undoActions, IReadOnlyList<LinedefSide> trace, Sidedef? source,
        IReadOnlyList<Linedef>? nearbyLines = null)
    {
        var sourceSector = source?.Sector;
        Sector? nearestBasicSector = null;

        if (sourceSector == null && nearbyLines != null && trace.Count > 0)
        {
            FindNearbySectorFallback(trace[0], nearbyLines, out sourceSector, out nearestBasicSector);
        }

        var floorHeight = sourceSector?.FloorHeight ?? nearestBasicSector?.FloorHeight ?? DrawLoopCommand.DefaultFloorHeight;
        var ceilingHeight = sourceSector?.CeilingHeight ?? nearestBasicSector?.CeilingHeight ?? DrawLoopCommand.DefaultCeilingHeight;
        var newSector = map.CreateSector(floorHeight, ceilingHeight);
        undoActions.Add(() => map.RemoveSector(newSector));

        if (sourceSector != null) CopySectorProperties(sourceSector, newSector);
        else if (nearestBasicSector != null)
        {
            // "Any side is better than no side" fallback: the nearest
            // line's *opposite* side had a sector but its matching side
            // didn't, so only basic texture/brightness settings are
            // trustworthy here, not a full property/UDMF-field copy.
            newSector.FloorTexture = nearestBasicSector.FloorTexture;
            newSector.CeilingTexture = nearestBasicSector.CeilingTexture;
            newSector.Brightness = nearestBasicSector.Brightness;
        }
        else
        {
            newSector.FloorTexture = DrawLoopCommand.DefaultFloorTexture;
            newSector.CeilingTexture = DrawLoopCommand.DefaultCeilingTexture;
            newSector.Brightness = DrawLoopCommand.DefaultBrightness;
        }

        // Every side in the trace, not just ones still void - nothing
        // already points at newSector (it was just created), so every
        // non-null matching side here is an old sidedef genuinely being
        // redistributed onto this newly-formed sector, not merely filled
        // in.
        foreach (var side in trace)
        {
            AttachOrRetargetSidedefTracked(map, undoActions, side.Linedef, side.Front, newSector);
        }

        return newSector;
    }

    private static void FindNearbySectorFallback(
        LinedefSide testSide, IReadOnlyList<Linedef> nearbyLines, out Sector? sourceSector, out Sector? nearestBasicSector)
    {
        sourceSector = null;
        nearestBasicSector = null;

        var testPoint = BoundaryTracer.SidePoint(testSide);
        var nearest = GeometryStitcher.FindNearestLinedef(nearbyLines, testPoint);
        if (nearest == null) return;

        var sideOfLine = GeometryMath.SideOfLine(nearest.Start.Position, nearest.End.Position, testPoint);
        var matching = sideOfLine < 0 ? nearest.Front : nearest.Back;
        if (matching != null)
        {
            sourceSector = matching.Sector;
            return;
        }

        var opposite = sideOfLine < 0 ? nearest.Back : nearest.Front;
        if (opposite != null) nearestBasicSector = opposite.Sector;
    }

    /// <summary>
    /// The ambiguous-neighbor rule: first match walking the trace in
    /// order - the *matching* side of each entry (the side that entry's
    /// own <see cref="LinedefSide.Front"/> represents) first;
    /// <see cref="FindOppositeSidedefInTrace"/> checks the other side of
    /// each entry instead, as interior's own fallback second pass when
    /// nothing matching is found anywhere in the boundary.
    /// </summary>
    public static Sidedef? FindMatchingSidedefInTrace(IReadOnlyList<LinedefSide> trace)
    {
        foreach (var side in trace)
        {
            var sidedef = side.Front ? side.Linedef.Front : side.Linedef.Back;
            if (sidedef != null) return sidedef;
        }

        return null;
    }

    public static Sidedef? FindOppositeSidedefInTrace(IReadOnlyList<LinedefSide> trace)
    {
        foreach (var side in trace)
        {
            var sidedef = side.Front ? side.Linedef.Back : side.Linedef.Front;
            if (sidedef != null) return sidedef;
        }

        return null;
    }

    private static void CopySectorProperties(Sector from, Sector to)
    {
        to.FloorTexture = from.FloorTexture;
        to.CeilingTexture = from.CeilingTexture;
        to.Brightness = from.Brightness;
        foreach (var (key, value) in from.Fields) to.Fields[key] = value;
    }

    public static void AttachOrRetargetSidedefTracked(MapData map, List<Action> undoActions, Linedef linedef, bool front, Sector sector)
    {
        var existing = front ? linedef.Front : linedef.Back;

        if (existing == null)
        {
            var opposite = front ? linedef.Back : linedef.Front;
            var originalOppositeMiddle = opposite?.MiddleTexture;

            map.AttachOrRetargetSidedef(linedef, front, sector);
            var created = (front ? linedef.Front : linedef.Back)!;

            // A wall gaining its very first sidedef, with nothing on the
            // other side either, is staying one-sided - needs a real,
            // solid texture (Phase 1's own established default) or it'd
            // render as nothing at all. A wall whose *opposite* side
            // already exists is becoming two-sided by this exact call -
            // matches MapData.AttachOrRetargetSidedef's own real cleanup
            // of the opposite side's now-superfluous middle texture, just
            // applied here to this brand-new side instead: a plain
            // two-sided wall's middle has nothing to mean either, on
            // either face, once there's a real sector on both sides.
            created.MiddleTexture = opposite != null ? "-" : DrawLoopCommand.DefaultWallTexture;

            undoActions.Add(() =>
            {
                sector.RemoveSidedef(created);
                if (front) linedef.Front = null; else linedef.Back = null;
                if (opposite != null) opposite.MiddleTexture = originalOppositeMiddle!;
                // Mirrors MapData.AttachOrRetargetSidedef's own forward-direction
                // dirty-marking, reversed: this linedef losing a whole side changes
                // both its own wall mesh and (when it had one) the opposite side's
                // sector's floor/ceiling boundary.
                sector.NeedsRebuild = true;
                if (opposite != null) opposite.Sector.NeedsRebuild = true;
            });
        }
        else
        {
            var originalSector = existing.Sector;
            map.AttachOrRetargetSidedef(linedef, front, sector);

            undoActions.Add(() =>
            {
                sector.RemoveSidedef(existing);
                existing.Sector = originalSector;
                originalSector.AddSidedef(existing);
                sector.NeedsRebuild = true;
                originalSector.NeedsRebuild = true;
            });
        }
    }
}
