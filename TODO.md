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

  **Update, Game configuration system:** the second consumer arrived - a
  real `.cfg`-format parser (`Core.Configuration.CfgParser`) for
  DoomArchitect's own bundled `Doom.cfg`/`Doom2.cfg`, and eventually a
  real UDB `.cfg` file a user might supply. Reconsidering at that point,
  as promised, landed on *not* sharing a tokenizer with `UdmfTreeParser`
  after all - confirmed via reading UDB's own real `Configuration.cs`
  source that UDB itself keeps `UniversalParser` (UDMF) and
  `Configuration` (`.cfg`) as two separate classes despite both being
  curly-brace/assignment grammars, because the two genuinely differ (a
  `.cfg` key can be a bare integer or contain arbitrary characters up to
  the next delimiter, UDMF's cannot; `.cfg` has `include()` and
  case-sensitive keys, UDMF has neither). Following UDB's own precedent
  here rather than guessing.

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
      Scope, flagged rather than silent: (IWAD+PWAD layering landed later,
      see "Multi-resource support" below) COLORMAP isn't implemented
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
- [x] Things (map objects) - data model (`Core.Map.Thing`) + parsing
      (classic binary THINGS, UDMF `thing` blocks) + a generic placeholder
      rendering in both views. The obvious worry going in - real Thing
      rendering means showing an actual monster/item sprite sized to its
      real type's radius/height, which needs a DoomEd-number-to-actor
      database (the Game configuration system, right below) - turned out
      not to block anything: UDB's own `DataManager.GetThingInfo` always
      returns something renderable even for a totally unrecognized type
      number (a synthesized fallback, radius 10/height 20, its own bundled
      placeholder sprite), used by both UDB's 2D and 3D thing rendering.
      This pass mirrors that bootstrap path with the user's own hand-made
      icon (`Assets/Icons/icon_thing.svg`) instead of UDB's bundled one.
      Only `x`/`y`/`height`/`angle`/`type` are modeled as typed properties
      (matching UDB's own UDMF required/defaulted thing fields exactly);
      everything else (id/pitch/roll/scale/special/arg0-4, every skill/
      ambush/coop flag) round-trips through `CustomFields` - UDB's exact
      write-omission rules for those fields were researched specifically
      to confirm this needs no per-field replication, since we never
      synthesize a value for an absent field and write back verbatim
      whatever's present. Classic-format flags are preserved as a raw
      `ushort` (`Thing.RawFlags`), not decoded into named booleans - real
      translation-table work with no game-config data to drive it yet.
      3D billboard uses Godot's own `BillboardMode.FixedY` (yaw-only,
      matching UDB's own default Thing billboard behavior) rather than
      porting UDB's per-frame camera-relative rotation matrix - simpler
      for an identical visual result. 2D uses the same icon texture,
      rotated to the thing's real angle and sized in world space (scaling
      with zoom, not a fixed screen-pixel size) via the same world-size-
      to-screen-pixels conversion already used for the adaptive grid.
      **Deliberately not built**: no editing (placing/dragging/deleting,
      no `EditMode.Things`) - data model + rendering only, per the literal
      scope of this item; no per-type sprite/size differentiation (every
      Thing looks identical - arrives with the Game configuration system);
      no viewing-angle-based sprite rotation selection (UDB's real 8-frame-
      per-angle system needs real sprite data this pass doesn't have -
      confirmed via source, not guessed, so this isn't a gap waiting to be
      noticed later, it's a documented, deliberate deferral).
- [x] Game configuration system (linedef actions, thing types, sector
      specials) - a real, from-scratch parser for UDB's actual `.cfg`
      grammar (`Core.Configuration.CfgParser`/`CfgLoader`), not hardcoded
      C# data. Two decisions changed this from the original "port UDB's
      game configs" wording, both discussed with the user directly:
      **(1) External file, not hardcoded C#** - the first draft of this
      plan proposed a static C# table, reasoning that two known,
      unchanging vanilla games didn't need a parsed external format. The
      user pushed back with this project's actual end goal in view:
      DoomArchitect targets full UDB feature parity plus custom
      extensions (recorded as its own standing principle,
      `feedback_aim_for_full_udb_port` in memory), and UDB itself uses
      external, user-authorable `.cfg` files specifically because it's an
      open-ended, moddable editor - a future DECORATE-defined-actor
      feature (confirmed in scope, not built yet) and a stated wish to let
      someone bring their own real UDB `.cfg` file over both need that
      same foundation. **(2) Licensing** - UDB's own bundled `.cfg` data
      files are GPLv3, this repo is MIT. Building a parser for the
      *grammar* has no licensing issue (freshly written from reading
      UDB's real `Configuration.cs`, not copied from it - the same
      "read the source, write fresh code" approach used everywhere else in
      this project); a user pointing DoomArchitect at their own existing
      UDB `.cfg` file isn't a licensing issue either (no redistribution by
      DoomArchitect). What needed care: DoomArchitect's own *bundled and
      shipped* `Doom.cfg`/`Doom2.cfg`/`Includes/*.cfg`
      (`src/DoomArchitect.Core/Configuration/GameConfigs/`, embedded
      resources so they're covered by `DoomArchitect.Core.Tests` with no
      Godot dependency) are authored fresh from public, decades-established
      vanilla Doom engine knowledge (DoomWiki-level facts, independently
      republished across dozens of differently-licensed source ports),
      not copied or closely derived from UDB's actual files.
      `CfgParser`/`CfgLoader`'s grammar and merge semantics were verified
      against UDB's real `Source/Core/IO/Configuration.cs`, not assumed -
      notably, `include()`'s merge favors the *included* file's values
      over whatever the including scope already had at that point for a
      plain leaf conflict (`Combine(cs, inc)` - `inc` wins), which is the
      opposite of the "local overrides always win" assumption it'd be easy
      to guess instead; self-inclusion is rejected using UDB's own real
      (narrow) check - the include argument as literally written compared
      against the including file's bare filename, not a general cycle
      detector (UDB's own doesn't have one either, though a lightweight
      recursion guard was added on top here purely to turn a genuine
      multi-file cycle into a clear exception instead of a stack
      overflow - a robustness addition, not a ported behavior). Doom2's
      own `.cfg` needs no C#-level "fall back to Doom's table" logic at
      all: it `include()`s the shared linedeftypes/sectortypes and base
      thingtypes, then layers its own exclusive monsters/items into the
      very same categories - the merge combines them automatically before
      `GameConfigurationLoader` ever sees the result. `IGameConfiguration`
      is deliberately an interface (mirroring the `IMapTargetFinder`
      precedent) so a future DECORATE-lump-driven or user-supplied-file
      implementation can sit behind the same seam later. Wired into the
      concrete, visible payoff promised back in the Things pass: real
      per-type radius/height and the real sprite texture (looked up by
      the exact frame name a `.cfg` entry specifies, e.g. `"POSSA1"` - no
      rotation-frame guessing needed) now render in both `MapView`'s 3D
      billboard and `MapOverlay`'s 2D icon sizing, replacing the one
      shared generic placeholder for any recognized type; `Hangs`
      (ceiling-attached decorations) is modeled and honored, measuring
      down from the sector's ceiling instead of up from its floor.
      Game selection is an always-shown, explicit picker in
      `OpenMapMenu` - matching UDB's own real "Configurations" dialog UX,
      not a silent auto-detect - pre-selected by
      `GameConfigurationDetector`'s best guess (map name pattern, then a
      Doom2-exclusive-doomednum content signature, then a filename hint,
      else Doom) but always requiring confirmation. No persistence of the
      choice yet (asks again on every load - matches the multi-map
      picker's existing precedent; UDB's own real mechanism, best
      understanding not verified in source, is a `.dbs` sidecar file this
      project has no equivalent concept for yet). **Deliberately not
      built**: linedef-action/sector-special data is parsed and tested but
      not wired into any UI yet (no Property editing UI exists); the
      bundled `.cfg` content is an intentionally partial starter set (all
      vanilla monsters/weapons/ammo/keys/health-armor for both games, a
      representative handful of decorations and linedef actions/sector
      specials, not an exhaustive transcription of every one that ever
      shipped - each entry authored and spot-checked individually, not
      mass-generated); viewing-angle sprite rotation selection (still just
      one canonical frame per type, exactly as specified in the `.cfg`
      data); DECORATE/ZScript custom-actor parsing (the real reason
      `IGameConfiguration` is an interface, not a concrete class); and any
      persistent per-WAD project-file storage of the chosen game
      configuration.

      **Update, thing categories + 2D marker color/direction:** two more
      real UDB `.cfg` fields modeled - `arrow` (nonzero = show a facing
      indicator) and `color` (a small palette index) - both category-
      level with per-entry overrides, same inheritance rule as width/
      height. `color` resolves against DoomArchitect's own palette
      (`Rendering.ThingCategoryColors`), not UDB's actual editor color
      scheme (that's UDB's own UI design, not a vanilla-Doom fact, unlike
      radius/height/sprite names). The 2D marker now uses the plain ring
      icon (`icon_thing_nodir.svg`, unused since the Things pass) for any
      type that doesn't actually rotate in gameplay instead of the
      directional notch one for everything - showing a facing indicator
      for something with no meaningful facing is actively misleading, not
      just extra detail. Per-key coloring (blue/yellow/red keys each
      tinted their own color) was tried and reverted - ambiguous at a
      glance against other categories using similar hues; one shared
      "keys" color instead, matching how every other category works.
      This also prompted checking the actual category *names/grouping*
      against UDB's real `Doom_things.cfg`/`Doom2_things.cfg` directly
      (cross-referenced via `awk`, not guessed) - two real mistakes found
      and fixed: "Teleport landing" (14) is its own `teleports` category
      in UDB, not part of `players`; and `health` splits from `powerups`
      (soul sphere/invulnerability/berserk/partial invisibility/radiation
      suit/computer area map/light amp visor/megasphere are `powerups`,
      not `health` - a distinction easy to miss since they're all
      pickups with a similar "buff" feel, but UDB keeps them separate).
      Category names now match UDB's real ones exactly (players/
      teleports/monsters/weapons/ammunition/health/powerups/keys/
      obstacles) rather than DoomArchitect's own prior invented grouping
      (which had merged health+powerups and used "ammo"/"decorations"
      instead of "ammunition"/"obstacles"). UDB's own `lights` category
      isn't represented yet - no light-emitting decoration thing types
      are modeled in the current intentionally partial starter set.
