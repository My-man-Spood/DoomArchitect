using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Undo;

/// <summary>
/// Vertices mode's Delete action - UDB's real <c>VerticesMode.DeleteItem</c>
/// (<c>ClassicModes/VerticesMode.cs</c>), ported exactly: a vertex with
/// precisely two linedefs attached has them merged into one (the first
/// retargeted onto the second's far vertex, the second discarded) before
/// being removed, so deleting a vertex mid-wall collapses the wall into a
/// single edge rather than leaving a gap; any other vertex (0, 1, or 3+
/// linedefs) has every one of its remaining linedefs fully removed too -
/// UDB's <c>Vertex.Dispose()</c> cascade (a linedef cannot exist without
/// two vertices) - which can genuinely tear open a sector's boundary at a
/// junction vertex. That bluntness is UDB's real "Delete" behavior, not a
/// bug; UDB's own gentler <c>DissolveItem</c> (tries to avoid breaking
/// sectors, preserves texture alignment across the merge) is a distinct,
/// not-yet-ported action - see TODO/TODO.md.
///
/// One command regardless of how many vertices are deleted, matching this
/// project's "single Undo step for a whole gesture" convention - not a
/// <see cref="CommandGroup"/> of one-per-vertex commands, since deleting
/// one selected vertex can change another still-pending selected vertex's
/// own linedef count (e.g. two adjacent selected vertices sharing a line)
/// and the two need to see each other's effects exactly like UDB's own
/// single sequential loop does.
/// </summary>
public sealed class DeleteVerticesCommand : ICommand
{
    private readonly MapData map;
    private readonly IReadOnlyList<Vertex> vertices;
    private readonly List<Action> undoActions = new();

    public DeleteVerticesCommand(MapData map, IReadOnlyList<Vertex> vertices)
    {
        this.map = map;
        this.vertices = vertices;
    }

    public void Do()
    {
        undoActions.Clear();

        foreach (var vertex in vertices)
        {
            // A vertex already removed as a side effect of an earlier one
            // in this same selection isn't possible today (removing a
            // vertex only ever removes *linedefs*, never other vertices -
            // see the class remarks), but this guard costs nothing and
            // mirrors UDB's own defensive "Not already removed
            // automatically?" check exactly.
            if (!map.Vertices.Contains(vertex)) continue;

            if (vertex.Linedefs.Count == 2) MergeTwoLinedefs(vertex);
            else RemoveAllAttachedLinedefs(vertex);

            map.RemoveVertex(vertex);
            undoActions.Add(() => map.RestoreVertex(vertex));
        }
    }

    public void Undo()
    {
        for (var i = undoActions.Count - 1; i >= 0; i--) undoActions[i]();
    }

    /// <summary>Retargets <c>keep</c> onto <c>discard</c>'s far vertex and drops <c>discard</c> - UDB's own arbitrary-but-deterministic "first two attached, in order" pick (<c>GetByIndex(v.Linedefs, 0/1)</c>).</summary>
    private void MergeTwoLinedefs(Vertex vertex)
    {
        var keep = vertex.Linedefs[0];
        var discard = vertex.Linedefs[1];
        var farVertex = discard.Start == vertex ? discard.End : discard.Start;
        var keepWasStart = keep.Start == vertex;

        vertex.RemoveLinedef(keep);
        farVertex.AddLinedef(keep);
        if (keepWasStart) keep.Start = farVertex; else keep.End = farVertex;
        keep.MarkAdjacentSectorsDirty();

        map.RemoveLinedef(discard, undoActions);

        undoActions.Add(() =>
        {
            map.RestoreLinedef(discard);

            farVertex.RemoveLinedef(keep);
            if (keepWasStart) keep.Start = vertex; else keep.End = vertex;
            map.RestoreVertex(vertex);
            vertex.AddLinedef(keep);
            keep.MarkAdjacentSectorsDirty();
        });
    }

    private void RemoveAllAttachedLinedefs(Vertex vertex)
    {
        foreach (var linedef in vertex.Linedefs.ToList())
        {
            linedef.MarkAdjacentSectorsDirty();
            map.RemoveLinedef(linedef, undoActions);
            undoActions.Add(() =>
            {
                map.RestoreLinedef(linedef);
                linedef.MarkAdjacentSectorsDirty();
            });
        }
    }
}
