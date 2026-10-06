# Auto-remove orphaned vertices when a linedef is removed

**Status:** Done
**Area:** Geometry

Reported live: right-click-creating an isolated 4-vertex sector in
Vertices mode, switching to Sectors mode, hovering it and pressing
Delete removed the sector and its boundary walls but left all 4
vertices behind - floating, unconnected to anything.

Both `DeleteSectorsCommand` and `DeleteLinedefsCommand`'s own doc
comments claimed this matched real UDB ("no vertex cleanup, exactly
like real UDB"). Confirmed wrong by testing real UDB directly
(Windows) - it fully wipes the vertices too. Re-checked UDB's own
source (a shallow clone, not guessed) to find out why the original
claim was wrong:

- `SectorsMode.DeleteItem`/`LinedefsMode.DeleteItem` themselves
  genuinely never touch vertices - that part was accurate.
- The real mechanism lives one level deeper, in the data model itself:
  `Vertex.DetachLinedefP` (`Source/Core/Map/Vertex.cs:178-191`) - called
  from `Linedef.Dispose()` whenever a linedef is actually destroyed -
  checks `if ((linedefs.Count == 0) && map.AutoRemove) this.Dispose();`.
  A vertex auto-disposes the instant its own linedef count hits zero.
  This is unconditional and automatic - every mode/action gets it for
  free, because it's baked into `Vertex`/`Linedef` themselves, not
  reimplemented per action.
- `MapSet.AutoRemove` defaults to `true` in both constructors - always
  on during normal editing. It's only ever turned off around specific
  delicate operations that need a vertex to survive a *transient*
  zero-linedef moment mid-rewire: `Linedef.FlipVertices()` (around its
  own start/end swap) and `UndoManager.PlaybackStream` (for the entire
  duration of an undo/redo replay, since a multi-step recorded stream
  can pass through transient zero-linedef states before a later step in
  the same replay reattaches something).

This project's own `MapData`/`Vertex` already mirrors UDB's structure
closely - it just never ported this one cascade. Also worth being
honest about: UDB's own undo mechanism (a global, automatic, byte-level
diff/snapshot recorder - see `TODO/editing-undo-redo-stack.md`'s own
remarks on why this project deliberately didn't port it literally) is
*why* this cascade comes free for every UDB action - `Vertex.Dispose()`
itself unconditionally calls `General.Map.UndoRedo.RecRemVertex(this)`.
This project's command-based `undoActions`-list pattern has no
equivalent global recorder, so there's no literal UDB code to port for
the undo-bookkeeping side of this fix - only the cascade *behavior*
itself is what's being matched.

## What was built

- **`MapData.RemoveLinedef`** gained the cascade: after detaching a
  linedef from both endpoints, either one left with zero linedefs of
  its own is removed too. Takes an optional `List<Action>? undoActions = null`
  so a caller that wants undo coverage gets a restore closure appended;
  every call site inside an undo closure itself (undoing a create, not
  a `Do()`-time removal) is safe left as the default `null` - redo in
  this project is just `Do()` run fresh (`UndoStack.Redo` calls
  `command.Do()`, it doesn't replay a "redo list"), so nothing
  downstream ever needs to "undo" that specific auto-cleanup once a
  command's own `Undo()` closures finish running.
- **`MapData.RestoreVertex`** made idempotent (no-op if already
  present) - the one change that makes the whole design safe regardless
  of ordering. Several existing commands (`DeleteVerticesCommand.MergeTwoLinedefs`,
  `DissolveVerticesCommand.MergeLines`) already do their *own* explicit
  vertex removal/restoration right after detaching a vertex's last
  linedef - with the new cascade potentially *also* firing for that
  same vertex moments earlier, two independent restore closures for it
  could otherwise leave it listed twice after an undo (`List<T>` allows
  duplicates, it wouldn't just reject the second add).
- **`MapData.CreateLinedef`** now also `RestoreVertex`s both of its own
  endpoints first (a no-op for the overwhelming majority of calls). A
  real case the new cascade introduces: `DissolveVerticesCommand.MergeLines`'s
  own "redraw" branch removes an edge and immediately creates a
  replacement connecting the *same* two vertex objects - if one of them
  had just been auto-removed, the new edge would otherwise attach to a
  vertex silently missing from `map.Vertices`.
- **`SplitLinedefCommand.Undo`** (the one caller with no `undoActions`
  list at all - a single hand-coded `Undo()`, not the list-of-closures
  pattern) needed its own explicit fix: one new defensive
  `map.RestoreVertex(originalEnd)` call, for the case where the split
  line's far endpoint was itself a dead-end (no other linedef) - caught
  by, not just guessed at by, `SplitLinedefCommandTests.Undo_FullyRestoresTheOriginalLinedef`'s
  existing `map.Vertices.Count` assertion.
- Every other `RemoveLinedef`/`JoinLinedefs` call site across
  `DeleteSectorsCommand`/`DeleteLinedefsCommand`/`DissolveLinedefsCommand`/
  `DissolveVerticesCommand`/`DeleteVerticesCommand`/`GeometryStitcher`/
  `DrawLoopCommand` updated to pass its own already-in-scope
  `undoActions` list through.
- Two existing tests were asserting the actual bug as intended
  behavior (`DeleteLinedefsCommandTests.Do_RemovesTheLinedefButLeavesItsVerticesAlone`,
  `DeleteSectorsCommandTests.Do_IsolatedSector_RemovesTheSectorAndEveryOneSidedBoundaryWall`'s
  own `Assert.Equal(4, map.Vertices.Count)` with a "no vertex cascade"
  comment) - renamed and flipped, not just left to fail.

## Verification

- `dotnet build DoomArchitect.sln` and
  `dotnet test src/DoomArchitect.Core.Tests/DoomArchitect.Core.Tests.csproj`
  (1063 tests - 5 new, covering the direct cascade on `MapData.RemoveLinedef`,
  the shared-vertex negative case, undo restoring orphaned vertices, and
  the `RestoreVertex` double-call idempotency guarantee the whole design
  leans on).
- Manual pass: the exact reported repro (right-click an isolated sector
  in Vertices mode, switch to Sectors mode, hover + Delete) - all 4
  vertices should now disappear along with the sector; Ctrl+Z should
  bring all of it back (sector, walls, vertices) in one step. Also
  spot-check that deleting one wall of a still-connected room (shared
  vertices survive) and dissolving a vertex mid-chain still behave
  exactly as before.
