using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Undo;

/// <summary>
/// Linedefs mode's Flip action - UDB's real <c>LinedefsMode.FlipLinedefs</c>
/// (<c>ClassicModes/LinedefsMode.cs</c>). The exact same action is also
/// bound in UDB's own <c>SectorsMode.cs</c>, but there it flips every
/// wrong-facing line around whichever sector is selected - a distinct,
/// bulkier operation, not ported here; this is Linedefs mode's own
/// per-selection version only.
/// </summary>
public sealed class FlipLinedefsCommand : ICommand
{
    private readonly IReadOnlyList<Linedef> linedefs;
    private readonly List<Action> undoActions = new();

    public FlipLinedefsCommand(IReadOnlyList<Linedef> linedefs)
    {
        this.linedefs = linedefs;
    }

    public void Do()
    {
        undoActions.Clear();
        GeometryStitcher.FlipLinedefs(linedefs, undoActions);
    }

    public void Undo()
    {
        for (var i = undoActions.Count - 1; i >= 0; i--) undoActions[i]();
    }
}
