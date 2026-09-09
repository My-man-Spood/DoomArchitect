using System.Numerics;

namespace DoomArchitect.Core.Map;

/// <summary>
/// Aggregate root for a single map. Owns every Vertex/Linedef/Sector and is
/// the only way to mutate them, so the vertex-linedef-sector adjacency
/// graph and the dirty-tracking on Sector can never drift out of sync.
/// </summary>
public sealed class MapData
{
    private readonly List<Vertex> _vertices = new();
    private readonly List<Linedef> _linedefs = new();
    private readonly List<Sector> _sectors = new();
    private readonly List<Thing> _things = new();

    public IReadOnlyList<Vertex> Vertices => _vertices;
    public IReadOnlyList<Linedef> Linedefs => _linedefs;
    public IReadOnlyList<Sector> Sectors => _sectors;
    public IReadOnlyList<Thing> Things => _things;

    public Vertex CreateVertex(Vector2 position)
    {
        var vertex = new Vertex(position);
        _vertices.Add(vertex);
        return vertex;
    }

    public Sector CreateSector(double floorHeight, double ceilingHeight)
    {
        var sector = new Sector(floorHeight, ceilingHeight);
        _sectors.Add(sector);
        return sector;
    }

    public Thing CreateThing(Vector2 position, int type)
    {
        var thing = new Thing(position, type);
        _things.Add(thing);
        return thing;
    }

    public Linedef CreateLinedef(Vertex start, Vertex end, Sector? front, Sector? back)
    {
        var linedef = new Linedef(start, end);

        if (front != null)
        {
            linedef.Front = new Sidedef(front, linedef);
            front.AddSidedef(linedef.Front);
        }

        if (back != null)
        {
            linedef.Back = new Sidedef(back, linedef);
            back.AddSidedef(linedef.Back);
        }

        start.AddLinedef(linedef);
        end.AddLinedef(linedef);
        _linedefs.Add(linedef);

        return linedef;
    }

    /// <summary>
    /// Moves a vertex, dirtying only the sectors of linedefs touching it -
    /// never the rest of the map.
    /// </summary>
    public void MoveVertex(Vertex vertex, Vector2 newPosition)
    {
        vertex.Position = newPosition;

        foreach (var linedef in vertex.Linedefs)
        {
            linedef.MarkAdjacentSectorsDirty();
        }
    }

    public IEnumerable<Sector> GetDirtySectors() => _sectors.Where(s => s.NeedsRebuild);

    public void ClearDirty(Sector sector) => sector.NeedsRebuild = false;

    public void MoveThing(Thing thing, Vector2 newPosition)
    {
        thing.Position = newPosition;
        thing.NeedsUpdate = true;
    }

    public IEnumerable<Thing> GetDirtyThings() => _things.Where(t => t.NeedsUpdate);

    public void ClearDirty(Thing thing) => thing.NeedsUpdate = false;

    // Selection - not undoable (view state, not document state). Kept as
    // one set of four repeated methods per type rather than generics or a
    // shared base, matching the same per-type-repeated convention already
    // established by the dirty-flag methods above.

    public void SelectOnly(Vertex vertex)
    {
        ClearSelectedVertices();
        vertex.IsSelected = true;
    }

    public void ToggleSelect(Vertex vertex) => vertex.IsSelected = !vertex.IsSelected;

    public void ClearSelectedVertices()
    {
        foreach (var vertex in _vertices) vertex.IsSelected = false;
    }

    public IEnumerable<Vertex> GetSelectedVertices() => _vertices.Where(v => v.IsSelected);

    public void SelectOnly(Linedef linedef)
    {
        ClearSelectedLinedefs();
        linedef.IsSelected = true;
    }

    public void ToggleSelect(Linedef linedef) => linedef.IsSelected = !linedef.IsSelected;

    public void ClearSelectedLinedefs()
    {
        foreach (var linedef in _linedefs) linedef.IsSelected = false;
    }

    public IEnumerable<Linedef> GetSelectedLinedefs() => _linedefs.Where(l => l.IsSelected);

    public void SelectOnly(Sector sector)
    {
        var previouslySelected = GetSelectedSectors().ToList();
        ClearSelectedSectorsRaw();
        sector.IsSelected = true;
        foreach (var previous in previouslySelected) ResyncSectorBoundarySelection(previous);
        ResyncSectorBoundarySelection(sector);
    }

    public void ToggleSelect(Sector sector)
    {
        sector.IsSelected = !sector.IsSelected;
        ResyncSectorBoundarySelection(sector);
    }

    public void ClearSelectedSectors()
    {
        var previouslySelected = GetSelectedSectors().ToList();
        ClearSelectedSectorsRaw();
        foreach (var sector in previouslySelected) ResyncSectorBoundarySelection(sector);
    }

    public IEnumerable<Sector> GetSelectedSectors() => _sectors.Where(s => s.IsSelected);

    /// <summary>
    /// The flag-only clear, deliberately without the boundary resync
    /// <see cref="ClearSelectedSectors"/> does. Used internally by
    /// <see cref="ConvertGeometrySelection"/>, which clears sector
    /// selection purely as bookkeeping *after* already deriving the
    /// correct target-type selection from it - resyncing there would
    /// stomp on linedef selection that was just correctly computed, since
    /// the resync reads sectors' current (about-to-be-false) state.
    /// </summary>
    private void ClearSelectedSectorsRaw()
    {
        foreach (var sector in _sectors) sector.IsSelected = false;
    }

