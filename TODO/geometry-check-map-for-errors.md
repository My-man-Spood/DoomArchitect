# Check Map for Errors

**Status:** Done
**Area:** Geometry

- [x] `Map > Check Map for Errors...` - ports UDB's own real tool
      (`ErrorChecks/`, confirmed directly against source), motivated by
      real corruption just found and hand-fixed in the user's own map
      (duplicate vertices, a self-referencing sector, fragmented linedef
      connectivity - produced by the new Make Sector tool running
      against already-damaged geometry). UDB's own real architecture is
      a reflection-discovered, attribute-tagged set of checkers, each
      run on its own worker thread with live progress - real overkill
      for this project's map sizes, ported as a plain, synchronous
      `MapErrorChecker.RunAll(MapData)` instead
      (`src/DoomArchitect.Core/Geometry/MapErrorChecker.cs`). The
      individual check *algorithms* are still ported faithfully from
      UDB's own real check classes - four for v1, the ones directly
      relevant to what just broke: overlapping (duplicate-position)
      vertices, stray (unconnected) vertices, bad linedef sides (no
      sidedefs at all; back-only, the exact shape
      `GeometryStitcher.FlipBackwardLinedefs` already exists to fix
      elsewhere; and a linedef whose front and back both belong to the
      same sector - not one of UDB's own named checks, but a direct,
      trivial, high-value addition given it's exactly the shape the new
      Make Sector tool's own dead-end/flip-side trace fallback can
      produce against already-broken geometry), and unclosed sectors
      (ported through this project's own `SectorTracer.Trace`, which
      already silently drops any sidedef it can't fit into a closed
      loop rather than reporting that gap itself - comparing the
      sector's own real sidedef count against the total vertex count
      actually traced catches exactly that silent drop). UDB's other
      real checks (missing/unknown textures, stuck/obsolete things, map
      size) are a deliberate, separate follow-up, not needed for this.

      **Trigger UI deliberately diverges from UDB's own current source**,
      confirmed directly rather than assumed: real UDB is a dedicated
      "Map Analysis" edit mode with its own toolbar button and a
      non-modal results window - not the status-bar indicator the user
      remembered (likely a different fork, a much older version, or a
      mental merge with the generic status-text area). User's own
      explicit choice once shown the discrepancy: a plain menu item
      (`MainMenuBar`'s existing `Map` menu, alongside `Map Options...`)
      opening a simple modal results dialog
      (`MapErrorCheckDialog`/`.tscn`, mirroring `MapSelectDialog`'s own
      minimal `AcceptDialog` + `ItemList` shape exactly) - no new edit
      mode, no stateful status-bar widget, re-running every check fresh
      each time it's opened rather than caching (same "recompute, don't
      cache" convention `OpenMapMenu.BuildScriptCatalog` already
      established).

      Double-clicking (or OK-ing) a result selects whichever element
      kind it actually carries - preferring Linedefs > Sectors >
      Vertices when a result spans more than one kind (e.g. a self-
      referencing linedef also names its own sector), since the more
      specific element is almost always the more useful one to land on
      - and jumps the camera to it via new `MapOverlayCamera.CenterOnArea`
      (reuses the exact same before/after-unproject-then-shift-camera
      trick `ZoomAt`/`PanView` already establish; only zooms *out* if the
      target area wouldn't otherwise fit, never zooms in unnecessarily -
      UDB's own real `ClassicMode.CenterOnArea` default 0.6 padding
      factor). **A real near-bug caught before it shipped**: the first
      pass selected multiple elements via `SelectOnly` inside a loop -
      which clears the *entire* selection on every call, so a multi-
      vertex/linedef result would have ended up with only the *last*
      element actually selected. Fixed by clearing once up front, then
      `ToggleSelect` per element (safe unconditionally, since every
      target was just cleared and so starts unselected).

      New `MapErrorCheckerTests` covering all four checks plus a clean-
      map `RunAll` case, including a dedicated test confirming the
      unclosed-sector check actually catches `SectorTracer`'s own silent
      sidedef drop (a dangling extra sidedef off one corner of an
      otherwise normal closed box) - 12 new tests, 1197 total passing.
      No coverage for `MapErrorCheckDialog`/`MainMenuBar`'s own wiring,
      same established convention as every other dialog in this
      project.
