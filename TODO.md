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
      X/Y -> Godot X/-Z, height -> Godot Y. (Originally shipped without
      the Y negation, "confirmed not mirrored" against the sample room -
      wrong; that room is fully symmetric and can't reveal a mirror by
      inspection. Actually fixed once a recognizable real map exposed it -
      see the Map I/O section.) Still one mesh per sector call,
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

- [x] `Core.IO`: UDMF text format parser/serializer - a close port of
      UDB's own `UniversalParser`/`UniversalStreamReader`/
      `UniversalStreamWriter` (~2200 lines read across two research
      passes into the actual source, not just its behavior guessed at),
      since a from-scratch reimplementation risked missing real
      edge-case behavior UDB already gets right:
      - `UdmfTreeParser`: the tokenizer, same grammar/number-classification
        (hex, the exact non-general "contains '.' or 'e-'" float
        heuristic, int-escalating-to-long) /string-escape/keyword rules
        as UDB's `InputStructure`, reorganized into named methods around
        a small cursor instead of one ~500-line switch loop. One
        deliberate fix (discussed with the user): UDB's own `\DDD` string
        escape has a real bug (only advances 1 of 3 digits, so the
        trailing 2 leak into the string) - fixed here rather than
        reproduced, since nothing depends on the bug
      - `UdmfReader`/`UdmfWriter`: exact field defaults (sector
        `lightlevel` = 160 not 255, linedef `sidefront`/`sideback` = -1
        sentinel, etc.), exact "log a warning and drop" recovery for
        malformed references (dangling vertex, zero-length linedef,
        out-of-range sidedef index, sidedef-with-invalid-sector) rather
        than aborting the whole load, and UDB's own asymmetric
        field-omission rules on write (sector always writes its five
        core fields even at defaults; sidedef omits offsets-when-zero
        and textures-when-"-"; linedef always writes sidefront/sideback,
        `-1` when absent)
      - Went further than pure UDMF-format porting: `Vertex`/`Sector`/
        `Linedef`/`Sidedef` each gained a `CustomFields` bag (boxed
        `object`, no dependency from `Core.Map` back onto `Core.IO`)
        holding any UDMF field recognized by the format but not yet a
        typed property here (linedef `special`/`arg0..arg4`, sector
        `id`/slopes, sidedef flags, vertex `zceiling`/`zfloor`, and any
        genuinely arbitrary third-party field). Combined with whole-block
        preservation for block types we don't recognize at all (`thing`
        included), a load-then-save round-trip loses almost nothing, even
        though most of it isn't editable yet
      - 96 Core tests total (51 new for this pass) covering the
        tokenizer, the reader's defaults/drop-rules/custom-field capture,
        the writer's formatting/omission rules, and full round-trips
- [x] `Core.IO`: WAD reader (`WadFile`) - the classic container format
      (12-byte header + flat lump directory, a map's lumps identified
      purely by position relative to its marker lump, e.g. `MAP01`), plus
      `MapFileLoader.LoadUdmfMap(wadPath, mapName)` wiring it straight
      into the UDMF reader. **UDMF-format maps only for now** - deliberately
      scoped down from "any WAD" after discussing size with the user.
      `WadFile.ReadMapTextMap` throws a clear `NotSupportedException`
      (not a confusing parse failure) when a map's marker isn't
      immediately followed by `TEXTMAP`, i.e. when it's a classic
      binary-format map
- [x] **Classic binary-format map reader** (`ClassicMapReader`) - fixed-size
      binary records (`VERTEXES`=4, `SECTORS`=26, `SIDEDEFS`=30,
      `LINEDEFS`=14 bytes/record), a close port of UDB's own
      `DoomMapSetIO`. Verified against the actual UDB source rather than
      general Doom-format community knowledge, which caught a real
      gotcha: sidedef texture fields are ordered
      upper-then-**lower**-then-**middle**, not the commonly-assumed
      upper-then-middle-then-lower (covered by a dedicated test). Same
      "warn and drop" recovery as the UDMF reader (dangling vertex
      refs, zero-length linedefs, out-of-range sidedef/sector refs), and
      the same `CustomFields` bucket for what isn't a typed property
      (linedef flags/special/tag, sector special/tag) - `THINGS` is
      skipped entirely, since there's no Things model and, unlike UDMF,
      no writer for this format to round-trip through anyway.
      Hexen/ZDoom-format maps are rejected with a clear
      `NotSupportedException` (that format has entirely different record
      layouts, not ported) - detected the same way UDB itself
      distinguishes formats: a `BEHAVIOR` lump alongside the map, not by
      guessing from record sizes (confirmed UDB does NOT sniff
      Doom-vs-Hexen from `LINEDEFS` byte width - that would have been a
      reasonable-sounding but wrong assumption to make without checking).
      `WadFile.FindClassicMapNames()` + `MapFileLoader.LoadClassicMap`
      mirror their UDMF counterparts; `OpenMapMenu` now tries UDMF first,
      then falls back to classic format - opening an original id
      Software WAD works end to end