    /// <summary>
    /// Recomputes every one of <paramref name="sector"/>'s bordering
    /// linedefs' selection to <c>(front sector selected) OR (back sector
    /// selected)</c> - a close port of UDB's own real <c>SectorsMode.
    /// SelectSector</c>, which runs this exact resync every time a
    /// sector's selection changes (not just on a mode-switch conversion,
    /// see <see cref="ConvertGeometrySelection"/>). Without it, a linedef
    /// selected as a side effect of converting into Sectors mode would
    /// stay stuck selected even after its sector is toggled off - since
    /// nothing else would ever clear it. The OR-across-both-sides rule
    /// matters for a two-sided linedef shared between two sectors: it
    /// should stay selected if either bordering sector is, not just the
    /// one that was just clicked.
    /// </summary>
    private static void ResyncSectorBoundarySelection(Sector sector)
    {
        foreach (var sidedef in sector.Sidedefs)
        {
            var linedef = sidedef.Linedef;
            var front = linedef.Front != null && linedef.Front.Sector.IsSelected;
            var back = linedef.Back != null && linedef.Back.Sector.IsSelected;
            linedef.IsSelected = front || back;
        }
    }

    public void SelectOnly(Thing thing)
    {
        ClearSelectedThings();
        thing.IsSelected = true;
    }

    public void ToggleSelect(Thing thing) => thing.IsSelected = !thing.IsSelected;

    public void ClearSelectedThings()
    {
        foreach (var thing in _things) thing.IsSelected = false;
    }

    public IEnumerable<Thing> GetSelectedThings() => _things.Where(t => t.IsSelected);

    /// <summary>
    /// Re-derives the Vertices/Linedefs/Sectors selection to match
    /// <paramref name="target"/>, exactly matching UDB's real
    /// <c>MapSet.ConvertSelection</c> (called by every classic mode's
    /// <c>OnEngage</c> on a mode switch, always converting from
    /// everything currently selected across all three types - the only
    /// case this codebase needs). Never touches Thing selection, which is
    /// independent (matches UDB). Not undoable, like every other
    /// selection operation - this is view state, not document state.
    /// </summary>
    public void ConvertGeometrySelection(GeometrySelectionType target)
    {
        switch (target)
        {
            case GeometrySelectionType.Vertices:
                ConvertToVertices();
                break;
            case GeometrySelectionType.Linedefs:
                ConvertToLinedefs();
                break;
            case GeometrySelectionType.Sectors:
                ConvertToSectors();
                break;
        }
    }

    /// <summary>
    /// Additive/preserved (UDB never clears vertex selection here): a
    /// vertex stays/becomes selected if it already was, is an endpoint of
    /// a selected linedef, or touches a linedef whose front-or-back
    /// sector is selected.
    /// </summary>
    private void ConvertToVertices()
    {
        foreach (var linedef in GetSelectedLinedefs().ToList())
        {
            linedef.Start.IsSelected = true;
            linedef.End.IsSelected = true;
        }

        var selectedSectors = GetSelectedSectors().ToHashSet();
        foreach (var vertex in _vertices)
        {
            if (vertex.Linedefs.Any(l =>
                    (l.Front != null && selectedSectors.Contains(l.Front.Sector)) ||
                    (l.Back != null && selectedSectors.Contains(l.Back.Sector))))
            {
                vertex.IsSelected = true;
            }
        }

        ClearSelectedLinedefs();
        ClearSelectedSectorsRaw();
    }

    /// <summary>
    /// Additive/preserved: a linedef stays/becomes selected if it already
    /// was, both its vertices were selected, or it belongs to any sidedef
    /// of a selected sector.
    /// </summary>
    private void ConvertToLinedefs()
    {
        var selectedVertices = GetSelectedVertices().ToHashSet();
        foreach (var linedef in _linedefs)
        {
            if (selectedVertices.Contains(linedef.Start) && selectedVertices.Contains(linedef.End))
            {
                linedef.IsSelected = true;
            }
        }

        foreach (var sector in GetSelectedSectors().ToList())
        {
            foreach (var sidedef in sector.Sidedefs) sidedef.Linedef.IsSelected = true;
        }

        ClearSelectedVertices();
        ClearSelectedSectorsRaw();
    }

    /// <summary>
    /// The one non-additive case: linedef/vertex selection is cleared and
    /// rebuilt from scratch as exactly the selected sectors' borders. A
    /// sector qualifies iff every linedef bordering it - across every
    /// sidedef it has, loops/holes included, with no special treatment -
    /// is in the working "selected, or both endpoints selected" linedef
    /// set. Final sector selection is (already selected) OR (qualifies) -
    /// the one place prior selection is preserved rather than replaced.
    /// </summary>
    private void ConvertToSectors()
    {
        var selectedVertices = GetSelectedVertices().ToHashSet();
        var workingLinedefSelection = GetSelectedLinedefs().ToHashSet();
        foreach (var linedef in _linedefs)
        {
            if (selectedVertices.Contains(linedef.Start) && selectedVertices.Contains(linedef.End))
            {
                workingLinedefSelection.Add(linedef);
            }
        }

        var selectedSectors = GetSelectedSectors().ToHashSet();

        ClearSelectedLinedefs();
        ClearSelectedVertices();

        foreach (var sector in _sectors)
        {
            var qualifies = sector.Sidedefs.Count > 0
                && sector.Sidedefs.All(sd => workingLinedefSelection.Contains(sd.Linedef));
            sector.IsSelected = selectedSectors.Contains(sector) || qualifies;

            if (sector.IsSelected)
            {
                foreach (var sidedef in sector.Sidedefs) sidedef.Linedef.IsSelected = true;
            }
        }
    }
}
