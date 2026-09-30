# Dissolve action (Vertices/Linedefs) - the "safer delete"

**Status:** Done  
**Area:** 2D editing

UDB's `dissolveitem` action - bound to its own key (`Backspace`, matching
UDB's own real default from `UDBuilder.default.cfg`, cross-checked
against its known Ctrl+Z/Ctrl+Y undo/redo encoding), separate from
`deleteitem` (`Delete`) - tries to remove geometry without leaving the
damage plain Delete can (an orphaned wall, a torn-open sector boundary).
Full faithful port, done 2026-09-30, after scoping and researching the
real UDB source first (see the original scoping section below, kept for
the record) - it turned out to be a genuinely large port, on the order of
the entire original Delete effort.

## What UDB actually does (verified against source)

- **Sectors mode has no real Dissolve at all** -
  `SectorsMode.DissolveItem` is literally `{ DeleteItem(); }`. This
  project's `MapOverlay.DissolveSelection()` reuses `DeleteSectorsCommand`
  directly for Sectors mode - no separate command exists for it.
- **Vertices mode's `DissolveItem`** recurses: a selected vertex with
  exactly two linedefs (checked against a snapshot of every selected
  vertex's line count taken *before* anything is touched - UDB's own
  exact rule) either:
  - joins the two sectors on either side of a *third* line, when its two
    far vertices are already directly connected by one (merging would
    otherwise collapse a 3-sided sector into an invalid 2-sided sliver) -
    then just removes this vertex's own two lines outright, no merge; or
  - merges its two lines into one, retargeting one onto the other's far
    vertex and recursing into that far vertex if it's *also* selected
    with exactly two (live-counted, not snapshotted - this one detail is
    genuinely different from the outer check) lines of its own, chaining
    through as many consecutive selected, collinear-ish vertices as are
    lined up in a row. Once a chain bottoms out, the final span is
    rebuilt as a brand-new edge and resolved exactly like a freshly drawn
    one (own new `DissolveVerticesCommand.ResolveNewEdgeSides`, a
    single-edge-scoped version of `DrawLoopCommand`'s own
    `ResolveInteriorSide`/`ResolveExteriorSide`) - not kept as whatever
    the first line in the chain happened to already have, since its real
    final sectors/textures may have changed underneath it via any
    sector-joins earlier in the chain.
- **Linedefs mode's `DissolveItem`** joins the two different sectors a
  selected linedef separates (`GeometryStitcher.JoinSectors`, keeping
  whichever has the bigger bounding-box area - `SectorBounds.Area`,
  itself just a small addition to the project's existing `SectorBounds`)
  before removing it, and separately tracks (by bounding-box center,
  taken up front) any sector that had fewer than 4 sidedefs to begin
  with; once every selected line is gone, any of those that ended up
  with 1-2 sidedefs left (genuinely too small to be a real polygon) is
  discarded and a fresh sector traced from scratch at that same point
  (`BoundaryTracer.FindPotentialSectorAt(MapData, Vector2)` - a new
  point-based overload, exactly UDB's own real
  `Tools.FindPotentialSectorAt(Vector2D)`: find the nearest linedef,
  determine which side, defer to the existing linedef-based overload +
  `SectorMaker.CreateAndPopulateSector`, UDB's own real
  `MergeInvalidSectors`/`MakeSector`) rather than left as a sliver. If no
  valid boundary can be traced there anymore, the point is just left
  without a sector - matching UDB's own graceful "trace failed, nothing
  happens" case exactly (covered by its own test, not left unverified).

## Real infrastructure this needed (not just two new command classes)

Following this project's own "one parser per format... reconsider once a
second real consumer arrives" precedent (see `TODO/architecture-notes.md`),
applied here to sector-creation orchestration instead of text parsing:

- **`Core.Geometry.SectorMaker`** (new) - `CreateAndPopulateSector`,
  `AttachOrRetargetSidedefTracked`, `FindMatchingSidedefInTrace`,
  `FindOppositeSidedefInTrace` extracted out of `DrawLoopCommand` (which
  now calls the shared versions instead) - Dissolve's own repair step
  needed exactly this same "turn a traced boundary into a real sector"
  logic outside any draw session.
- **`GeometryStitcher.JoinSectors`** (new) - UDB's real `Sector.Join`:
  reassign every one of a sector's own sidedefs onto another, then remove
  it. Shared by both `DissolveVerticesCommand`'s own `TryJoinSectors` and
  `DissolveLinedefsCommand`.
- **`GeometryStitcher.DetachSectorSidedefs`** (new) - the "detach every
  sidedef, remove the sector" half of UDB's real `Sector.Dispose()`
  cascade, extracted out of `DeleteSectorsCommand` (refactored to use it)
  once `DissolveLinedefsCommand`'s own repair step needed the identical
  operation.
- **`BoundaryTracer.FindPotentialSectorAt(MapData, Vector2)`** (new
  overload) - the point-based entry described above.
- **`SectorBounds.Area`** (new property) - UDB's own real bbox-area
  comparison, added to the sector-bounds primitive this project already
  had (originally built for tag-label anchoring).

One real, deliberate fix over UDB's own literal source, not a
reproduction: `MergeLines`'s own v2-branch picks "whichever of v2's two
remaining lines isn't ld2" - but `ld2` has already been disposed and
detached from v2 by that point, so the comparison can never actually
match anything; UDB's real behavior only comes out correct in practice
because a freshly retargeted line happens to land at the end of v2's own
internal list. Ported as the obviously-intended check instead (whichever
of the two isn't the line that's actually still attached, i.e. compare
against `ld1`) rather than reproducing a comparison that can never be
true.

12 new tests across both commands, including the recursive chain-merge
case, the `TryJoinSectors` degenerate-triangle guard, the invalid-sector
repair's graceful-failure path, and full Undo round-trips for all three.

---

## Original scoping (2026-09-30, before implementation - kept for the record)

Scoped and researched against the real UDB source before writing any
code, because it turned out to be much bigger than Delete was, not a
small variant of it. Three options were laid out: (1) a full faithful
port, including extracting the draw-pipeline/boundary-tracing machinery
so it's shared rather than duplicated; (2) a scoped-down version
capturing the intent without the exact mechanism; (3) defer entirely.
Option 1 was chosen and is what's described above.
