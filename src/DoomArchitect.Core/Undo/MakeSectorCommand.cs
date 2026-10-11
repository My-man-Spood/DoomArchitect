using System.Linq;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Undo;

/// <summary>
/// Make Sector mode's own commit - UDB's real
/// <c>MakeSectorMode.MakeSector</c> (<c>ClassicModes/MakeSectorMode.cs</c>):
/// turn an already-traced boundary (<see cref="trace"/>, found live by
/// <c>MakeSectorOverlayHandler</c>'s own hover update via
/// <see cref="BoundaryTracer.FindPotentialSectorAt"/>) into a real,
/// populated sector via <see cref="SectorMaker.CreateAndPopulateSector"/> -
/// which steals any already-existing sidedef along the way from whatever
/// sector currently (even wrongly) owns it, exactly the repair this mode
/// exists for - then flips any linedef left with only a back side, UDB's
/// own exact follow-up.
/// </summary>
public sealed class MakeSectorCommand : ICommand
{
    private readonly MapData map;
    private readonly IReadOnlyList<LinedefSide> trace;
    private readonly List<Action> undoActions = new();

    public MakeSectorCommand(MapData map, IReadOnlyList<LinedefSide> trace)
    {
        this.map = map;
        this.trace = trace;
    }

    public void Do()
    {
        undoActions.Clear();

        var source = SectorMaker.FindMatchingSidedefInTrace(trace) ?? SectorMaker.FindOppositeSidedefInTrace(trace);
        var newSector = SectorMaker.CreateAndPopulateSector(map, undoActions, trace, source, nearbyLines: map.Linedefs);

        var backOnly = newSector.Sidedefs
            .Select(sd => sd.Linedef)
            .Where(l => l.Front == null && l.Back != null)
            .ToList();
        GeometryStitcher.FlipBackwardLinedefs(backOnly, undoActions);

        CreatedSector = newSector;
    }

    /// <summary>Set once <see cref="Do"/> has run - <c>MakeSectorOverlayHandler</c>'s own read-back, to start UDB's real post-commit white flash-fade (<c>MakeSectorMode</c>'s own <c>flashpolygon</c>) against the real, just-created sector.</summary>
    public Sector? CreatedSector { get; private set; }

    public void Undo()
    {
        for (var i = undoActions.Count - 1; i >= 0; i--) undoActions[i]();
    }
}
