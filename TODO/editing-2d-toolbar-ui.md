# Toolbar UI (mode/grid/status bar)

**Status:** Done  
**Area:** 2D editing

- [x] Toolbar UI (`Scripts/View/ModeToolbar.cs`, `GridToolbar.cs`,
      `StatusBar.cs`, `Assets/Icons/*.svg` - hand-drawn, not reused from
      UDB, to sidestep the GPL-asset question entirely). Lives entirely
      on its own `UI` CanvasLayer (`layer = 10`, drawn on top) rather
      than nested inside `MapOverlay` - general UI shouldn't be a child
      of the map-editing surface it controls, so `MapView` toggles each
      piece's `Visible` explicitly alongside the overlay's instead of it
      being inherited for free:
      - `ModeToolbar`: three icon `Button`s in a `ButtonGroup` radio set.
        Clicking one sets `MapOverlay.Mode` exactly like the 1/2/3 keys
        do; `_Process` syncs button pressed-state from `Mode` every frame
        via `SetPressedNoSignal` (avoiding a feedback loop) so a keybind
        press updates the buttons too, not just the reverse
      - `GridToolbar`: a grid-icon toggle button mirroring `G`/`SnapEnabled`
        the same way, a `+`/`-` button pair calling new
        `MapOverlay.IncreaseGridSize()`/`DecreaseGridSize()` methods (the
        `[`/`]` double/halve-with-bounds logic, pulled out of `MapView`'s
        keybind handler so the keybind and the buttons share one policy
        instead of two copies of it), and a live grid-size label that
        reads `GridSize` every frame - reflects dynamic-grid-size changes
        from zooming, not just manual resizing
      - `StatusBar`: the mode/grid/snap status text, moved out of
        `MapOverlay._Draw()`'s `DrawString` call into a real bottom-of-
        screen `Label` for the same UI/editing-surface separation reason
        (`MapOverlay.EffectiveSnap` had to become `public` for it to read)
      - Every button has `tooltip_text` (a stock `Control` property -
        Godot shows it on hover automatically, no script needed)

      **Update, real icons (2026):** the five edit-mode toolbar icons
      (`mode_vertex`/`mode_line`/`mode_sector`/`icon_thing_nodir`/
      `mode_draw`) were swapped for UDB's own real `VerticesMode.png`/
      `LinesMode.png`/`SectorsMode.png`/`ThingsMode.png`/
      `DrawGeometryMode.png` (`Source/Plugins/BuilderModes/Resources/`),
      now that the license change removes the reason they were hand-drawn
      in the first place. These are native 16x16 raster icons scaled up
      to this project's own 28x28 button size, unlike the hand-drawn SVGs
      they replace - a real, accepted quality tradeoff (blurrier at this
      size), not an oversight. The other icons here (checkboxes, grid,
      tag, thing markers) have no equivalent standalone UDB icon asset to
      swap in and stay hand-drawn.
