using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Undo;

/// <summary>
/// Linedefs mode's Delete action - UDB's real <c>LinedefsMode.DeleteItem</c>
/// (<c>ClassicModes/LinedefsMode.cs</c>), which really is this simple: just
/// removes each selected linedef, full stop. No vertex left with zero
/// remaining linedefs is cleaned up, and no sector left missing one of its
/// walls is repaired - both are simply left as-is, exactly like real UDB
/// (its own gentler <c>DissolveItem</c>, which does try to join the
/// sectors on either side back together, is a distinct, not-yet-ported
/// action - see TODO/TODO.md).
/// </summary>
public sealed class DeleteLinedefsCommand : ICommand
{
    private readonly MapData map;
    private readonly IReadOnlyList<Linedef> linedefs;
    private readonly List<Action> undoActions = new();

    public DeleteLinedefsCommand(MapData map, IReadOnlyList<Linedef> linedefs)
    {
        this.map = map;
        this.linedefs = linedefs;
    }

    public void Do()
    {
        undoActions.Clear();

        foreach (var linedef in linedefs)
        {
            linedef.MarkAdjacentSectorsDirty();
            map.RemoveLinedef(linedef);
            undoActions.Add(() =>
            {
                map.RestoreLinedef(linedef);
                linedef.MarkAdjacentSectorsDirty();
            });
        }
    }

    public void Undo()
    {
        for (var i = undoActions.Count - 1; i >= 0; i--) undoActions[i]();
    }
}
