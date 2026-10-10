# 3D-mode height edit + panning

**Status:** Done  
**Area:** 3D editing

- [x] 3D-mode plain-scroll-wheel floor/ceiling height + right-click
      properties - done 2026-09-19. User's own request, confirmed as real
      UDB behavior rather than invented from scratch by decoding its
      default keybind config: the numeric action-key values there
      (`raisesector8`/`lowersector8` = plain wheel, `raisesector1`/
      `lowersector1` = wheel+Shift for a 1-unit fine adjustment,
      `raisebrightness8`/`lowerbrightness8` = wheel+Ctrl) split cleanly
      into a base "wheel up/down" code plus a modifier bit - confirming
      plain scroll really does raise/lower height by 8 in real UDB's own
      default binds, not brightness (that's Ctrl+Scroll specifically,
      still the not-yet-built item directly below). `BaseVisualGeometrySector.OnChangeTargetHeight`/
      `ChangeHeight` confirmed it targets `Sector.FloorHeight`/`CeilingHeight`
      directly (no slopes to consider, matching this project's own scope).
      `MapView.AdjustTargetHeight` - reads `TargetSurfaceKind` off the
      already-built `MapRaycaster`/`_currentTarget` (already distinguishes
      Floor/Ceiling/Wall, exactly what this needed) and applies a
      `SetPropertyCommand<Sector, double>` through the undo stack; a Wall
      target is left untouched, matching the user's own explicit scope.
      Deliberately not ported: UDB's own real per-notch `UndoGroup`
      coalescing (this project's own `UndoStack` has no merging mechanism
      at all yet, so each notch is its own undo step) and extending the
      action to the whole 3D-mode selection at once (`_selectedSectors3D`
      only tracks *which sectors* are selected, not separately *which
      surface* of each the way UDB's own per-surface visual objects do,
      so multi-select-then-scroll would be ambiguous without a real
      redesign of that selection model) - always acts on just the live
      target instead.

      Also added in the same pass: right-click in 3D mode now opens
      Sector or Linedef properties depending on what's targeted (UDB's
      own real `visualedit` action, bound to the right mouse button by
      default exactly like classic mode's own `classicedit` - confirmed
      directly against `BaseVisualGeometrySector`/`BaseVisualGeometrySidedef.OnEditEnd`,
      which call the identical `ShowEditSectors`/`ShowEditLinedefs` this
      project's own 2D-mode dialogs already use, reusing the exact same
      `MapOverlay.RaiseEditSectorsRequested`/`RaiseEditLinedefsRequested`
      wiring). Operates on the whole 3D-mode selection of the *targeted*
      type if any (a wall target always means Linedef properties, even
      with sectors selected elsewhere), else just the targeted element -
      matches UDB's own real `GetSelectedObjects` fallback exactly. Also
      releases 3D mode's own mouse capture before popping the dialog
      (matching `FreeFlyCamera`'s already-established Escape/left-click
      capture toggle) - a popup opened while the OS cursor is still
      captured/hidden would otherwise be unreachable to actually click.

## Update: the floor-vs-ceiling selection ambiguity this entry flagged became a real, reported problem

The original scope note above ("multi-select-then-scroll would be
ambiguous without a real redesign") stopped being theoretical: with
one merged `_selectedSectors3D` (just *which sectors*, never *which
surface*), clicking a ceiling visually highlighted its floor too (the
highlight code drew both unconditionally for every selected sector,
with nothing to tell them apart), and there was no way to select a
floor and a ceiling together and raise both in one scroll, even though
that's exactly what UDB's own real `raisesector8`/`lowersector8`
already do (`GetSelectedObjects` collects *every* selected object
regardless of type and applies the same delta to each one's own
`OnChangeTargetHeight`).

Did the real redesign instead of continuing to flag around it: split
into `_selectedFloors3D`/`_selectedCeilings3D` (two independent
`HashSet<Sector>`), matching UDB's own real `VisualFloor`/`VisualCeiling` -
genuinely separate selectable objects there, which is exactly why UDB
never had this ambiguity in the first place. `TargetHighlight.UpdateHighlights`
now draws a sector's floor/ceiling highlight only when that specific
surface is in the matching set, never both from one merged list.
`AdjustTargetHeight` now matches UDB's own real selection-wide
behavior exactly: every selected floor's `FloorHeight` *and* every
selected ceiling's `CeilingHeight` move together in one scroll, as a
single combined undo step, falling back to just the live target
(unchanged from before) only when both selections are empty - the
same "selection wins outright, falls back to the live target when
empty" rule this project already follows everywhere else
(`ResolveNudgeTargets`, the connected-texture-select extension).
`HandleThreeDEditClick`'s own "which sectors to edit" is now the union
of both sets (editing doesn't need the floor/ceiling distinction - the
Sector dialog edits both fields together regardless).

The classic-2D bridge (entering/leaving 3D mode) has nothing finer to
preserve, since a 2D sector selection has no floor/ceiling concept at
all: entering 3D seeds *both* sets from a 2D-selected sector; leaving
3D counts a sector as 2D-selected if *either* of its two 3D surfaces
is selected.

Also added in the same pass, a separate real gap surfaced alongside
this one: a `deselect_all` keybind (`C` by default, UDB's own real
`clearselection` - a `BaseAction` shared across its classic and
visual modes both - which had no equivalent here at all). Matches UDB's
own real scope exactly rather than 3D-only: clears every selected
floor/ceiling/wall/thing in 3D mode, or every selected vertex/linedef/
sector/thing at once in 2D (`MapData`'s own four `ClearSelected*`
methods, matching UDB's own real
`ClearSelection(true, true, true, true, true, true)`).
