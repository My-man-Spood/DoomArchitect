using System.Linq;
using System.Numerics;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Undo;

/// <summary>
/// Linedefs mode's Dissolve action - UDB's real
/// <c>LinedefsMode.DissolveItem</c> (<c>ClassicModes/LinedefsMode.cs</c>),
/// the gentler alternative to <see cref="DeleteLinedefsCommand"/>'s own
/// blunt "just remove them": before disposing the selected linedefs, any
/// two different sectors it borders get joined into one first
/// (<see cref="GeometryStitcher.JoinSectors"/>, keeping whichever has the
/// larger bounding-box area), so what used to be two rooms with a wall
/// between them becomes one open room instead of a one-sided wall facing
/// void. Separately, any sector that had fewer than 4 sidedefs *before*
/// this dissolve (recorded up front, by its own bounding-box center - not
/// "at risk," just "small enough to be worth checking afterward") gets
/// checked again once everything's done: if it now has 1 or 2 sidedefs
/// left (genuinely too small to be a real polygon), it's discarded and a
/// fresh sector is traced from scratch at that same point
/// (<see cref="BoundaryTracer.FindPotentialSectorAt(MapData,Vector2)"/> +
/// <see cref="SectorMaker.CreateAndPopulateSector"/> - UDB's own real
/// <c>Tools.MergeInvalidSectors</c>/<c>MakeSector</c>) rather than left as
/// a degenerate sliver.
/// </summary>
public sealed class DissolveLinedefsCommand : ICommand
{
    private readonly MapData map;
    private readonly IReadOnlyList<Linedef> linedefs;
    private readonly List<Action> undoActions = new();

    public DissolveLinedefsCommand(MapData map, IReadOnlyList<Linedef> linedefs)
    {
        this.map = map;
        this.linedefs = linedefs;
    }

    public void Do()
    {
        undoActions.Clear();

        // Snapshot which sectors are worth re-checking after the dissolve,
        // and where to re-trace them if they end up too small - taken
        // *before* anything is touched, exactly like UDB's own upfront
        // pass, so a sector's recorded center reflects its real pre-dissolve
        // shape even if some of its own walls are about to disappear.
        var toRecheck = new Dictionary<Sector, Vector2>();
        foreach (var linedef in linedefs)
        {
            TrackIfSmall(linedef.Front?.Sector, toRecheck);
            TrackIfSmall(linedef.Back?.Sector, toRecheck);
        }

        foreach (var linedef in linedefs)
        {
            if (linedef.Front != null && linedef.Back != null && linedef.Front.Sector != linedef.Back.Sector)
            {
                var frontSector = linedef.Front.Sector;
                var backSector = linedef.Back.Sector;
                if (SectorBounds.Compute(frontSector).Area > SectorBounds.Compute(backSector).Area)
                    GeometryStitcher.JoinSectors(map, backSector, frontSector, undoActions);
                else
                    GeometryStitcher.JoinSectors(map, frontSector, backSector, undoActions);
            }

            linedef.MarkAdjacentSectorsDirty();
            map.RemoveLinedef(linedef);
            undoActions.Add(() =>
            {
                map.RestoreLinedef(linedef);
                linedef.MarkAdjacentSectorsDirty();
            });
        }

        foreach (var (sector, center) in toRecheck) RepairIfNowInvalid(sector, center);
    }

    public void Undo()
    {
        for (var i = undoActions.Count - 1; i >= 0; i--) undoActions[i]();
    }

    private static void TrackIfSmall(Sector? sector, Dictionary<Sector, Vector2> toRecheck)
    {
        if (sector == null || toRecheck.ContainsKey(sector)) return;
        if (sector.Sidedefs.Count < 4) toRecheck[sector] = SectorBounds.Compute(sector).Center;
    }

    private void RepairIfNowInvalid(Sector sector, Vector2 center)
    {
        if (!map.Sectors.Contains(sector)) return;
        if (sector.Sidedefs.Count is 0 or >= 3) return;

        GeometryStitcher.DetachSectorSidedefs(map, sector, undoActions);

        var trace = BoundaryTracer.FindPotentialSectorAt(map, center);
        if (trace == null) return;

        var newSector = SectorMaker.CreateAndPopulateSector(map, undoActions, trace, source: null, nearbyLines: map.Linedefs);

        // "Now we go for all the lines along the sector to see if they
        // only have a back side left, flip them" - UDB's own exact
        // MergeInvalidSectors follow-up.
        var backOnly = newSector.Sidedefs
            .Select(sd => sd.Linedef)
            .Where(l => l.Front == null && l.Back != null)
            .ToList();
        GeometryStitcher.FlipBackwardLinedefs(backOnly, undoActions);
    }
}
