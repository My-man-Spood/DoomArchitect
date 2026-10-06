using System.Linq;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Undo;

/// <summary>
/// Vertices mode's Dissolve action - UDB's real
/// <c>VerticesMode.DissolveItem</c>/<c>MergeLines</c>/<c>TryJoinSectors</c>
/// (<c>ClassicModes/VerticesMode.cs</c>), the gentler alternative to
/// <see cref="DeleteVerticesCommand"/>'s own blunt cascade. A vertex with
/// exactly two linedefs attached (checked against the *original*, pre-Do
/// count, snapshotted upfront - UDB's own exact rule, matters when one
/// selected vertex's own processing changes another's live count) tries a
/// smart merge instead of a plain retarget:
/// <list type="bullet">
/// <item>if its two far vertices are *already* directly connected by some
/// third line (merging would collapse a 3-sided sector into an invalid
/// 2-sided sliver), that third line's own two sectors are joined instead
/// (<see cref="GeometryStitcher.JoinSectors"/>) and this vertex's own two
/// lines are simply removed - no merge attempted;</item>
/// <item>otherwise, the merge recurses: retargeting one line onto the
/// other's far vertex can itself leave *that* far vertex - if it's also
/// selected - with exactly two lines of its own, chaining through as many
/// consecutive selected, collinear-ish vertices as are lined up in a row.
/// Once a chain bottoms out, the final merged span isn't kept as whatever
/// sidedef data the first line in the chain happened to have - it's
/// rebuilt from scratch as a genuinely new edge, resolved the same way a
/// freshly drawn one would be (<see cref="ResolveNewEdgeSides"/>), so its
/// final sectors/textures reflect whatever the map actually looks like
/// after every join/merge in the chain, not stale pre-dissolve data.</item>
/// </list>
/// Any other vertex (0, 1, or 3+ lines) falls back to
/// <see cref="DeleteVerticesCommand"/>'s own plain cascade-removal.
///
/// One real, deliberate fix over UDB's own literal source: <c>MergeLines</c>'s
/// own v2-branch picks "whichever of v2's two remaining lines isn't ld2"
/// - but by that point <c>ld2</c> has already been disposed and detached
/// from v2, so that comparison can never actually match anything; UDB's
/// real behavior only comes out correct in practice because a freshly
/// retargeted line happens to land at the end of v2's own internal list.
/// Ported here as the obviously-intended check instead (whichever of the
/// two isn't the line that's actually still attached) rather than
/// reproducing a comparison that can never be true.
/// </summary>
public sealed class DissolveVerticesCommand : ICommand
{
    private readonly MapData map;
    private readonly IReadOnlyList<Vertex> vertices;
    private readonly List<Action> undoActions = new();

    public DissolveVerticesCommand(MapData map, IReadOnlyList<Vertex> vertices)
    {
        this.map = map;
        this.vertices = vertices;
    }

    public void Do()
    {
        undoActions.Clear();

        // Snapshotted once, before any mutation - UDB's own exact rule.
        var originalLineCount = vertices.ToDictionary(v => v, v => v.Linedefs.Count);
        var selected = new HashSet<Vertex>(vertices);

        foreach (var vertex in vertices)
        {
            if (!map.Vertices.Contains(vertex)) continue;

            if (originalLineCount[vertex] == 2)
            {
                var ld1 = vertex.Linedefs[0];
                var ld2 = vertex.Linedefs[1];
                var v1 = ld1.Start == vertex ? ld1.End : ld1.Start;
                var v2 = ld2.Start == vertex ? ld2.End : ld2.Start;

                var dontMerge = false;
                foreach (var l in v1.Linedefs.ToList())
                {
                    if (l == ld2) continue;
                    if (l.Start == v2 || l.End == v2)
                    {
                        TryJoinSectors(l);
                        dontMerge = true;
                        break;
                    }
                }

                if (dontMerge) RemoveAllAttachedLinedefs(vertex);
                else MergeLines(selected, ld1, ld2, vertex);
            }
            else
            {
                RemoveAllAttachedLinedefs(vertex);
            }

            map.RemoveVertex(vertex);
            undoActions.Add(() => map.RestoreVertex(vertex));
        }
    }

