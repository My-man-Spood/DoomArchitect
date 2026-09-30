# 2D view panning

**Status:** Done  
**Area:** 2D editing

- [x] 2D view panning (`MapOverlay.PanView`), ported from UDB's real
      `pan_view` action (`Source/Core/Resources/Actions.cfg:311-319`,
      `Source/Core/Editing/ClassicMode.cs:955-997`) - simpler than
      expected: hold **Space** and move the mouse, no mouse button
      involved at all (confirmed - `pan_view`'s default binding is
      literally just the Space key, `UDBuilder.default.cfg:93`). 1:1
      grab-and-drag, no separate pan-speed setting: the map point under
      the cursor before a motion event ends up under the cursor again
      after it, at whatever the current zoom's screen-to-map ratio is.
      Reuses the exact same before/after-`Unproject`-then-shift-camera
      trick `ZoomAt` already established for scroll-zoom, just for a
      translation instead of a zoom change - one new small method, no
      Core changes. Implemented once in UDB (`ClassicMode`, shared by
      every classic mode via inheritance); ported here as one centralized
      check at the top of `MapOverlay._UnhandledInput` instead, since
      this project dispatches all four modes from one place rather than
      separate mode classes to guard individually.
      **Deliberate, flagged divergence**: while Space is held, this
      suppresses *every* other mouse interaction (button press/release
      included, not just motion-driven hover/marquee/drag-threshold
      logic) - stricter than UDB's own real guard, which is only
      `if(panning) return;` at the top of `OnMouseMove` and technically
      leaves `OnSelectBegin`/`OnEditBegin` unguarded (harmless in
      practice there only because nothing gets highlighted while
      panning). Full suppression is simpler and strictly safer - no
      possible path to also starting a select/drag/marquee gesture while
      panning - so it was chosen over replicating UDB's narrower guard
      exactly. No cursor change while panning, matching UDB (confirmed no
      `Cursor`-equivalent change exists in its own real pan code either).
      3D visual mode is untouched - UDB's own visual-mode camera movement
      is a fully separate WASD/mouselook system with no relation to this
      action at all, matching this project's existing `FreeFlyCamera`.
