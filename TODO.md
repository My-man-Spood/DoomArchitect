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
- [x] "Open Map..." UI (`Scripts/View/OpenMapMenu.cs`): a button opening a
      `FileDialog` filtered to `*.wad`, scanning for every map inside it -
      both UDMF (`WadFile.FindUdmfMapNames()` - a marker lump immediately
      followed by `TEXTMAP`) and classic binary-format maps
      (`FindClassicMapNames()`) - and reporting the result via a
      `MapLoaded` event, kept deliberately unaware of `MapView` (same UI/
      editing-surface separation as the rest of the toolbar). A WAD with
      exactly one map loads it immediately; a WAD with several (a real
      IWAD, almost always) pops up a small map-picker `ItemList` inside
      an `AcceptDialog` - built entirely in code at `_Ready()` rather than
      as a scene node, since it's generic enough not to need any actual
      layout work, and this keeps the feature self-contained to this one
      script. Added after texture/lighting work made it worth being able
      to load a *specific* map to check against (e.g. picking a level
      known to be darker than MAP01 to verify sector lighting actually
      varies, rather than only ever seeing whichever map happens to load
      first). `MapView.LoadMap` tears down and rebuilds every sector mesh
      for the new map, resets undo history (the old stack's commands
      close over the discarded `MapData`), and refits the top-down camera
      to the loaded map's actual bounds - the sample room's 256x256
      camera framing can't be assumed for a real map. A failed load (bad
      file, or a Hexen/ZDoom-format map, currently unsupported) shows an
      `AcceptDialog` with the error rather than failing silently or
      console-only

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

