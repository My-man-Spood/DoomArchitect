# MapOverlay.cs god-object cleanup

**Status:** Done  
**Area:** Architecture

- [x] `Scripts/View/MapOverlay.cs` god-object cleanup - flagged
      2026-09-04 at ~500 lines; revisited 2026-09-16 once it had grown to
      1181, well past the "revisit once it starts being painful to
      navigate" trigger (the Thing-rendering work alone added ~260 lines
      to it). Split into 9 files, as separate composed classes (not
      partials, per this entry's own original guidance):
      `MapOverlayCamera` (projection math), `MapOverlayGrid` (background
      grid), `MarqueeSelector` (the shared left-button marquee state
      machine every mode drives), `MapOverlayColors` (the hover/selection
      tints all four element types use identically), and one handler per
      element type - `VertexOverlayHandler`/`LinedefOverlayHandler`/
      `SectorOverlayHandler`/`ThingOverlayHandler` - each owning that
      type's own hit-testing, input, and drawing. Split by element type
      rather than input-vs-drawing, since that's the axis the file's own
      growth actually followed (the Thing work never touched Vertex/
      Linedef/Sector code, and "drawing mode"/"adding things" - both now
      on the open list above - will cut the same way).
      **Went further than a pure file-move**: reading all four modes' own
      original `Handle*Input` methods side by side confirmed they were a
      genuinely identical skeleton (left-click select/marquee, right-
      click drag, matching UDB's own real button split), differing only
      in which `MapData` query/undo command/optional double-click event
      to use - collapsed into one generic `ElementOverlayHandler<TSelectable,TDraggable>`
      engine (two type parameters since a linedef/sector has no position
      of its own and drags its own *vertices* instead of itself, unlike
      Vertex/Thing), with each per-element handler supplying the real
      differences as constructor delegates. `MapOverlay` itself is now a
      ~260-line thin orchestrator (constructs the pieces, dispatches
      `_UnhandledInput`/`_Draw` to them). One real bug fixed along the
      way: found two orphaned, unattached `<summary>` doc-comment blocks
      left behind by an earlier `DrawThings`/`DrawThing` split, re-merged
      onto the method they actually describe. One real risk caught before
      it shipped: originally constructed the new pieces in `_Ready()`,
      but `MapView`'s own `_Ready()` assigns straight into
      `MapOverlay.Camera` with no guaranteed ordering between the two (not
      a parent-child relationship) - moved construction into `MapOverlay`'s
      own constructor (field initializers) instead, so every property
      setter is safe from the very first frame regardless of Godot's own
      node-ready order.
