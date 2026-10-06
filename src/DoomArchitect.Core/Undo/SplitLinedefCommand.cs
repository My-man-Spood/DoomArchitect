using System.Numerics;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Undo;

/// <summary>
/// Standalone counterpart to the split step <see cref="DrawLoopCommand"/>
/// performs internally as part of a larger drawn loop - Vertices mode's
/// right-click-on-a-linedef action: inserts a new vertex mid-line with
/// no drawing session attached at all, just
/// <see cref="MapData.SplitLinedef"/> plus its own undo.
/// </summary>
public sealed class SplitLinedefCommand : ICommand
{
    private readonly MapData map;
    private readonly Linedef linedef;
    private readonly Vector2 position;

    private Vertex? vertex;
    private Linedef? newHalf;
    private Vertex? originalEnd;

    public SplitLinedefCommand(MapData map, Linedef linedef, Vector2 position)
    {
        this.map = map;
        this.linedef = linedef;
        this.position = position;
    }

    /// <summary>The vertex this command creates - only meaningful after <see cref="Do"/> has run.</summary>
    public Vertex CreatedVertex => vertex!;

    public void Do()
    {
        vertex = map.CreateVertex(position);
        originalEnd = linedef.End;
        newHalf = map.SplitLinedef(linedef, vertex);
    }

    public void Undo()
    {
        // RemoveLinedef below can orphan originalEnd (if the split line's
        // far endpoint had no other linedef of its own - a dead-end wall)
        // and auto-remove it from map.Vertices - RestoreVertex here is a
        // no-op in the common case, and correctly undoes that in the
        // dead-end one, before the reattachment three lines down would
        // otherwise leave it referenced but missing from the map's own
        // vertex list.
        map.RemoveLinedef(newHalf!);
        vertex!.RemoveLinedef(linedef);
        linedef.End = originalEnd!;
        map.RestoreVertex(originalEnd!);
        originalEnd!.AddLinedef(linedef);
        map.RemoveVertex(vertex);
    }
}
