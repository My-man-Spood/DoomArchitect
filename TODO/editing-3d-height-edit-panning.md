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
