# DoomArchitect roadmap

A from-scratch Linux/Godot reimagining of Ultimate Doom Builder. Architecture
decisions and reasoning live in the conversation history / `.tours/`; this
file just tracks what's built and what's next.

## Done

- [x] Godot C# project scaffold, proving the core idea: one live 3D scene,
      an orthographic top-down camera and a perspective camera, switching
      is just a camera swap (`Scenes/Main.tscn`, `Scripts/View/MapView.cs`)
- [x] `DoomArchitect.Core` split out as a plain, Godot-free class library
      (structurally enforced via `Microsoft.NET.Sdk`, no GodotSharp ref)
- [x] Map data model: `Vertex`, `Linedef`, `Sidedef`, `Sector`, `MapData`
      aggregate root, with dirty-tracking on vertex move (only touched
      sectors get marked dirty - mirrors UDB's `UpdateNeeded` propagation)
- [x] `DoomArchitect.Core.Tests` (xUnit) proving the dirty-tracking
      guarantee, running via plain `dotnet test`, no Godot engine

## Next up (geometry pipeline)

- [x] `Core.Geometry`: derive a sector's boundary polygon(s) by tracing
      linedef loops (port of UDB's `Source/Core/Map/SectorBuilder.cs`/
      `Triangulation.cs` tracing step) - `SectorTracer.Trace(sector)`
      returns each loop with its winding direction (clockwise = outer,
      counter-clockwise = hole)
- [x] `Core.Geometry`: polygon triangulation for floor/ceiling meshes,
      including holes for nested sectors - `PolygonNesting.BuildTree`
      (which loop nests under which), `PolygonCutter.Cut` (bridges holes
      into their outer loop via a rightward ray cast), `EarClipper.Clip`
      (the actual triangulation). Full pipeline: `SectorTracer.Trace` ->
      `PolygonNesting.BuildTree` -> `PolygonCutter.Cut` -> `EarClipper.Clip`
- [x] `App`: `System.Numerics.Vector2/3` <-> `Godot.Vector2/3` conversion
      helpers (no built-in bridge between them, confirmed via reflection) -
      `Scripts/Interop/VectorConversions.cs`
- [x] `App.Rendering`: `SectorMeshBuilder` - Core triangles -> Godot
      `ArrayMesh`. Floor and ceiling each get two real triangles per
      polygon triangle (one wound each way) rather than a single
      double-sided-material triangle - a single triangle's normal only
      shades correctly from the side it's meant to face, so viewed from
      the wrong side it renders black regardless of culling. Doom
      X/Y -> Godot X/Z, height -> Godot Y; confirmed not mirrored against
      an actual rendered top-down view. Still one mesh per sector call,
      not yet one `MeshInstance3D` per sector wired into a live scene -
      that's the next item
- [x] Free-fly camera for the 3D view (`Scripts/View/FreeFlyCamera.cs`) -
      WASD + mouse look + Space/Shift for up/down, only active while its
      camera is Current. Wasn't originally scoped, but needed for actually
      checking rendering work visually instead of guessing camera angles
- [x] Wire the rebuild loop: `MapView` now keeps a `Sector -> (floor
      MeshInstance3D, ceiling MeshInstance3D)` map and, every `_Process`,
      rebuilds and reassigns `.Mesh` for anything `MapData.GetDirtySectors()`
      returns, then clears the flag - unblocked now that vertex dragging
      actually dirties sectors
- [x] Camera render layers: `SectorMeshBuilder.Build` now returns floor
      and ceiling as two separate meshes (`SectorMesh` record) instead of
      one, since render layers are per-`MeshInstance3D`. Ceiling goes on
      layer 2; `TopDownCamera.cull_mask = 1` excludes it, so "2D view"
      shows floors only, while the perspective camera (default cull mask)
      still sees both

## Next up (editing basics)

- [x] Screen-space overlay layer for 2D-view gizmos (`Scripts/View/MapOverlay.cs`,
      a `Control` on its own `CanvasLayer`): grid snapped to a 64-unit
      spacing derived from the map's vertex bounds, vertex knobs, and
      linedefs color-coded one-sided (white) vs two-sided (gray). Redrawn
      every frame via `Camera3D.UnprojectPosition()` rather than baked
      into world-space geometry, so it stays crisp at any zoom; toggled
      alongside the camera swap so it only shows in the 2D view. Pulled
      the Doom-X/Y -> Godot-X/Z mapping out of `SectorMeshBuilder` into a
      shared `VectorConversions.ToWorld` since both it and the overlay
      need the exact same mapping
- [x] Click/drag to move vertices in the 2D view (`MapOverlay`):
      `_UnhandledInput` picks the nearest vertex within a screen-space
      radius on left-click, then on drag unprojects the mouse via a
      camera-ray/ground-plane (Y=0) intersection and calls
      `MapData.MoveVertex`. Selection (as opposed to just drag-grab) isn't
      built yet - no persistent "this vertex is selected" state
- [x] Basic edit modes (`EditMode`: Vertices / Linedefs / Sectors),
      matching UDB's own numeric-key mode split (1/2/3). `MapOverlay.Mode`
      is the single source of truth: the gizmo type matching the active
      mode draws at full opacity, the others dim to 35%, and an on-screen
      label (drawn straight into the overlay via `DrawString`, no extra
      scene node) shows the current mode
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
- [x] Scroll-wheel zoom for the top-down camera (`MapOverlay.ZoomAt`):
      changes `Camera3D.Size` (smaller = zoomed in), then shifts the
      camera position by the map-space delta at the cursor before/after
      so the point under the cursor stays fixed on screen (matching
      UDB's own scroll-to-zoom feel) instead of always zooming toward
      the map origin. Clamped to a 20..2000 `Size` range
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
- [x] `Core.Undo`: command-based undo/redo stack (pure Core, no Godot).
      Deliberately NOT a port of UDB's actual `UndoManager` - that's a
      1400-line byte-level binary diff/snapshot system tightly coupled to
      its own `MapElement` serialization format, a background thread that
      compresses old snapshots, and WinForms-era plugin/ticket plumbing.
      None of that carries a correctness risk the way the map-format
      algorithms do (a command stack and a binary-diff stack produce
      identical user-facing undo/redo), so it isn't a case for porting
      literally - a plain `ICommand`/`UndoStack` two-stack design fits
      this codebase's much smaller data model instead. What *did* carry
      over, because it's genuinely the same problem: UDB's exact
      Ctrl+Z/Ctrl+Y keys (checked against its default keybind config, not
      guessed), its 2000-level history cap (`UndoStack.MaxHistory`, same
      number on a different mechanism), and its grouping behavior - one
      undo step per user gesture rather than one per intermediate write,
      via `CommandGroup` (undoes members in reverse order). Vertex/
      linedef/sector drags in `MapOverlay` now record a command only at
      mouse-release (comparing the drag's start snapshot to the final
      position), not per mouse-motion frame, and skip recording entirely
      if nothing actually moved

## Map I/O

- [ ] `Core.IO`: UDMF text format parser/serializer
- [ ] `Core.IO`: WAD reader (map lumps at minimum; texture/resource lumps
      later)
- [ ] Load a real map file end to end: WAD/UDMF -> `MapData` -> rendered
      in both views

## Later / someday

- [ ] Texture pipeline: Doom picture format decode (`Core`) -> `Godot
      ImageTexture` (`App`)
- [ ] Sidedef upper/middle/lower wall mesh generation
- [ ] Things (map objects) - data model + billboard sprite rendering
- [ ] Game configuration system (linedef actions, thing types, sector
      specials) - data + parsing, ported from UDB's game configs
- [ ] Property editing UI (sector/linedef/thing dialogs)
- [ ] Slopes / 3D floors (UDMF extensions) - should fit naturally since
      floors/ceilings are already real meshes in this architecture
- [ ] The "funky" 3D-view stuff: real-time lighting, shader effects,
      anything that leans on Godot's own renderer being live in the 2D
      view too

## Known concerns

- [ ] `Scripts/View/MapOverlay.cs` is ~500 lines and growing, mixing four
      distinct concerns: drawing (grid/vertices/linedefs/sector fill),
      per-mode input/drag handling, camera projection math (`Unproject`/
      `Project`/`ViewportBounds`/zoom), and grid math. Not a problem yet,
      but flagged as a god-object-in-the-making - noted 2026-09-04 rather
      than fixed, since the user doesn't mind it yet. Natural split when
      it's addressed: separate classes composed by `MapOverlay` along
      those seams (e.g. a per-mode input handler set, a camera-math
      helper), not partial classes - partials hide the size without
      actually decoupling responsibilities. Revisit once Things/property-
      editing UI adds another mode's worth of code, or sooner if it
      starts being painful to navigate

## Process

- [ ] CI (GitHub Actions): run `dotnet test` on `Core.Tests` on every
      push - cheap to set up now since it needs no Godot install at all
