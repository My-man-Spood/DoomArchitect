# Grid snapping

**Status:** Done  
**Area:** 2D editing

- [x] Grid snapping, ported from UDB's `GridSetup`/its classic-mode drag
      code:
      - `Core.Geometry.GridSnapper.Snap(position, gridSize)` ports
        `GridSetup.SnappedToGrid` exactly - round each axis independently
        to the nearest multiple, using the runtime's default (round-half-
        to-even) rounding, same as UDB's unqualified `Math.Round` call.
        Left out on purpose: UDB's grid rotation/origin transform and its
        clamp to the map format's configured boundaries - neither exists
        elsewhere in this codebase yet, so faking them would just be dead
        code
      - `MapOverlay.EffectiveSnap` ports the exact `ShiftState ^
        SnapToGrid` pattern used identically across every one of UDB's
        classic edit modes: a persistent `SnapEnabled` toggle (default
        on, bound to `G` - UDB itself binds no key here since it's a
        toolbar checkbox we don't have) that holding Shift momentarily
        inverts. Applied in all three drag paths - for the multi-vertex
        linedef/sector drags, only the mouse anchor point is snapped each
        frame (not each vertex independently), so the delta stays exact
        and dragged shapes don't distort
      - Grid size (`MapOverlay.GridSize`, default 32 matching UDB's own
        default) changes via `[`/`]`, matching UDB's own keys and their
        double/halve-with-bounds (1..1024) behavior
      - The grid itself now fills the whole viewport - `ViewportBounds()`
        unprojects the four viewport corners instead of bounding by the
        map's vertex extent, so it stays full-screen at any pan/zoom
        (once those exist) the way UDB's does, rather than stopping at
        the edge of whatever geometry happens to exist
      - Added a black `WorldEnvironment` background so the grid (drawn
        translucent, on top of everything) reads clearly against empty
        space and more subtly over the lit floor mesh
