# Drawing mode (Draw Lines)

**Status:** Done  
**Area:** 2D editing

- [x] Drawing mode (UDB's real "Draw Lines" mode) - **Phases 1+2 done
      2026-09-17**, geometry-stitching rewritten 1:1 against UDB's real
      source **2026-09-19** (see that entry below for the full writeup);
      **Phase 3 (auto-close-across-geometry specifically, plus cardinal-
      direction snap/continuous drawing/rubber-band polish) not started**.
      User's own explicit scope call
      is full UDB parity eventually ("every possible way to draw lines,
      vectors, sectors... ported basically exactly as it is in UDB"),
      phased rather than all at once - offered a further phase split for
      Phase 2 itself (mechanical snap/split vs. the much larger join/
      inheritance half) and explicitly chose to build all of it now rather
      than defer. Planned against several research passes against UDB's
      real source (`DrawGeometryMode`/`Tools.DrawLines`/`FindClosestPath`/
      `FindPotentialSectorAt`/`MakeSector`/`JoinSector`/`Linedef.Split`/
      `EarClipPolygon`/`LinedefTracePath`) - full design in (while it still
      exists) `/home/spood/.claude/plans/steady-bubbling-gosling.md`.

      **Phase 1**: `EditMode.Draw` + `DrawOverlayHandler` - click to place
      points, click back near the first one to close the loop and commit
      a sector (or cancel, if fewer than 3 points - a degenerate loop,
      matching UDB exactly); Escape/right-click cancels, Backspace removes
      the last point. New Core: `Geometry/PolygonWinding.cs` (the shoelace
      formula, extracted out of `Loop.SignedArea()` so both share one
      implementation). Front/back sidedef assignment verified directly
      against `SectorTracer`'s own documented winding convention and
      cross-checked against the existing
      `MapDataTestExtensions.CreateClosedSector` test-helper's own real
      precedent, not guessed.

      **Phase 2**: stitching into existing geometry - the big remaining
      piece, now done. New Core: `Geometry/LinedefSide.cs` (mirrors UDB's
      real type); `Geometry/LinedefAngleSorter.cs` (the exact same angle
      formula `SectorTracer`'s own already-tested `RelativeAngle` uses,
      generalized off `Sidedef` onto the more general `LinedefSide`, reused
      rather than re-derived independently); `Geometry/BoundaryTracer.cs`
      (`FindPotentialSectorAt`/`FindOuterLines`/`FindInnerLines`/
      `FindClosestPath`-equivalent walk over the map's raw vertex/linedef
      topology - reuses this project's own already-built `Loop`/
      `PolygonNesting` for outer/hole validation instead of porting a
      second polygon class the way UDB's own separate `EarClipPolygon`
      exists); `MapData.SplitLinedef`/`AttachOrRetargetSidedef` (the real
      `Linedef.Split`/`JoinSector` primitives - `Sidedef.Sector` is now
      settable to support re-pointing an already-existing sidedef, not
      just creating fresh ones); `Undo/DrawLoopCommand.cs` (replaces
      `CreateSectorLoopCommand` - per edge, the interior side always
      resolves into a new sector, inheriting a neighbor's properties if
      the trace finds one; the exterior side either joins an existing
      neighbor directly - no new sector - or stays void).

      Two real algorithmic bugs found and fixed while writing
      `BoundaryTracerTests` (hand-constructed graphs with known-correct
      expected traces, including a diagonal-split box and a box with a
      hole): the anti-loop tie-break was missing UDB's real "never swap
      away from the start/end line" exception, so a trace could never
      actually close on real geometry; a vertex shared with the outer
      loop's own boundary was being treated as a valid interior hole seed
      (point-in-polygon containment is ambiguous exactly on a boundary
      point) - now excluded explicitly. A third real bug, found by
      reasoning through the "a drawn line splits an existing sector"
      scenario before it could even be tested: `DrawLoopCommand` was
      skipping trace entries that already had a sidedef when populating a
      newly resolved boundary, when what an old sector's own untouched
      sidedefs actually need in that scenario is *re-pointing* to the new
      sector, not being left alone - `AttachOrRetargetSidedefTracked`
      already handled that branch correctly, the outer skip was just
      wrong and has been removed.

      One deliberate simplification, flagged rather than silently
      dropped: `AttachOrRetargetSidedefTracked`'s freshly-created sidedefs
      always get `DrawLoopCommand.DefaultWallTexture` rather than first
      trying to copy a neighboring sidedef's own specific texture name the
      way UDB's real `JoinSector`/`TakeSidedefSettings` does before
      falling back to a default.

      **Post-Phase-2 fix, 2026-09-18**: user reported drawing a loop
      against an existing wall neither made it two-sided/traversable nor
      inherited its neighbor's textures. Root cause: `DrawLoopCommand.Do()`
      always created a brand-new `Linedef` between every consecutive pair
      of resolved points, even when they were already directly connected
      by an existing one (e.g. two points landing on the same old wall) -
      so no drawn loop could ever end up sharing a real edge with existing
      geometry, only a coincident duplicate or an isolated point-touch.
      Fixed via a new `CreateOrReuseEdge` that detects and reuses an
      already-existing coincident `Linedef` instead of duplicating it,
      with `front` for the interior/exterior resolution passes recomputed
      per edge against whether the reused edge's own direction matches
      this loop's own traversal direction. Separately, `MapData.SplitLinedef`/
      `AttachOrRetargetSidedef` never marked any affected *existing*
      sector's `NeedsRebuild` dirty flag (unlike `MoveVertex`, which
      already does via `Linedef.MarkAdjacentSectorsDirty`) - meant a
      reused wall's mesh could pick up the correct Front/Back sectors in
      Core yet never actually rebuild in the running app, since
      `MapView`'s dirty-sector sweep is what triggers `RebuildWallMesh`
      for an *existing* `MeshInstance3D` (a brand-new Linedef gets its
      first mesh for free when the App side notices it, but a reused one
      needs the dirty flag). Both sectors touched by `AttachOrRetargetSidedef`
      (the new target and whichever sector a re-pointed/opposite sidedef
      used to belong to) and both sides of a freshly split linedef are now
      marked dirty. Root-caused via a new failing test reproducing the
      report at the Core level
      (`Do_LoopSharingAWholeExistingWallByBothEndpoints_ReusesItAsATwoSidedWall`)
      before touching any code; a separate, pre-existing test
      (`..._InheritsPropertiesAndBecomesTwoSided`, renamed
      `..._TouchesAtAPointOnlyWithNoSharedWall`) turned out to have a
      wrong expectation of its own - a loop touching old geometry at a
      single vertex only genuinely has no shared wall to inherit from,
      matching real UDB's own identical limitation there.

      **Second post-Phase-2 fix, 2026-09-18 (same session)**: the reuse
      fix above only covered *one* split point per original wall - user's
      own hard test case (a 60-unit new sector sharing only the *middle*
      portion of a wider 128-unit wall) needs *two*. Root cause:
      `DrawPoint.SplitLinedef` captures whichever `Linedef` was hit at
      draw time - for two points on the same still-unsplit wall, that's
      the exact same object reference - and the old per-point
      `ResolveVertex` split each one independently, in whatever order
      `points` happened to list them, always calling `MapData.SplitLinedef`
      straight on that same captured reference. The *first* split
      correctly shrinks it in place; the *second* point's own position
      then usually no longer lies on what that same (now-shrunk) object
      represents, silently producing overlapping/corrupted geometry
      instead of a clean three-way division. Fixed via a new
      `ResolveVertices` that groups same-linedef split points together,
      sorts each group by distance from the original linedef's own
      `Start` (matching the order they actually lie along the wall,
      regardless of `points` order), and splits a running "tail" segment
      sequentially - the first split's own leftover far half becomes the
      second split's real target instead of the stale original. Also
      fixed alongside it, found by re-reading the user's own bug report
      more carefully ("lose its texture and become traversible"):
      `AttachOrRetargetSidedefTracked`'s freshly-created sidedef always
      got `DefaultWallTexture`, even when the wall was *becoming*
      two-sided (the opposite side already existed) - a real, opaque
      texture on both faces of a plain two-sided wall, when UDB's own
      real behavior is `"-"` on both once there's a genuine sector on
      each side (the opposite side's own cleanup to `"-"` was already
      correct; the newly-created side's own texture was the missed
      half).

      **Third fix, same session, App-layer only (no Core change)**: user
      also flagged that `WallMeshBuilder`'s walls being rendered
      genuinely double-sided (`DoubleSidedMesh`, a leftover from before
      front/back sidedefs could carry independently different textures)
      now actively causes wrong-texture-from-the-wrong-side/z-fighting
      once a two-sided linedef's front and back masked-middle textures
      actually differ (`LinedefWallBuilder.BuildTwoSided` already
      correctly builds two independent, spatially-coincident
      `WallSegment`s in that case - one per side's own texture - so
      double-siding *both* draws all four triangle-equivalents of the
      same quad, undefined draw order deciding which texture wins each
      pixel). Fixed by making wall quads single-sided, wound per
      `WallSegment.Side.IsFront`. `TextureCache`'s wall/flat material
      never disabled the engine's own default back-face culling in the
      first place (only the sprite material explicitly does, for its own
      unrelated billboard reason) - the old double-triangle trick was the
      only reason a wall ever looked double-sided at all, so no material
      change was needed, just the winding. First attempt at the winding
      direction was backwards - derived by hand via a right-hand-rule
      cross product against `VectorConversions.ToWorld`'s axis mapping
      and cross-checked numerically, but that derivation implicitly
      assumed a normal-vs-view-direction culling test; Godot's actual
      front-face/culling convention (screen-space triangle winding after
      projection) didn't match it. User caught it immediately by eye
      ("most textures are rendered on the wrong side of the lines") -
      fixed by swapping the two winding branches; no way to unit-test
      this in `DoomArchitect.Core.Tests` at all (Core is Godot-free by
      design), so this one *only* got verified by the user's own visual
      check in the running app, not by an automated test the way every
      other fix this session was. `SectorMeshBuilder` (floor/ceiling) and
      `TargetHighlight` deliberately still use `DoubleSidedMesh`
      unchanged - a sector only ever has one floor/one ceiling texture
      (no front/back conflict possible there), and the target/selection
      highlight isn't textured content at all, so per the user's own
      explicit call, only walls needed this fix.

      **Fourth fix, 2026-09-30 - the reverse-direction half of the
      2026-09-18 dirty-marking fix above was still missing**: user drew a
      big square, drew a child square inside it (a hole), deleted the
      child sector, undid the delete, then undid the original draw - left
      with a stale 3D mesh still showing the hole/pillar shape, even
      though the 2D overlay (and `MapData` itself) had correctly gone
      back to a single plain sector. The delete/undo round trip in
      between was a complete red herring - it's byte-for-byte reversible
      on its own (see `DeleteSectorsCommand`) - the real bug reproduces
      with no delete involved at all: plain `DrawLoopCommand.Undo()` of a
      loop drawn as a hole inside another sector never marked that
      surrounding sector dirty. Root cause: the 2026-09-18 fix above
      corrected `MapData.AttachOrRetargetSidedef`'s own *forward*
      direction, but `AttachOrRetargetSidedefTracked`'s own undo closures
      (both branches - attaching a brand-new sidedef, and retargeting an
      already-existing one) never mirrored that marking in reverse, even
      though the exact same two sectors are affected either way. Fixed by
      adding the same `NeedsRebuild = true` pair to both undo closures.
      Root-caused and pinned with a new failing-first test,
      `Undo_LoopEntirelyInsideAnotherSector_MarksTheSurroundingSectorDirty`
      (confirmed it fails against the unfixed code, not just against the
      fix).

      **Phase 3, done 2026-09-20**: cardinal-direction constrained
      drawing, full gap-closing across existing geometry, a
      `SplitOuterSectors`-equivalent post-pass, continuous drawing mode,
      a real rubber-band line, `BoundaryTracer`'s own rightward-ray-cast
      retry, and genuinely open (non-closed) polylines. Researched
      directly against UDB's real source
      (`DrawGeometryMode`/`Tools.DrawLines`/`Tools.FindClosestPath`/
      `Tools.FindPotentialSectorAt`/`Tools.SplitOuterSectors`) via a full
      Plan Mode cycle before implementation, not guessed.

      **One correction to this entry's own earlier wording**: "a real
      dashed rubber-band line" turned out to be wrong - UDB's actual
      rubber-band (`DrawGeometryMode.Update`/`Renderer2D.RenderLine`) is
      solid, not dashed, color-coded by whether the segment will stitch
      onto existing geometry (stitch color vs. new-geometry color), with
      a short perpendicular direction-indicator tick at its midpoint.
      That's what got built (`DrawOverlayHandler.DrawSegment`/
      `DrawDirectionTick`), replacing the old plain-solid, placed-vs-
      rubber-band-only coloring.

      **Cardinal-direction (45°) snap**: new `Geometry/CardinalSnapper.cs`
      (pure function, mirrors `GridSnapper`'s own shape), wired into
      `DrawOverlayHandler.ResolveDrawPoint` behind `Alt+Shift`
      (`CardinalSnapEnabled`, live `Input.IsKeyPressed` read, same
      pattern as `MapOverlay.EffectiveSnap`'s own Shift-inverts-the-
      toggle convention), active only once at least one point is placed
      (matches UDB - no cardinal lock for the very first point). A stitch
      candidate is only accepted while locked if it actually lies on the
      locked direction line (`IsOnLockedLine`, matching UDB's own real
      `ourline.GetSideOfLine(nv.Position) == 0` gate - cardinal lock
      takes priority over stitching to arbitrary nearby geometry).
      Deliberately not ported: UDB's own "grid offset kept relative to
      the first point" refinement when cardinal-lock and grid-snap
      combine (that exact source detail wasn't fully recoverable) - grid
      snap, when also active, applies directly via the existing
      `GridSnapper`/`SnapIfEnabled` to the already-locked point instead.

      **Continuous drawing mode**: per the user's own explicit choice (a
      UI toggle button, not a keybind, since this project has no
      settings-persistence layer yet - matches the same gap already
      flagged for keybinding config above), a new `ContinuousDrawToggleButton`
      next to `DrawModeButton` in `Main.tscn`, wired in `ModeToolbar.cs`
      exactly like `GridToolbar.cs` already wires `SnapToggleButton`, into
      a new plain session-only `MapOverlay.ContinuousDrawing` property.
      `DrawOverlayHandler.FinishDraw`/Escape both check it: when on,
      finishing or cancelling clears the in-progress points and stays in
      Draw mode (UDB's own real `OnAccept`/`OnCancel` continuous-drawing
      branches) instead of the normal `ReturnFromDraw`.

      **Open (non-closed) polylines**: `DrawLoopCommand` gained a
      `closeLoop` constructor parameter (default `true`, preserving every
      existing caller's behavior unchanged) - `DrawOverlayHandler`
      decides it from *which gesture* committed the draw (clicking back
      near the first point vs. a plain right-click/too-few-points-to-
      close), a deliberate simplification of UDB's own real detection
      (purely geometric, `firstline.Start == lastline.End` after
      resolution - this project's own Draw mode never adds a literal
      duplicate closing point the way UDB's real `DrawPointAt` does, so
      there's no vertex-identity signal to detect closure from after the
      fact). When open, `Do()` ports UDB's real `splittingonly` check
      (`IsSplittingOnly`, new `GeometryStitcher.FindNearestLinedef`
      helper) - gates a genuinely-new-out-of-the-void sector from being
      created alongside a plain sector-interior split
      (`ResolveInteriorSide`'s new `splittingOnly` parameter) - and UDB's
      real sideless-leftover-linedef cleanup rule (only clean up once
      *something* in the draw got a real sector; a fully-unstitched open
      draw into the void keeps its raw lines).

      **Gap-closing through existing geometry**: new
      `Geometry/DrawGapCloser.cs` (pure search, touches no `MapData` at
      all) ports UDB's real "try every combination of stitched start/end
      candidate, keep the shortest path" search from `Tools.DrawLines`,
      built on a new public two-endpoint `BoundaryTracer.FindClosestPath`
      (UDB's own real `Tools.FindClosestPath` - turned out to already
      exist as `BoundaryTracer`'s own private `Walk`, whose existing
      self-closing calls are just its `start == end` special case, so
      this only needed a thin public wrapper, not new tracing logic).
      Unlike UDB's own real version (a pure-geometry function that has to
      *re-discover* what a drawn endpoint stitches onto via a fresh
      distance search), this project still has the original `DrawPoint`'s
      own `ExistingVertex`/`SplitLinedef` reference available at this
      point and uses that directly - simpler and immune to a dense-area
      distance search finding a different line/vertex than the one
      actually clicked. `DrawLoopCommand.AppendClosingPath` turns a found
      path into real new vertices/linedefs along its own waypoints (UDB's
      own real behavior: NOT reusing the traced existing linedefs
      directly, just their positions as a new chain that the ordinary
      stitch pass right after this merges into the existing geometry).

      **`BoundaryTracer` rightward-ray-cast retry**: `FindOuterLines`
      no longer just fails when a trace lands on the wrong (inner) loop -
      it retries from a different starting edge, found by casting a ray
      rightward from the wrongly-traced loop's own right-most vertex to
      the next linedef it crosses (UDB's own real algorithm, ported
      directly, capped at a defensive `MaxOuterRetries` beyond what's
      confirmed of UDB's own real unbounded-retry behavior). The tie-break
      for two lines crossing at the same point (UDB's own real
      `GetRelativeAngle`-based rule, "prefer whichever is closer to
      parallel with the x-axis") is approximated directly via each
      candidate's own acute angle from horizontal rather than re-derived
      byte-for-byte - source for the exact comparator wasn't available,
      and an exact tie is a genuinely rare case. Verified against a
      deliberately adversarial regression test (a small triangle
      appendage at a box's own closing vertex, its own angle numerically
      confirmed via `LinedefAngleSorter.RelativeAngle` directly - not
      guessed - to out-score the box's real closing edge in the walk's
      own tightest-turn comparison) - real experimentation (not just
      hand-derivation) found this doesn't decisively prove the retry path
      itself fired, since `Loop.Contains`'s own ray-crossing test tends to
      still correctly place the relevant side-point inside a self-
      touching combined trace even without retrying - so what it actually
      confirms is that the walk still finds its way back to the real
      closing edge rather than getting lost or failing outright, not that
      the retry specifically executed. A cleaner decisive test wasn't
      found despite real effort (see `BoundaryTracerTests`'s own remarks
      on this specific test) - flagged rather than overclaimed.

      **`SplitOuterSectors`-equivalent post-pass**: new private
      `DrawLoopCommand.SplitOuterSectors`, run last in `Do()` (matching
      UDB's own real invocation point - `DrawGeometryMode.OnAccept`,
      *after* `Tools.DrawLines` itself fully completes). Reuses
      `PolygonNesting.BuildTree(SectorTracer.Trace(sector)).Count > 1` as
      the "is this sector's own polygon disconnected into multiple
      islands" test (UDB's own real `Sector.Triangles.IslandVertices.Count
      > 1`, no equivalent existed here yet), and reuses
      `CreateAndPopulateSector`/`CopySectorProperties` directly rather
      than re-deriving UDB's own separate `MakeSector` machinery. Two
      flagged simplifications versus UDB's own real `MakeSector`/
      `SectorWasInvalid` (neither's exact source was available to verify
      byte-for-byte): the carved-out sector copies its properties
      directly from the sector it's split from, rather than UDB's own
      separate trace-based property search; and a split that leaves the
      *original* sector with fewer than 3 sides of its own isn't
      specially disposed of - a real, narrower-than-UDB gap. Verified via
      a regression test confirming the common false-positive risk doesn't
      happen (a plain split inside one island of a pre-existing multi-
      island sector correctly leaves the *other*, untouched island
      alone) - a decisive *positive* end-to-end test (this draw's own
      operation being what actually creates a multi-island split) wasn't
      constructed; naturally triggering that through `DrawLoopCommand`'s
      own emergent interior/exterior resolution turned out to need a
      topology this session couldn't cleanly hand-build in the time
      available, flagged rather than skipped silently.

      **Right-click audit, 2026-09-18**: user's own muscle memory
      ("press right click to draw... in vertex mode") turned out to be
      real UDB behavior this project was missing entirely, not a false
      memory - checked every mode's real right-click ("classicedit")
      behavior directly against UDB's source
      (`VerticesMode`/`LinedefsMode`/`SectorsMode`/`ThingsMode.OnEditBegin`)
      rather than guessing. Found and fixed: right-clicking empty space
      (nothing under the cursor to select/edit) in Vertices/Linedefs/
      Sectors mode now starts Draw mode with the first point already
      placed there (UDB's own real `AutoDrawOnEdit`) -
      `ElementOverlayHandler` gained an optional `onEmptyRightClick`
      delegate, wired on those three handlers via a new
      `MapOverlay.StartDrawingAt`/`DrawOverlayHandler.BeginAt`; Vertices
      mode specifically also gained UDB's own real second priority tier -
      right-clicking near a linedef (not a vertex) splits it immediately
      via a new `SplitLinedefCommand`, without needing Draw mode at all
      (`VertexOverlayHandler` now intercepts right-click itself, ahead of
      the shared engine, to check for this case first). Draw mode's own
      right-click changed from cancelling the whole gesture to UDB's real
      `finishdraw` behavior instead - commits what's drawn so far (Escape
      remains the real cancel, genuinely distinct in UDB, not a synonym) -
      narrowed to "close the loop right now" rather than UDB's more
      general "commit open-or-closed," since `DrawLoopCommand` has no
      open-polyline support (noted above).

      Two related real gaps found in the same audit, deliberately **not**
      built this pass (each is its own separably-sized feature, not a
      right-click wiring fix) - flagged here rather than silently
      skipped: UDB's own real Vertices-mode right-click-with-no-drag on a
      highlighted vertex opens a vertex properties dialog
      (`VerticesMode.OnEditEnd`'s `ShowEditVertices`) - this project has
      no `VertexEditDialog` at all yet, unlike Linedef/Sector/Thing's own
      already-built edit dialogs; and UDB's own real Things-mode
      right-click on empty space inserts a new Thing directly (not Draw
      mode) - this is the same "Adding things" feature already tracked as
      its own TODO entry immediately below, not a new discovery.

      Also added: a live length/angle label per segment while drawing
      (`DrawOverlayHandler.DrawLengthLabel`, UDB's own real
      `LineLengthLabel` - "L:&lt;length&gt;  A:&lt;angle&gt;", shown for
      both already-placed segments and the current rubber-band one) -
      user's own second complaint this session ("it's hard to know what
      you're doing" with no length feedback at all). The perpendicular
      offset that keeps the label off the line itself is deliberately
      computed from the *projected screen points*, not a map-space
      direction scaled afterwards - keeps it a constant pixel offset
      regardless of zoom (UDB achieves the identical result by dividing
      by its own renderer's scale) without needing to reason about
      whether a map-space perpendicular direction even survives
      projection unchanged.

      **Geometry-stitching rewrite, 2026-09-19**: user's own explicit
      mandate after real-world use exposed the previous `CreateOrReuseEdge`
      look-ahead approach as fundamentally unreliable ("the intersection
      rules we have for drawing overlapping lines are exceptionally
      lackluster... we do some parts correctly and some parts wrong") -
      "let's take doom builder's algorithms 1 to 1." A full research pass
      directly against `Tools.DrawLines`/`MapSet.StitchGeometry` and every
      primitive it calls found the real architectural mismatch:
      **UDB never looks ahead** - it draws every consecutive point pair as
      a brand-new `Linedef` unconditionally, then runs a genuinely
      general-purpose reconciliation pass against whatever already
      exists. `CreateOrReuseEdge` was a different design that could only
      ever cover the one case it was explicitly built for.

      `DrawLoopCommand.Do()` rewritten to match UDB's own real "draw
      first, stitch after" shape exactly. New Core:
      `Geometry/GeometryStitcher.cs` (a direct port of `MapSet.JoinVertices`
      x2/`SplitLinesByVertices`/`RemoveLoopedLinedefs`/`JoinOverlappingLines`/
      `FlipBackwardLinedefs`, plus `Tools.DrawLines`' own per-segment
      existing-line-crossing pre-pass, guarded by its own real
      `MINIMUM_INTERSECTION_DISTANCE`); `MapData.MergeVertex`/`JoinLinedefs`
      (the real `Vertex.Join`/`Linedef.Join` primitives, the latter's full
      real sector-matching branching ported exactly - including a couple
      of checks that read as unreachable given the branch they sit in,
      kept rather than "corrected," since UDB's own source has them too);
      `BoundaryTracer.DetermineFrontInterior` (UDB's own real per-linedef,
      geometry-driven interior/exterior determination, replacing the
      `PolygonWinding.IsClockwise` shortcut that only ever worked for a
      single simple polygon and breaks down once stitching can produce a
      self-touching or multiply-connected shape). Confirmed directly
      against source and deliberately *not* ported: UDB's own real
      `SplitLinesByLines` (new-vs-new crossing splitting) is a complete
      no-op in the `CLASSIC` merge mode `Tools.DrawLines` itself always
      uses - a self-intersecting drawn polygon genuinely isn't split by
      real UDB during a normal draw either, not a gap on this project's
      side.

      Verified against the exact scenarios the user reported broken:
      drawing a loop that straddles an existing wall with zero explicit
      snapping (both crossing points now split correctly, mid-wall, with
      no `DrawPoint.OnLinedef` involved at all) and a new edge passing
      straight through an existing T-junction vertex with zero explicit
      snapping (the existing vertex gets picked up, not duplicated) - see
      `DrawLoopCommandTests.cs`'s own `..._WithNoExplicitSnapping_...`
      tests. Of the 11 pre-existing `DrawLoopCommandTests`, only one
      needed changing (an `Assert.Same` on a specific `Linedef` object's
      identity surviving a wall-sharing draw - under UDB's own real
      `JoinOverlappingLines`/`Linedef.Join` semantics, the *newly drawn*
      coincident edge survives and the original is merged into it, not
      the other way around; structurally still fully correct, just no
      longer the same object) - every other test kept passing unchanged.

      This also directly narrows (not fully closes) the "Phase 3"
      auto-close gap noted below: a drawn edge that crosses *several*
      existing linedefs/vertices along its own straight path is now
      handled correctly (the stitch pass), but finding a path *through*
      existing geometry to close an otherwise-open drawn polyline
      (UDB's own real `FindClosestPath`-based gap-closing, gated by
      `autoclosedrawing`) still isn't - remains its own separate,
      deliberately out-of-scope item, confirmed directly against source
      to be architecturally distinct from stitching correctness.
