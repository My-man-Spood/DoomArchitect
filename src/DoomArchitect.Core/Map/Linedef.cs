namespace DoomArchitect.Core.Map;

public sealed class Linedef
{
    internal Linedef(Vertex start, Vertex end)
    {
        Start = start;
        End = end;
    }

    public Vertex Start { get; internal set; }
    public Vertex End { get; internal set; }

    /// <summary>Null on a one-sided linedef (a wall with nothing behind it).</summary>
    public Sidedef? Front { get; internal set; }

    /// <summary>Null on a one-sided linedef (a wall with nothing behind it).</summary>
    public Sidedef? Back { get; internal set; }

    /// <summary>
    /// The real, named UDMF <c>twosided</c> concept - "has two sides,"
    /// independent of just having a back sidedef index (confirmed real,
    /// against the base UDMF spec, and against Ultimate Doom Builder's
    /// own real source and config - <c>ZDoom_misc.cfg</c>'s own
    /// <c>linedefflags_udmf</c> list names it
    /// <c>twosided = "Doublesided";</c>, the very first entry, right
    /// alongside "Impassable"/"Block monsters"/etc. - a real, genuinely
    /// user-editable checkbox in UDB's own Linedef Edit dialog, not a
    /// value hidden from the mapper). <see cref="IO.UdmfWriter"/>'s own
    /// remarks have the bug this exists to fix - GZDoom's own node-
    /// building trusts this field over inferring two-sidedness from
    /// <see cref="Back"/> alone.
    ///
    /// Stored, matching UDB's own <c>Linedef.ApplySidedFlags</c> (which
    /// sets this exact condition, explicitly, at every site across its
    /// own codebase that attaches/detaches a sidedef) - *not* computed
    /// from <see cref="Front"/>/<see cref="Back"/> on the fly. A real,
    /// deliberate departure from UDB was tried here first (compute it
    /// fresh, so it can never drift) and corrected after the user
    /// pointed out why that's wrong for this specific field: since a
    /// mapper can genuinely set this independent of the real sidedef
    /// state (confirmed by the checkbox above), a disagreement between
    /// this flag and Front/Back being null isn't a bug to silently
    /// paper over - it's exactly the real, reviewable map issue UDB's
    /// own dedicated checks (<c>ResultLineNotDoubleSided</c>/
    /// <c>ResultLineNotSingleSided</c>) exist to surface to the mapper.
    /// Kept in sync automatically for every *normal* edit via
    /// <see cref="Map.MapData"/>'s own handful of real mutation sites
    /// (far fewer here than UDB's own ~25, since this project's model
    /// is more consolidated) - same as UDB, a mapper can still end up
    /// with a genuine mismatch through unusual editing, and that's a
    /// real state to preserve and eventually flag, not to hide.
    /// </summary>
    public bool TwoSided { get; internal set; }

    /// <summary>
    /// Set whenever this linedef's selection state changes. Not undoable -
    /// selection is view state, not document state.
    /// </summary>
    public bool IsSelected { get; internal set; }

    /// <summary>
    /// UDMF fields recognized by the format but not modeled as a typed
    /// property here (e.g. <c>special</c>/<c>arg0..arg4</c>/flags/tags) -
    /// preserved so a load-then-save round-trip doesn't lose them, even
    /// though nothing can interpret or edit them yet.
    /// </summary>
    public UniFields Fields { get; } = new();

    /// <summary>
    /// Dirties the sector(s) on this linedef's own sides - at most two,
    /// which is the entire blast radius of moving one of its endpoints.
    /// </summary>
    internal void MarkAdjacentSectorsDirty()
    {
        if (Front != null) Front.Sector.NeedsRebuild = true;
        if (Back != null) Back.Sector.NeedsRebuild = true;
    }
}