- [x] **Fixed a real north-south mirroring bug**, found by loading
      DOOM2 MAP01 (a map the user knows well enough to immediately spot
      it) - every map rendered flipped top-to-bottom relative to its
      actual layout. Root cause: `VectorConversions.ToWorld` mapped Doom
      Y directly onto Godot Z with no negation, but the top-down
      camera's -90-degree X rotation makes screen-up correspond to world
      -Z - so increasing Doom Y (north, "up" on every real Doom
      automap) moved toward the *bottom* of the screen instead. No
      camera rotation/roll can fix this: a pure rotation always
      preserves handedness, so it can only choose which world axis
      lands on screen-up, never flip one axis independently - the fix
      had to go in the shared coordinate mapping itself (`ToWorld`/
      `ToDoom` now negate Y/Z), which then correctly fixes the 2D view,
      the 3D view, and vertex-drag hit-testing all at once since
      everything already funneled through that one conversion point.
      Also fixed one place (`MapView.FitTopDownCameraToMap`) that had
      quietly bypassed `ToWorld` and hardcoded the old un-negated
      relationship by hand - now goes through the shared helper so it
      can't independently drift again. This had been latent since the
      very first rendering work; the sample room used to "confirm" the
      mapping back then is a square with a centered square hole,
      symmetric under a north-south flip, so it could never have
      revealed this
- [x] Load a real map file end to end: WAD -> UDMF text -> `MapData`,
      proven by `MapFileLoaderTests` (a real temp `.wad` file written to
      disk, loaded back through `MapFileLoader`, asserted against)
- [x] "Open Map..." UI (`Scripts/View/OpenMapMenu.cs`): a button opening
      a `FileDialog` filtered to `*.wad`, loading the first UDMF map it
      finds (`WadFile.FindUdmfMapNames()` - a map marker immediately
      followed by `TEXTMAP`) and reporting the result via a `MapLoaded`
      event, kept deliberately unaware of `MapView` (same UI/editing-
      surface separation as the rest of the toolbar). `MapView.LoadMap`
      tears down and rebuilds every sector mesh for the new map, resets
      undo history (the old stack's commands close over the discarded
      `MapData`), and refits the top-down camera to the loaded map's
      actual bounds - the sample room's 256x256 camera framing can't be
      assumed for a real map. A failed load (bad file, or a classic
      binary-format map) shows an `AcceptDialog` with the error rather
      than failing silently or console-only

## Architecture notes

- **One parser per format, not a shared "universal" one.** UDB's own
  `UniversalParser` is a genuinely reusable curly-brace/assignment text
  tokenizer, used for UDMF *and* UDB's own game-config files and other
  internal formats - "universal" in a real sense. `Core.IO.UdmfTreeParser`
  is structurally similar under the hood (it doesn't know what a
  "vertex" or "sector" is either, just blocks and assignments), but it's
  deliberately scoped and named as UDMF-only rather than exposed as a
  shared engine other readers could plug into.
  Why: the next format on the roadmap - the classic binary Doom map
  format (`THINGS`/`LINEDEFS`/`SIDEDEFS`/etc.) - is fixed-size binary
  records, not text at all, so it wouldn't share a single line with
  `UdmfTreeParser` regardless of how generic that was made. With only
  one real consumer of a curly-brace text grammar today, pulling out a
  shared "universal" parser now would be an abstraction built for a
  future format that doesn't exist yet, and might not even look like
  UDMF's grammar if it did. If a second genuinely curly-brace-shaped
  text format shows up later (e.g. a game-config file, matching one of
  UDB's own other uses of `UniversalParser`), that's the point to
  reconsider factoring `UdmfTreeParser`'s already-generic tokenizer out
  into something shared - not before

## Later / someday

- [ ] Texture pipeline: Doom picture format decode (`Core`) -> `Godot
      ImageTexture` (`App`)
- [x] Sidedef upper/middle/lower wall mesh generation
      (`Core.Geometry.LinedefWallBuilder` + `Rendering.WallMeshBuilder`) -
      a close port of UDB's own visual-mode wall builders
      (`BaseVisualSector`/`VisualUpper`/`VisualLower`/`VisualMiddleSingle`,
      verified against the actual source): a one-sided linedef spans its
      sector's full floor-to-ceiling height; a two-sided linedef gets an
      upper wall on whichever side's ceiling is higher (none if equal)
      and a lower wall on whichever side's floor is lower (none if
      equal), each clamped against the classic vanilla "closed sector"
      trick (floor above ceiling) the same defensive way UDB's own code
      does. Confirmed from the actual source that a missing texture
      (`"-"`) never gates whether a wall's *shape* gets built - only
      which material/placeholder is applied at render time - so these
      build purely off height gaps, texture-field-independent. Reuses
      the exact same trace -> mesh pipeline philosophy as
      `SectorMeshBuilder`; a new shared `DoubleSidedMesh` helper was
      pulled out of `SectorMeshBuilder` so both builders emit the same
      two-triangles-per-face double-sided pattern instead of duplicating
      it. Walls (like ceilings) live on the same "hidden from the 2D
      top-down view" render layer. Wired into `MapView`'s live rebuild
      loop: a linedef's wall mesh rebuilds whenever either of its
      sectors goes dirty (harmless if that happens on both sides in the
      same frame - rebuilds twice with an identical result).
      **Deliberately not covered**: a two-sided linedef's *masked*
      middle texture (fences/bars/windows) - confirmed from UDB's actual
      code that its default (non-repeating) vertical placement genuinely
      depends on the real texture's pixel height, which we don't have
      until the texture pipeline exists. Tracked as its own follow-up
      right below, not silently dropped
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