    public void Undo()
    {
        for (var i = undoActions.Count - 1; i >= 0; i--) undoActions[i]();
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

    private void TryJoinSectors(Linedef linedef)
    {
        if (!map.Linedefs.Contains(linedef)) return;
        if (linedef.Front == null || linedef.Back == null || linedef.Front.Sector == linedef.Back.Sector) return;

        var frontSector = linedef.Front.Sector;
        var backSector = linedef.Back.Sector;
        if (SectorBounds.Compute(frontSector).Area > SectorBounds.Compute(backSector).Area)
            GeometryStitcher.JoinSectors(map, backSector, frontSector, undoActions);
        else
            GeometryStitcher.JoinSectors(map, frontSector, backSector, undoActions);
    }

    /// <summary>Retargets <paramref name="ld1"/> onto <paramref name="ld2"/>'s far vertex and drops <paramref name="ld2"/>, recursing through either end if it's also a selected two-line vertex - see the class remarks for the full algorithm.</summary>
    private void MergeLines(HashSet<Vertex> selected, Linedef ld1, Linedef ld2, Vertex v)
    {
        var v1 = ld1.Start == v ? ld1.End : ld1.Start;
        var v2 = ld2.Start == v ? ld2.End : ld2.Start;
        var ld1WasStart = ld1.Start == v;

        v.RemoveLinedef(ld1);
        v2.AddLinedef(ld1);
        if (ld1WasStart) ld1.Start = v2; else ld1.End = v2;
        ld1.MarkAdjacentSectorsDirty();

        map.RemoveLinedef(ld2, undoActions);
        undoActions.Add(() =>
        {
            map.RestoreLinedef(ld2);
            v2.RemoveLinedef(ld1);
            if (ld1WasStart) ld1.Start = v; else ld1.End = v;
            v.AddLinedef(ld1);
            ld1.MarkAdjacentSectorsDirty();
        });

        var redraw = true;

        if (map.Vertices.Contains(v2) && selected.Contains(v2) && v2.Linedefs.Count == 2)
        {
            var lines = v2.Linedefs;
            var other = lines[0] == ld1 ? lines[1] : lines[0];

            MergeLines(selected, ld1, other, v2);
            map.RemoveVertex(v2);
            undoActions.Add(() => map.RestoreVertex(v2));
            redraw = false;
        }

        if (map.Vertices.Contains(v1) && selected.Contains(v1) && v1.Linedefs.Count == 2)
        {
            var lines = v1.Linedefs;
            var other = lines[0] == ld1 ? lines[1] : lines[0];

            MergeLines(selected, other, ld1, v1);
            map.RemoveVertex(v1);
            undoActions.Add(() => map.RestoreVertex(v1));
            redraw = false;
        }

        // Neither branch recursed - this is the chain's true final span,
        // so (and only so) it gets rebuilt as a genuinely new edge rather
        // than kept as whatever ld1 happened to carry pre-merge. A branch
        // that *did* recurse already fully retargeted/disposed ld1 deeper
        // in the call - nothing left to do at this level.
        if (redraw)
        {
            var start = ld1.Start;
            var end = ld1.End;

            map.RemoveLinedef(ld1, undoActions);
            undoActions.Add(() => map.RestoreLinedef(ld1));

            var newLinedef = map.CreateLinedef(start, end, null, null);
            undoActions.Add(() => map.RemoveLinedef(newLinedef));

            ResolveNewEdgeSides(newLinedef);
        }
    }

    /// <summary>
    /// Resolves a single brand-new edge's own two sides exactly like a
    /// freshly drawn one would - <c>DrawLoopCommand</c>'s own
    /// <c>ResolveInteriorSide</c>/<c>ResolveExteriorSide</c>, scoped down
    /// to one edge with no surrounding loop-session context (no
    /// <c>splittingOnly</c> ambiguity, no other new edges an exterior
    /// join could wrongly "steal" from - there's only ever this one).
    /// </summary>
    private void ResolveNewEdgeSides(Linedef linedef)
    {
        var frontInterior = BoundaryTracer.DetermineFrontInterior(map, linedef);
        ResolveInteriorSide(linedef, frontInterior);
        ResolveExteriorSide(linedef, !frontInterior);
    }

    private void ResolveInteriorSide(Linedef linedef, bool front)
    {
        if ((front ? linedef.Front : linedef.Back) != null) return;

        var trace = BoundaryTracer.FindPotentialSectorAt(map, linedef, front);
        if (trace == null) return;

        var matching = SectorMaker.FindMatchingSidedefInTrace(trace);
        SectorMaker.CreateAndPopulateSector(map, undoActions, trace, matching ?? SectorMaker.FindOppositeSidedefInTrace(trace), map.Linedefs);
    }

    private void ResolveExteriorSide(Linedef linedef, bool front)
    {
        if ((front ? linedef.Front : linedef.Back) != null) return;

        var trace = BoundaryTracer.FindPotentialSectorAt(map, linedef, front);
        if (trace == null) return;

        var target = SectorMaker.FindMatchingSidedefInTrace(trace);
        if (target == null) return;

        foreach (var side in trace)
        {
            if (side.Linedef != linedef) continue;
            SectorMaker.AttachOrRetargetSidedefTracked(map, undoActions, side.Linedef, side.Front, target.Sector);
        }
    }
}
