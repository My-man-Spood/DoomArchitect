# Dynamic grid size

**Status:** Done  
**Area:** 2D editing

- [x] Dynamic grid size, ported from UDB's own
      `ClassicMode.MatchGridSizeToDisplayScale` (the mechanism the
      previous entry's "not ported" note was about):
      - `Core.Geometry.DynamicGridSize.ForVisibleExtent(minVisibleMapUnits)`
        is a bit-for-bit port of UDB's integer round-up-to-power-of-two
        trick (kept as the exact same bit-twiddling rather than
        reimplemented via a logarithm, so it lands on identical sizes at
        identical zoom levels) - always returns a power of two, including
        fractional ones (0.5, 0.125), targeting roughly a fixed cell
        count across the smaller visible screen dimension
      - `MapOverlay.DynamicGridSizeEnabled` (default on, matching UDB's
        own default) recomputes `GridSize` via that helper on every zoom
        (`ZoomAt`), using the same viewport-corner-unprojection
        `ViewportBounds()` already provides for the visible extent
      - Toggled with `D` (like snap's `G`, UDB binds no key here either -
        both are toolbar checkboxes there); manually resizing with
        `[`/`]` turns it off, matching UDB's own `DisableDynamicGridResize`
        (you don't want automatic and manual sizing fighting each other)
