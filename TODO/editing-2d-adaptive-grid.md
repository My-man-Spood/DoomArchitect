# Adaptive multi-tier grid

**Status:** Done  
**Area:** 2D editing

- [x] Adaptive multi-tier grid, ported from UDB's `RenderBackgroundGrid`/
      `RenderGrid`:
      - A second grid tier, always fixed at exactly 64 units (Doom's
        alignment unit) in a distinct color, draws whenever the
        configured grid is 64 or finer - so that reference stays visible
        no matter how fine you've zoomed the working grid
      - Each tier independently doubles its own drawn spacing (not the
        configured/snap size) until a cell is at least 6 screen pixels -
        UDB's exact "increase rendered grid size if needed" threshold -
        so a fine grid zoomed far out never renders as illegible mush
      - Cell size in screen pixels is measured by projecting a
        `size`-unit segment through the camera rather than reasoning
        about `Camera3D.Size`/aspect-mode math directly - same trick
        `ViewportBounds` already uses, works the same for any projection
