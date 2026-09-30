# Per-mode hover + drag interactions

**Status:** Done  
**Area:** 2D editing

- [x] Per-mode hover + drag interactions, each gated to its own mode via
      `MapOverlay`'s `HandleVertexInput`/`HandleLinedefInput`/
      `HandleSectorInput`:
      - Linedefs: hovering near a segment (point-to-segment screen-space
        distance) highlights it orange; dragging translates both its
        vertices together by the mouse's delta (relative drag, not
        snap-to-mouse - grabbing partway along a line shouldn't teleport
        it)
      - Sectors: added `Core.Geometry.SectorHitTest.Contains(sector, point)`
        - hole-aware point-in-sector test done by summing even-odd
        containment across every one of `SectorTracer.Trace`'s loops,
        no bridging/tree needed (refactored the existing ray-cast out of
        `PolygonNesting` into `Loop.Contains` so both share it). Hovering
        a sector fills its actual floor area (holes excluded) using the
        same trace -> nest -> cut -> ear-clip pipeline `SectorMeshBuilder`
        already uses for the 3D mesh; dragging translates every vertex
        the sector's loops touch by the mouse's delta - including
        vertices shared with a neighboring sector, which will distort
        that neighbor too. That's not handled specially (matches UDB's
        underlying data model: a vertex is one shared point, moving it
        moves it for everyone referencing it) - splitting a shared edge
        apart is real future work, not something to fake now