- [x] Texture pipeline: Doom picture format decode (`Core.Textures`) ->
      `Godot ImageTexture` (`Rendering.TextureCache`). Ported from UDB's
      actual data-loading code (`DoomPictureReader`, `DoomFlatReader`,
      `Playpal`, `PatchNames`, `TextureImage`/`WADReader`,
      `ImageDataFormat`), verified against the real source rather than
      guessed: patch/sprite posts (with the exact tall-patch topdelta
      accumulation quirk and the whole-patch-aborts-on-overflow bounds
      check); flats sized from lump length (perfect-square, else forced
      64x64 truncated read for a malformed >4096-byte lump); PLAYPAL
      (palette 0 only, matching UDB, with a gray fallback if missing);
      PNAMES + TEXTURE1/TEXTURE2 composite-texture parsing (Doom/Strife
      per-entry auto-detect, TEXTURE1's reserved index-0 slot dropped,
      TEXTURE2 not); patch compositing (`CompositeTextureBuilder`) with
      UDB's real vanilla negative-patch-offset rendering-bug emulation
      ported verbatim, including its exact (and, on inspection, not
      remotely a "half-opacity" test) alpha-transparency semantics -
      `PixelColor.a` is a raw byte compared against the float literal
      `0.5f`, which C#'s byte->float promotion collapses to "alpha != 0",
      not a half-opacity threshold; caught in review after an initial
      port used a naive 127-midpoint threshold instead.
      Two deliberate deviations from UDB's literal algorithm, both agreed
      with the user beforehand: TEXTURE1/2 entries are read via their
      real stored offsets rather than UDB's unstated assume-sequential-
      layout behavior (identical output on every real file, more correct
      on a theoretical non-sequential one); and the per-entry validation
      condition is fixed from UDB's actual operator-precedence bug
      (`(width>0 && height>0 && patches>0 && scalex!=0) || scaley!=0`,
      which passes almost unconditionally since `scaley` is virtually
      never zero) to the evidently-intended all-AND condition.
      Modern image format support (PNG/JPEG signature-sniffing, matching
      UDB's own always-on fallback even inside plain WAD lumps) is
      implemented via a swappable `IModernImageDecoder` interface backed
      by SixLabors.ImageSharp, so standalone lookups *and* patches used
      inside a composited texture both decode PNG/JPEG identically. PCX/
      TGA are signature-detected (UDB's exact heuristics, including the
      width/height/bpp range checks - an earlier, narrower two-field
      version of this check had a real false-positive rate against
      legitimate Doom patch data, caught and fixed in review) but not
      decoded - no comparably lightweight decoder exists, and both
      formats are essentially unused in real Doom content.
      Scope, flagged rather than silent: resolution is scoped to a single
      loaded WAD (no IWAD+PWAD layering yet - belongs with the future
      Game configuration system below); COLORMAP isn't implemented
      (confirmed from the actual source that UDB's own texture/flat
      rendering never applies it); `MixTexturesFlats` and the two
      negative-offset compatibility flags are hardcoded to their vanilla
      defaults (no game-config system yet to make them configurable);
      HiRes lump-range replacement, PK3/directory containers, and the
      text-based `TEXTURES` lump DSL are all confirmed inactive for
      vanilla IWADs and out of scope.
      App-side: `Rendering.TextureCache` converts decoded pixels to
      cached `Godot.StandardMaterial3D`s (nearest-neighbor filtering, a
      deliberate rendering choice - not a UDB behavior - to keep Doom's
      chunky low-res art from being smoothed by Godot's default filter);
      `SectorMeshBuilder`/`WallMeshBuilder` now generate real UVs (flats
      at their conventional 64-map-unit tile size; walls top-pegged,
      offset by the sidedef's own OffsetX/OffsetY) and `MapView` assigns
      the resulting materials per mesh surface. The map-format sentinel
      `"-"` is never passed into `TextureSet`'s lookups (that's reserved
      for a genuinely unresolvable name, which shows a placeholder
      checkerboard) - a `"-"` surface simply gets no material override,
      left at Godot's own default, since "no texture" isn't an error.
      This also unblocks the two-sided *masked* middle texture noted as
      deferred in the wall-mesh-generation entry right below - now
      implemented, see there for details.
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
      A two-sided linedef's *masked* middle texture (fences/bars/windows)
      was added once the texture pipeline above made real texture pixel
      height available - a port of UDB's `VisualMiddleDouble` (its
      sizing math specifically, re-fetched verbatim from the actual
      source rather than re-derived from memory): the "opening" between
      the two sectors is `[max(floor_front, floor_back),
      min(ceiling_front, ceiling_back)]` (the same bounds an upper/lower
      wall's own gap uses); the texture's top edge anchors to the
      opening's top and hangs down by its own pixel height, clipped to
      the opening on both ends - a texture shorter than the opening
      leaves the rest of the opening with no geometry at all (not
      tiled), matching UDB's own non-repeating default exactly rather
      than approximating it.
      **Deliberately not modeled**: UDB's "lower unpegged" flag (which
      would instead anchor the texture's *bottom* edge to the opening's
      bottom) and the `wrapmidtex`/Hexen repeat-texture behavior - both
      because `Core.Map.Linedef` has no typed flags concept at all yet.
      Always uses UDB's own default (unpegged-flag-unset) behavior in the
      meantime; revisit once linedef flags are modeled, naturally
      alongside the Game configuration system below.
- [x] Sector lighting, including vanilla "fake contrast" wall shading
      (`Core.Lighting.SectorBrightness`) - reprioritized ahead of Things
      once real textures made the lack of any shading stand out. A close
      port of UDB's own `Renderer.CalculateBrightness`, verified against
      the actual source rather than guessed: below light level 192,
      brightness drops off faster than linear (`192 - (192-level)*1.5`,
      the "Doom light levels" curve every vanilla game config has on by
      default - emulates the banding of vanilla's 32-entry COLORMAP
      table without literally reimplementing palette-shifting); walls
      additionally get "fake contrast" - a flat +-16 nudge applied only
      when a wall runs exactly north-south or east-west (never on a
      diagonal wall, and never at all once the sector's own light level
      is already 253+) - a vanilla engine trick for depth perception with
      no relation to any actual light source or direction. Floors/
      ceilings never get fake contrast, matching the actual source
      exactly. No artificial minimum-visibility floor either - a light
      level of 0 renders as genuinely black, same as UDB's own preview.
      Implemented as brightness baked directly onto each mesh's own
      vertices (one flat color per sector, or per wall accounting for
      fake contrast) rather than any real Godot light: `TextureCache`'s
      materials are `Unshaded` with `VertexColorUseAsAlbedo` on, so the
      scene's own `DirectionalLight3D` no longer affects sector/wall
      shading at all - correct, not a compromise, since real light-
      direction-based shading has no equivalent in Doom's own model and
      would look wrong on a Doom map regardless. `MapView` needed zero
      changes - brightness is entirely computed inside
      `SectorMeshBuilder`/`WallMeshBuilder` from data already available
      to them (`Sector.Brightness`, a wall segment's own originating
      sidedef/sector), so this stayed fully isolated to Core.Lighting
      plus the existing mesh-builder/material-cache layer.
      **Deliberately not modeled**: UDMF's per-sidedef `light`/
      `lightabsolute` override and separate `lightfloor`/`lightceiling`
      sector fields (all ZDoom/UDMF extensions with no typed properties
      yet) and the `nofakecontrast`/`smoothlighting` UDMF flags that can
      change or disable the above - always uses UDB's own vanilla-format
      defaults in the meantime, revisit alongside the Game configuration
      system and linedef/sidedef flags below.
- [x] 3D "what am I looking at" targeting + highlighting
      (`Core.Geometry.IMapTargetFinder`/`MapRaycaster`,
      `IMapSpatialIndex`/`UniformGridSpatialIndex`,
      `Scripts/Rendering/TargetHighlight.cs`) - reprioritized ahead of
      Things once it became clear this is genuinely foundational: every
      future picking feature (texture picking, wall/sector selection)
      will build on this, not just the light-level-adjustment keybind
      that originally motivated it (still not built - see below).
      A close port of UDB's own visual-mode picking
      (`VisualMode.PickObject`), rebuilt on pure Core geometry instead of
      Godot physics so it's fully unit-tested without Godot running at
      all, matching this project's whole testing culture. Two research
      passes into the actual UDB source (`VisualMode.cs`/`VisualBlockMap.cs`
      for the pick algorithm and its quadtree; `BaseVisualMode.cs` and
      `SectorInfoPanel.cs`/`ImageData.GetPreview()` for what UDB's 3D
      preview actually costs per frame) grounded every performance choice
      below in real, verified behavior rather than guesses - this was a
      deliberate, high-priority design goal given a specific concern about
      past stutter on less powerful hardware, not an afterthought:
      - The ray-cast itself is throttled to an 80ms poll, not every frame
        - UDB's own exact `PICK_INTERVAL`, a ~12x reduction over naive
        60fps polling for zero added complexity.
      - Candidates are narrowed via `IMapSpatialIndex` (a uniform grid,
        walked with the standard Amanatides-Woo "fast voxel traversal"
        algorithm) rather than testing every sector/linedef in the map.
        Deliberately simpler than UDB's own recursive quadtree
        (`VisualBlockMap`) - a real, considered trade-off (less
        implementation/correctness risk, plenty fast for typical Doom map
        geometry, less optimal only for pathological density) - and
        deliberately behind an interface specifically so a quadtree (or
        any other structure) could be swapped in later without touching
        `MapRaycaster` or anything else, if profiling on real content
        ever shows the grid isn't enough.
      - Tracing UDB's real stutter source found it wasn't the pick or the
        highlight at all (both cheap and well-throttled) - it was the
        auxiliary sector-info panel duplicating a texture-preview bitmap
        on every target switch with no evidence of disposing the previous
        one. This pipeline deliberately doesn't build any info panel yet
        (see below) - when one is, cache texture previews by name and
        reuse them, never duplicate/allocate one per switch.
      - The highlight itself is a small dedicated overlay mesh
        (`TargetHighlight`), not a port of UDB's shader-tint-the-real-
        surface technique - `WallMeshBuilder` already merges a linedef's
        wall parts sharing a texture into one mesh surface, so tinting
        "a whole surface" could over-highlight (e.g. an upper wall and a
        one-sided middle wall coincidentally sharing a texture would both
        light up together). A separate mesh built from the exact targeted
        `WallSegment`'s own geometry sidesteps this entirely, and matches
        how this codebase already highlights things in the 2D view
        (`MapOverlay`'s own separate screen-space overlay layer, not a
        mutation of the real map geometry's material).
      - Floor/ceiling hit-testing is already slope-general at essentially
        no extra cost: `PlaneMath` intersects a real `System.Numerics.Plane`
        (normal + offset) rather than hardcoding "intersect Z = height" -
        `PlaneMath.Horizontal(height)` is just the only plane this
        codebase currently knows how to build, since `Sector` has no
        slope data yet. When slopes arrive, only that one construction
        step changes; the actual ray-intersection math here won't need
        touching. **Wall hit-testing is NOT slope-future-proofed the same
        way** - a `WallSegment`'s `Bottom`/`Top` are flat doubles derived
        from a sector's constant floor/ceiling heights, and
        `MapRaycaster.TryWall`'s Z-bounds check assumes that. Making walls
        properly slope-aware is real new work belonging to
        `LinedefWallBuilder` itself (a wall's height becoming a function
        of position along it, not a constant) - flagged explicitly here
        (and in a code comment at the exact spot in `MapRaycaster`) rather
        than left as a silent trap for whenever slope support is actually
        built (see the Slopes entry below, which cross-references this).
      - Masked-middle-texture walls ARE hit-testable/highlightable:
        `MapRaycaster`/`TargetHighlight` thread an optional
        `Func<string, double>` height-lookup straight through to
        `LinedefWallBuilder.Build`, exactly like `WallMeshBuilder` already
        does - no new `Core.Geometry` -> `Core.Textures` coupling needed.
      **Deliberately not built yet**: any info panel/texture preview (see
      above - the actual historical stutter source, being avoided on
      purpose), and any editing action wired to this (sector brightness
      adjustment - the feature that originally motivated this whole
      system - texture picking, and selection are all future consumers,
      built separately later now that the foundation exists). 3D-mode
      only for now; the underlying Core system is view-mode-agnostic and
      could support a 2D-mode equivalent later without changes.
- [ ] Things (map objects) - data model + billboard sprite rendering
- [ ] Game configuration system (linedef actions, thing types, sector
      specials) - data + parsing, ported from UDB's game configs
- [ ] Property editing UI (sector/linedef/thing dialogs)
- [ ] Full-bright toggle + real sector/wall brightness editing (Ctrl+Scroll,
      matching UDB's own `togglebrightness`/`raisebrightness8`/
      `lowerbrightness8` default keybinds) - the feature that originally
      motivated building 3D targeting above; now buildable as a
      straightforward consumer of `IMapTargetFinder` plus a
      `ChangeSectorBrightnessCommand` through the existing undo stack.
- [ ] Slopes / 3D floors (UDMF extensions) - should fit naturally since
      floors/ceilings are already real meshes in this architecture. Also
      the point to revisit `MapRaycaster.TryWall`'s wall hit-testing (see
      the targeting entry above) - it currently assumes flat, constant
      floor/ceiling heights and will need updating alongside
      `LinedefWallBuilder` once walls can actually tilt.
- [ ] The "funky" 3D-view stuff: dynamic/animated lighting (blinking,
      strobing, and other sector-special light effects - static per-
      sector lighting is already done, see above), shader effects,
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