- [x] Multi-resource support (`Core.IO.WadResourceSet`) - a real UDMF PWAD
      with none of its own embedded PLAYPAL/TEXTURE1/sprites (common:
      UDMF maps typically lean on the IWAD for everything) had no way to
      pull those from a separately-loaded IWAD, so Things and any
      PWAD-undefined texture/flat fell back to placeholders. Fixed by
      layering `WadFile`s (later-added = higher priority, exactly UDB's
      own real `DataManager` precedence - confirmed via source, its
      single-item lookups search backwards) with the currently-loaded map
      always forced highest priority; `TextureSet` now depends on this
      instead of a raw `WadFile` for everything (palette, patches,
      textures, flats, sprites), not just sprites -
      `TextureSet.Load(WadFile, ...)` still works unchanged (wraps it in
      `WadResourceSet.Single`) so no existing call site changed.
      This surfaced a bigger discovery while researching UDB's real
      architecture (`DataReader.cs`/`DataManager.cs`/`MapOptions.cs`, all
      read directly): UDB persists resources in two real layers - app-
      wide default resources per game configuration, and a per-map
      `.dbs` sidecar file (an INI-style `Configuration`-format file,
      literally UDB's own extension, keyed by map header name). This
      prompted a real design conversation with the user about whether to
      port `.dbs` now or wait for a much bigger, not-yet-designed
      "DoomArchitect Projects" system (PK3-style conventions + built-in
      scripting/game-extension tooling UDB doesn't have - deliberately
      NOT decided here, deferred to its own future dedicated design
      session - see memory `project_doomarchitect_projects_vision`).
      Resolved: build both real UDB persistence layers now. Added
      `Core.Configuration.CfgWriter` (the write half of the `.cfg`
      grammar `CfgParser`/`CfgLoader` already read faithfully, mirroring
      UDB's own real `Configuration.OutputStructure` formatting) and
      non-destructive `CfgBlock.WithAssignment`/`WithBlock` helpers (a
      settings file must preserve fields DoomArchitect doesn't model -
      same "preserve what you don't understand" principle as UDMF
      `CustomFields`, applied to file-level persistence). `AppSettings`
      (global, per-game-configuration default resources) and
      `MapSettings` (one WAD's `.dbs`-equivalent content) are both
      Godot-free/unit-tested Core types; `MapSettings` deliberately
      mirrors one real, easy-to-miss UDB asymmetry confirmed via source -
      `gameconfig` is a single top-level field shared by every map in a
      WAD's `.dbs`, while `resources` genuinely is nested per map header
      name. Both store ordered resource paths as numbered keys
      (`resource0`, `resource1`, ...), the same idiom UDB's own
      `Configuration`-backed code uses for ordered lists - necessary
      since the underlying storage is a plain dictionary with no
      guaranteed enumeration order on either side. `Scripts/Settings/
      AppSettingsFile.cs`/`MapSettingsFile.cs` are thin App-layer path-
      resolution + read/write-bytes wrappers (`user://settings.cfg` for
      the former, `<wad>.dbs` for the latter). `OpenMapMenu`'s per-load
      "game configuration" dialog is now a combined "Map Options" dialog
      (config selector + resource list together, matching UDB's own real
      single dialog rather than a separate always-present button) -
      pre-filled from this map's own remembered `.dbs` if present, else
      from the app-wide default for whichever game configuration is
      selected; confirming saves both back, so the *next* new map for the
      same game is pre-filled too, with no separate Preferences UI needed
      at all. **Deliberately not built**: PK3/directory resources,
      drag-and-drop resource reordering (priority is just list order),
      and every other real UDB `.dbs` field (script documents, tag
      labels, sector-drawing overrides, etc.) - present-but-uninterpreted
      on a round-trip if a real UDB `.dbs` already exists next to a WAD,
      not destroyed.

      **Update, menu bar + Preferences + in-editor Map Options + scene-
      based dialogs:** using the feature above surfaced three real gaps -
      no way to edit a game configuration's app-wide default resources
      except as a side effect of a map load; no way to add/change a
      loaded map's resources without closing and reopening it; and every
      dialog this session was hand-built in C# (`new AcceptDialog {...}`)
      rather than as real `Control`-based scenes. Added a native Godot
      4.4+ `MenuBar` (`Scenes/Main.tscn`, script `MainMenuBar.cs`) with
      **File** (Open Map...), **Map** (Map Options...), and
      **Preferences** (Resources...) - the standalone "Open Map..."
      toolbar button was removed, consolidated into the menu (redundant
      next to a real menu bar). `Map > Map Options...` re-opens the same
      combined dialog for the map that's *actually currently loaded*, not
      just at load time - `OpenMapMenu` now tracks the current map's
      identity separately from its transient in-progress load state.
      Confirming a revisit fires a new `MapResourcesChanged` event
      (distinct from `MapLoaded`) that `MapView.RefreshResources`
      handles by rebuilding meshes against the new textures/game
      configuration *without* resetting undo history or the camera -
      `LoadMap` still does the full reset, but only for a genuinely
      different map; both share a new `RebuildAllMeshes` helper extracted
      from `LoadMap`'s old body. `Preferences > Resources...` is a new
      `PreferencesDialog` scene editing `AppSettings`'s default resources
      per game configuration directly, independent of any map being open.
      Every dialog (`MapSelectDialog`, `MapOptionsDialog`, the new
      `PreferencesDialog`) is now a real `.tscn` scene under `Scenes/UI/`
      with its own script, instantiated via `PackedScene.Instantiate()` -
      including a genuinely reusable `ResourceListEditor` component (an
      `ItemList` + Add/Remove + nested file-add dialog) embedded in both
      `MapOptionsDialog` and `PreferencesDialog`, mirroring UDB's own real
      `ResourceListEditor` control, which is reused for exactly the same
      two purposes. No `DoomArchitect.Core` changes - App/Godot-layer UI
      only, so verification here is manual, not `dotnet test`.

      **Known gap, verified against UDB's real bundled configs:** what we
      call a "game configuration" (Doom/Doom2) only covers one of the
      *three* axes UDB's own real configuration identity actually
      combines - confirmed directly from UDB's real bundled filenames
      (`Assets/Common/Configurations/*.cfg`, pattern
      `<Engine>_<Game><Format>.cfg`: `Doom_DoomDoom.cfg`,
      `Boom_Doom2Doom.cfg`, `ZDoom_DoomUDMF.cfg`,
      `GZDoom_HexenHexen.cfg`, etc.) - **engine** (vanilla Doom / Boom /
      MBF21 / Eternity / ZDoom / GZDoom / Zandronum / ...), **game**
      (Doom/Doom2/Heretic/Hexen/Strife), and **map format** (Doom binary/
      Hexen binary/UDMF). Telling detail: there is no `Doom_DoomUDMF.cfg`
      - the vanilla engine has no UDMF variant at all, since actual
      vanilla Doom.exe could never read UDMF; UDB doesn't even offer that
      combination.
      DoomArchitect currently collapses this to just the **game** axis,
      applied identically regardless of map format - deliberate, not an
      oversight, for two reasons: (1) no engine-level extensions are
      modeled at all yet (no Boom generalized specials, no MBF/ZDoom
      additions), so "engine" isn't a real axis for us today - there is
      only ever the one vanilla ruleset; (2) map **format** (UDMF vs.
      classic binary) is auto-detected directly from the WAD's own
      structure (unambiguous, unlike "which game"), so there's no reason
      to make the user pick it the way UDB's users do.
      Revisit this once Boom/MBF/ZDoom-style extensions become a real
      goal (a natural step under `feedback_aim_for_full_udb_port` /
      `project_doomarchitect_projects_vision`) - at that point "engine"
      needs to become its own real, user-selectable axis again, the way
      UDB does it, most likely still decoupled from map format for the
      same reason given above.
- [x] Things edit mode (select/hover/move) - a 4th `EditMode`, added
      ahead of Property editing UI below since that item reminded the
      user Things had no selection mode at all yet. Mirrors the existing
      Vertices/Linedefs/Sectors hover-find + drag + undo-record pattern
      in `MapOverlay.cs` exactly (`HandleThingInput`/`FindThingNear`, a
      new `Core.Undo.MoveThingCommand`, `MapData.MoveThing`/
      `GetDirtyThings`/`ClearDirty(Thing)` mirroring `Sector.NeedsRebuild`'s
      dirty-flag idiom, named `Thing.NeedsUpdate` since a moved Thing only
      needs a position resync, not a mesh rebuild - `MapView` extracts a
      shared `ResolveThingWorldZ` helper used both at creation and by a
      new per-frame dirty-things sync). Picking uses each Thing's own real
      on-screen radius (via the same per-type lookup `DrawThings` already
      does) rather than a small fixed pick radius - truer "click what you
      see" given how much Thing footprints vary (a Spider Mastermind vs.
      a key). Movement snaps to grid through the same `EffectiveSnap`
      helper every other mode already shares.
      **Real bug found and fixed in the process**: this project's
      existing Vertices/Linedefs/Sectors keybinds (1/2/3) never actually
      matched UDB's real defaults at all - confirmed via UDB's own
      bundled `Assets/Common/UDBuilder.default.cfg`
      (`buildermodes_verticesmode/linedefsmode/sectorsmode/thingsmode =
      86/76/83/84`, the raw key codes for **V**/**L**/**S**/**T** - letter
      keys, not numbers). An uncorrected divergence from before this
      session, only caught while looking up a real default key for the
      new Things mode. Corrected all four to V/L/S/T rather than bolting
      "4" onto an already-wrong scheme.
- [ ] Keybinding management - every keybind (undo/redo, mode switches,
      grid controls, etc.) is still hardcoded in `MapView._UnhandledInput`
      with zero user configurability, unlike UDB's own real
      `Actions.cfg`-driven, fully rebindable system. Noted as a direct
      consequence of finding and fixing the V/L/S/T keybind mismatch
      above - worth a real settings surface once enough editing features
      exist to make rebinding actually useful.
- [x] Property editing foundation (persistent selection + a renamed,
      UDB-shaped field bag + a generic property-edit undo command) -
      research into UDB's real dialog architecture (`SectorEditFormUDMF`/
      `LinedefEditFormUDMF`/etc., `UniFields`/`UniValue`/
      `UniversalFieldInfo`, `FieldsEditorControl`) surfaced a hard blocker
      before any actual Property editing UI (below) could start: every
      UDB dialog operates on "the current selection," and this project
      had no persistent selection concept at all - only transient
      hover/drag state that reset on mouse-up.
      **`CustomFields` renamed to `Fields`**, reshaped to match UDB's real
      naming/shape (`Core.Map.UniFields : Dictionary<string, UniValue>`,
      `UniValue { UniversalType Type; object Value; }`) specifically so
      porting UDB's dialog logic later needs less translation - a
      deliberate ask from the user, not just a rename for its own sake.
      Two flagged divergences from UDB's actual `UniFields`/`UniValue`:
      `Value` validates against `long` (not UDB's `int`, continuing the
      already-established `UdmfValue.ToObject()` choice) and UDB's
      `Owner`/`BeforeFieldsChange()` (its automatic undo-snapshot hook)
      aren't ported at all - incompatible with this codebase's explicit
      `ICommand.Do()/Undo()` model, used instead. `UniFieldsExtensions`
      ports UDB's `SetFloat`/`GetFloat`/`SetInteger`/`GetInteger`/
      `SetString` (as extension methods, not UDB's "static method taking
      the field bag as a param" idiom) faithfully, including its "never
      store the default value, omit the key instead" rule; `GetBool`/
      `SetBool`/`GetString` are new - real gaps in UDB's own surface
      (confirmed via grep), filled here for a complete four-primitive-type
      accessor surface. `Fields` is fully public and directly mutable
      (matching UDB's `sc.Fields["key"] = ...` exactly) rather than routed
      through a `MapData` method - not actually a new divergence from
      "MapData is the mutation boundary," since plenty of existing
      properties (`Sector.FloorHeight`, `Thing.Angle`, etc.) are already
      plain settable properties with no `MapData` indirection either;
      `MapData` methods exist only for the two properties with real
      cross-element side effects (vertex/thing moves).
      **Selection**: `Vertex`/`Linedef`/`Sector`/`Thing` (not `Sidedef` -
      no edit mode/picking exists for it) each gained
      `IsSelected { get; internal set; }`, the same dirty-flag idiom as
      `Sector.NeedsRebuild`/`Thing.NeedsUpdate` (this is the third
      instance of that exact per-type-repeated pattern - a shared
      `MapElement` base is worth a real discussion once a fourth case
      shows up, not bundled into this pass). `MapData` gained
      `SelectOnly`/`ToggleSelect`/`ClearSelected*`/`GetSelected*` per
      type. Selection is not undoable (view state, not document state,
      matching UDB) and persists independently across `EditMode`
      switches. **2D** (`MapOverlay`): click selects-only, Shift+click
      toggles, clicking empty space clears that type's selection; a new
      red `SelectedColor` (per the user's explicit choice) sits in a
      3-way priority under hover (hover always wins, no blended state) in
      `DrawVertices`/`DrawLinedefs`/`DrawThings`, and `DrawSectorHighlight`
      now fills every selected sector, not just the hovered one. **3D**
      (`MapView`/`TargetHighlight`): UDB has real multi-select in visual
      mode too, not just the classic 2D modes, so this shares the exact
      same `MapData` selection state rather than being a separate 2D-only
      feature - confirmed with the user directly rather than assumed.
      Click-to-select (now handled at all - 3D mode previously had zero
      mouse-button input) acts on the already-current hover target at
      **Sector/Linedef granularity** (a whole linedef/sector, not UDB's
      real per-wall-surface upper/middle/lower granularity - see below);
      unlike 2D, there's no "current mode" restriction, so a mixed
      Sector+Linedef selection builds up naturally. `TargetHighlight`
      was reworked from a single-mesh "one target at a time" node into a
      pooled-child-`MeshInstance3D` `UpdateHighlights(...)` that rebuilds
      every ~80ms pick tick (matching UDB's own `PICK_INTERVAL`) showing
      the hover target plus every selected Sector/Linedef at once.
      **`Core.Undo.SetFieldCommand`**: a new generic leaf `ICommand` for
      setting/removing one field on one element, auto-capturing the
      pre-existing value at construction time so a future dialog never
      has to snapshot it separately. Composes with the existing
      `CommandGroup`/`UndoStack` unchanged for multi-element/multi-field
      edits - no changes needed to either.
      **Deliberately deferred, tracked here on purpose** (surfaced by the
      user as easy to lose track of if only mentioned in passing) so none
      of it quietly falls through the cracks:
      - `.cfg`-driven `UniversalFieldInfo` schema (a `Managed` bool
        gating which fields get a hardcoded dialog control vs. a generic
        fallback row) - each future dialog instead declares its own small
        local `HashSet<string>` of field names it owns, and the generic
        fallback renders purely from each live `UniValue`'s own runtime
        `Type` (no schema lookup needed at all for that). Revisit once a
        dialog actually needs enum/texture/color-typed generic rendering.
      - `UniversalType` cases beyond Integer/Float/String/Boolean (UDB's
        real enum has 27 total) - the rest are UI-control hints,
        meaningless without the schema above.
      - `UniFields` mixed-value comparison helpers (`AllFieldsMatch`/
        `CustomFieldsMatch`/`UniValuesMatch`/`ValuesMatch`) - needed for a
        future multi-select-editing dialog to show a blank/indeterminate
        field where selected elements differ, not needed yet.
      - `UniValue.ValidateName(string)` - trivial to add whenever a UI for
        adding a brand-new custom field by name exists.
      - Marquee/box-select (drag a rectangle to multi-select), in both 2D
        and 3D.
      - Multi-element drag (dragging an entire existing multi-selection
        together as one gesture, instead of collapsing to
        select-only-this-one on drag-start) - needs per-element
        `CommandGroup` + shared drag-anchor math.
      - Sidedef selection entirely, in both 2D and 3D.
      - **3D per-wall-surface selection granularity** (UDB's real
        front-upper/front-middle/front-lower/back-* tracked
        independently, which is what its texture-alignment tools need) -
        this pass only selects at Sector/Linedef granularity, a deliberate
        scope call confirmed with the user. Needs a real "which part"
        identity added to `Core.Geometry.WallSegment` (it has none today)
        and a selection data shape finer than one flag per `Linedef`.
      - "Select all connected same-texture walls"-style flood-fill
        selection utilities (and similar UDB texture-alignment helpers) -
        raised as a "probably wanted eventually" idea, not designed or
        scoped at all yet.
      - A shared `MapElement` base class unifying the now-3x-repeated
        per-type dirty/selection boilerplate (see above).

      **Update, selection lifecycle corrections against real UDB
      behavior:** testing the foundation above surfaced three real gaps,
      each verified against the actual UDB source rather than assumed
      (the user's own recollection and this pass's first-cut
      implementation were each wrong in different ways from what UDB
      actually does):
      1. **Plain click now toggles, not selects-only.** UDB's real
         `classicselect` action (`VerticesMode.OnSelectEnd`/
         `LinedefsMode.OnSelectEnd`/etc.) genuinely adds/removes just the
         clicked element with no modifier needed - Shift/Ctrl only affect
         marquee-drag mode, which this project doesn't have. The
         Shift-vs-plain-click distinction from the first pass was
         dropped entirely, in both 2D (`MapOverlay`) and 3D (`MapView`).
      2. **Mode switching now runs a real geometric conversion**
         (`MapData.ConvertGeometrySelection`, wired into
         `MapOverlay.Mode`'s setter), a close port of UDB's actual
         `MapSet.ConvertSelection` (`Source/Core/Map/MapSet.cs:1163-1262`,
         full algorithm extracted from source, not guessed): a vertex
         selection converting to Linedefs only selects a linedef when
         *both* its vertices were selected; a Linedefs selection
         converting to Sectors only selects a sector when *every* linedef
         bordering it - across every loop it has, holes included, with
         zero special-casing - is selected, OR the sector was already
         selected (the one place prior selection is preserved rather than
         replaced). Neither a plain clear (what the user's own memory
         expected) nor the original persist-untouched behavior (what this
         pass first built) matches this - it's a real third thing.
         `Core.Map.GeometrySelectionType` (Vertices/Linedefs/Sectors, no
         Things) is the new small enum for it. Switching to Things mode
         runs no conversion at all - Things selection is fully
         independent of this, matching UDB exactly.
      3. **3D visual-mode selection is now genuinely separate from 2D
         classic-mode selection**, bridged only at the moment of
         entering/leaving 3D (`MapView`'s `Key.Tab` handler), instead of
         always sharing the literal same `Sector.IsSelected`/
         `Linedef.IsSelected` state the first pass built. Matches UDB's
         real model (confirmed via source): its visual-mode wrapper
         objects carry their own local `selected` field, never
         initialized from the classic `Selected` flag, only synced in
         `BaseVisualMode.OnEngage`/`OnDisengage`
         (`Source/Plugins/BuilderModes/VisualModes/BaseVisualMode.cs:1441-1568`).
         New local `_selectedSectors3D`/`_selectedLinedefs3D`
         (`HashSet<Sector>`/`HashSet<Linedef>`) live in `MapView` itself
         (App-layer session state, not `Core.Map` - mirrors how
         `MapOverlay`'s own hover/drag fields already work), seeded from
         the classic selection on entering 3D and written back on
         leaving it; `TargetHighlight.UpdateHighlights` takes those two
         collections directly instead of a `MapData` reference. UDB gates
         this sync behind a `SyncSelection` setting + Shift-modifier
         combo this project has no settings surface for yet - this port
         always syncs instead (flagged, not silently simplified).
         Confirming that a *mixed* sector+linedef selection within 3D
         mode itself is fine and intentional in UDB (a single unified
         selection list there) was the other half of this research pass -
         that part of the original design was already correct.

      **Update, fixed a real reported bug - stale linedef highlighting
      after deselecting a sector:** selecting linedefs forming a sector,
      switching to Sectors mode, then toggling the sector off left its
      border linedefs stuck showing as selected, with no way to clear
      them. Root cause, verified against UDB's real source rather than
      guessed: `ConvertGeometrySelection` correctly sets a qualifying
      sector's border linedefs selected when *converting into* Sectors
      mode (matching UDB's real `MapSet.ConvertSelection` exactly), but
      nothing then kept that in sync afterward - `MapData.ToggleSelect`/
      `SelectOnly`/`ClearSelectedSectors` on a `Sector` only ever touched
      `Sector.IsSelected` itself. UDB's own real `SectorsMode.SelectSector`
      (`Source/Plugins/BuilderModes/ClassicModes/SectorsMode.cs:538-637`)
      does more: every time a sector's selection changes, it resyncs
      every bordering linedef's `Selected` to `(front sector selected) OR
      (back sector selected)` - confirmed this isn't a renderer bug either
      (UDB's real `Renderer2D.DetermineLinedefColor` shows `Selected`
      unconditionally, not gated by which classic mode is active, and
      DoomArchitect's own renderer already matched that correctly). Ported
      that resync (`MapData.ResyncSectorBoundarySelection`, called from
      `ToggleSelect(Sector)`/`SelectOnly(Sector)`/`ClearSelectedSectors()`)
      - the OR-across-both-sides rule matters for a linedef shared between
      two sectors, which must stay selected if *either* bordering sector
      still is. One real subtlety caught during implementation: the first
      attempt put this resync directly inside the shared
      `ClearSelectedSectors()` method, which `ConvertGeometrySelection`
      also calls internally as pure bookkeeping *after* already deriving
      the correct target-type selection - resyncing there stomped on
      linedef selection that had just been correctly computed. Fixed by
      splitting out a private `ClearSelectedSectorsRaw()` (flags only, no
      resync) for that internal bookkeeping use, keeping the public,
      resync-enabled `ClearSelectedSectors()` for actual user-facing
      sector-deselection gestures only.

      **Update, fixed a real reported bug - dragging a multi-selection
      only moved the hovered element and deselected it:** root cause was
      that every `Handle*Input` method conflated two different gestures
      into one left-click handler - "click to toggle selection" and
      "press-and-drag to move" - so starting a drag on an already-selected
      element immediately toggled it *off* (the same code path always
      flipped selection), then only ever moved the one field-tracked
      dragged element, never the rest of the selection. Verified against
      UDB's real source rather than reinvented (the user explicitly asked
      for this): UDB never conflates the two gestures at all - **left
      mouse button only ever selects/toggles (drag-with-left is reserved
      for box-select, not built here); right mouse button is the only
      thing that ever moves geometry**, confirmed identical across
      `VerticesMode`/`LinedefsMode`/`SectorsMode`/`ThingsMode`'s
      `OnDragStart`. Its real rule: pressing on an unselected element
      replaces the whole selection with just that one before dragging it;
      pressing on an already-selected element preserves and drags the
      *entire* current selection together, with nothing deselected on a
      successful drag. Ported this exactly: all four `Handle*Input`
      methods now split left-click (toggle only, never drags) from
      right-click-drag (the `SelectOnly`-if-unselected rule, then a
      snapshot of every selected element's start position - a
      `Dictionary<Vertex, MapVector2>`, or `Dictionary<Thing, MapVector2>`
      for Things - moved by one shared delta per frame, deduplicated via
      `.Distinct()` where a Linedef/Sector selection can share vertices
      with a neighbor, batched into one `CommandGroup` undo step on
      release). Pure App-layer change (`Scripts/View/MapOverlay.cs`) - no
      Core changes needed, every primitive it uses already existed from
      the selection foundation work (`SelectOnly` in particular had no
      real caller until this).
- [x] 2D marquee/box-select, ported from UDB's real mechanics
      (`Source/Plugins/BuilderModes/ClassicModes/*Mode.cs`,
      `Source/Core/Editing/ClassicMode.cs`) - the natural next piece once
      left-click stopped moving anything (see above). A real surprise
      worth recording: UDB's marquee isn't "point-in-rectangle for
      everything" - Linedefs need **both** endpoints inside by default;
      Sectors need **every** vertex inside (equivalent to a fully
      contained bounding box); Things use plain center-point with **no
      radius consideration at all**, despite Things having real radius
      data used everywhere else in this codebase (hover-picking, 2D icon
      sizing, 3D billboarding) - confirmed as UDB's own actual behavior,
      not something to "fix." `Core.Map.MarqueeSelectionMode`
      (Select/Add/Subtract/Intersect, chosen from Ctrl/Shift exactly like
      UDB's real `BaseClassicMode.GetMultiSelectionMode`) plus 4 new
      `MapData.MarqueeSelectX` methods implement the exact per-type hit
      tests and the real 4-case apply logic (iterates *every* element of
      that type, not just already-selected ones); Sectors mode also
      re-runs the existing `ResyncSectorBoundarySelection` afterward,
      same as the earlier sector-toggle fix. `MapOverlay`'s left-button
      handling had to move its plain-click toggle from press to release,
      gated on whether the drag ever crossed a 2px threshold (matching
      UDB's own `MouseSelectionThreshold`/`!selecting` split) - press no
      longer decides click-vs-drag by itself.
      **Update, the "select touching" toggle got added back in**: this
      pass initially scoped out UDB's secondary `MarqueSelectTouching`
      toggle (loosens Linedefs/Sectors to a crossing/intersecting test)
      as its own separate UI feature with no obvious home - the user
      pushed back and asked for it built too, as a real menu toggle, with
      correct terminology looked up rather than invented. UDB's own real
      strings (`Source/Plugins/BuilderModes/Interface/
      MenusForm.Designer.cs:804-815`, a toolbar `ToolStripButton`) are
      **"Select Touching"** and (from its own tooltip/status text) "select
      inside" - both reused directly for a new Preferences > **"Selection
      Box"** submenu with two radio-checkable, mutually-exclusive items
      (`MainMenuBar.cs`, `Scenes/Main.tscn`'s new `SelectionBox` PopupMenu
      node), instead of UDB's real per-mode toolbar button placement -
      this project's menu bar is where settings-like toggles already
      live. Session-only, matching UDB's own confirmed real behavior
      (`marqueSelectTouching` has no `ReadPluginSetting`/
      `WritePluginSetting` anywhere, unlike its sibling settings that do -
      always resets to "off" on relaunch) - not persisted into
      `AppSettings`. New `Core.Geometry.SegmentIntersection` (a standard
      orientation-based segment-vs-segment test, written fresh rather than
      sourced - ordinary well-known 2D math, not a map-format fact needing
      a UDB citation) backs the crossing tests for both Linedefs and
      Sectors touching-mode.
      **Deliberately not built**: UDB's `AdditiveSelect` toggle (would
      invert plain-Shift's meaning - not needed, its default already
      matches what's here); distance-ordered marquee selection (UDB sorts
      a marquee's hit-set by distance from the drag origin for index-
      label purposes - no such labeling feature exists here yet); any
      marquee/box-select concept in 3D visual mode (UDB doesn't have one
      there either).
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
- [ ] Property editing UI (sector/linedef/thing dialogs) - the foundation
      above (selection + `Fields`/`UniFields`/`UniValue` + generic
      `SetFieldCommand`) unblocks this; not yet started. Per prior
      research into UDB's real dialogs: plan as (roughly) three separate
      pieces given the format/game-dependent complexity - Sector
      (simplest, a good first proof of the foundation), Linedef+Sidedef
      combined (UDB itself embeds sidedef editing inside the linedef
      dialog - no standalone sidedef dialog exists there either - and
      this is the largest of the three, needing dynamic per-action
      argument-editing UI), and Thing.
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
