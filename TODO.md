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
      **Real pegging and transparency added in a later pass** (2026-09-18),
      closing the two gaps flagged above: `LinedefWallBuilder` now reads
      the line's own real "lower unpegged" flag - `Core.Map.Linedef`
      still has no typed flags concept, so this reads both storage
      conventions that actually exist in the codebase directly:
      `ClassicMapReader`'s verbatim raw-flags-word `"flags"` integer
      field (bit 16, `ML_DONTPEGBOTTOM`, verified against UDB's own
      game-configuration data) and `UdmfReader`'s own named boolean
      `"dontpegbottom"` field - and, when set, anchors the texture's
      *bottom* edge to the opening's own bottom (hangs up) instead of the
      prior unconditional top-anchor/hangs-down behavior, matching
      `VisualMiddleDouble.Setup`'s own real crop-plane formula exactly.
      `Sidedef.OffsetY` now shifts that anchor point itself for a masked
      middle - moving where the texture actually sits, not just scrolling
      which part of it shows - unlike a plain upper/lower/single wall,
      where the quad's extent is fixed by sector heights alone and
      `OffsetY` only ever scrolls the image within it; `WallSegment`
      grew a `VerticalTextureOffset` field to carry the right V-origin
      for either case so the App layer doesn't need its own pegging logic
      (`WallMeshBuilder` reads that instead of `Side.OffsetY` directly).
      Transparency was a separate, purely App-layer gap: the decoded
      texture pixels themselves were already correctly alpha 0/255 per
      pixel (`DoomPictureReader`/`CompositeTextureBuilder` both zero-init
      and only ever write alpha 255 for pixels a patch post actually
      covers) - `TextureCache.CreateMaterial` just never enabled any
      `Transparency` mode on the material using them. Fixed with a new
      `TextureCache.GetMaskedWallMaterial`, reusing the same already-
      decoded/uploaded texture as the opaque `GetWallMaterial` (no double
      decode) but with `TransparencyEnum.AlphaScissor` - a hard cutout
      rather than smooth `Alpha` blending, chosen because Doom's own
      masking convention is strictly binary (confirmed via the decoders
      above), so a cutout matches the source data faithfully without
      introducing the draw-order/sorting concerns smooth blending would
      add for no real benefit. `WallMeshBuilder` now groups wall segments
      by `(Texture, IsMasked)` rather than `Texture` alone, so a masked
      middle and a same-named opaque upper/lower/single never merge into
      one surface, and picks the right material per surface accordingly.
      Still deliberately not modeled: the `wrapmidtex`/Hexen repeat-
      texture behavior, for the same underlying reason as before
      (no typed linedef-flags concept for that flag either yet) - revisit
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
      for an identical visual result. 2D originally used a single flat
      icon texture, rotated to the thing's real angle and sized in world
      space (scaling with zoom, not a fixed screen-pixel size) via the
      same world-size-to-screen-pixels conversion already used for the
      adaptive grid - since replaced with UDB's own real sprite-based
      approach, see the dedicated entry below.
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
- [x] PK3 (zip-based) resource loading - pulled forward ahead of property
      editing UI because a real test map depends on `gzdoom.pk3` for
      textures/flats/sprites/patches it doesn't embed itself. Researched
      UDB's real `DataReader`/`WADReader`/`PK3StructuredReader`/`PK3Reader`
      hierarchy (`Source/Core/Data/*.cs`) before designing this, then built
      the shared lookup surface this entry originally envisioned:
      `Core.IO.IResourceContainer` (`FindLump`/`FindNamespaceLumps`), with
      `WadFile` and the new `Pk3File` both implementing it, and
      `WadResourceSet` widened into `ResourceSet` over `IResourceContainer`
      instead of `WadFile` specifically - `TextureSet` never needed to
      change its own logic, just its declared types. `ResourceContainerFactory.Open`
      sniffs a path's magic bytes (`IWAD`/`PWAD` vs. the zip `PK` signature)
      so the resource-list UI (Map Options, Preferences) and saved
      `.dbs`/`settings.cfg` resource paths both just work whether a path is
      a `.wad` or a `.pk3`, no extension-trusting required. PK3 namespace
      folders (`patches/`, `textures/`, `flats/`, `sprites/`, `graphics/`)
      ported literally from UDB's real `PK3StructuredReader` constants -
      only the 5 namespaces `TextureSet` actually consumes; `hires/`/
      `colormaps/`/`voxels/` are real GZDoom conventions too but nothing
      reads them yet from either a WAD or a PK3, so left out until
      something does.

      Deliberate divergences from UDB, flagged rather than silent: built on
      .NET's own built-in `System.IO.Compression.ZipArchive` instead of the
      third-party SharpCompress UDB uses (SharpCompress's whole reason for
      being there is tolerating a PK3 that's secretly a rar/7z file - not a
      real-world need, and this way needed zero new dependencies); within
      one archive, a duplicate path keeps the *first* entry and silently
      drops later ones, matching UDB's own real (arguably non-canonical
      versus real GZDoom's last-wins) behavior on purpose, since UDB is
      this project's north star for Core logic.

      **Deliberately not built**: a `.wad` embedded in a PK3's own root
      (UDB supports this; not needed for `gzdoom.pk3`, which doesn't embed
      one); nested PK3-in-PK3 (UDB doesn't support this either);
      `MixTexturesFlats`/`roottextures`/`rootflats` game-config options
      (still hardcoded to vanilla defaults, same as the texture pipeline
      entry above); saving/writing to a PK3 (resources are only ever
      referenced by path, never rewritten, same as WADs today).
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
- [x] Property editing UI, Sector (v1) - `SectorEditDialog`, ported from
      UDB's real `SectorEditFormUDMF` (both field scope and on-screen
      layout: Properties tab grouped/ordered exactly like UDB's real
      `groupfloorceiling`/`groupeffect`/`groupaction` group boxes, plus a
      real tab strip mirroring UDB's 5 remaining tabs as placeholders).
      Floor/ceiling height+texture, brightness, gravity, special, tag -
      real-time apply while open (heights/textures/brightness) with one
      combined undo step and full revert on cancel; special/tag/gravity
      apply only on OK, matching UDB's own real split. Numeric fields got
      UDB's real blank/absolute/`++N`/`--N`/`*N`/`/N` multi-select grammar
      (`Core.Editing.NumericFieldExpression`) plus real spinner buttons
      (`StepperLineEdit`, porting UDB's actual `ButtonsNumericTextbox`
      step values - 8/16/1 for heights and brightness, 0.1/1/0.01 for
      gravity). Linedef+Sidedef and Thing dialogs were still not started
      at the time this was written - see the dedicated Linedef entry
      below (now done) and TODO.md's own tracking for Thing, the last of
      the three main property dialogs, not yet started.

      **Update, Properties tab closer to real parity + GZDoom UDMF config:**
      a real GZDoom Doom2 UDMF screenshot exposed how much v1 was missing -
      brought up to parity field-by-field against `SectorEditFormUDMF`,
      researched directly rather than guessed (two dedicated research
      passes into `TagsSelector.cs`/`SectorEditFormUDMF.cs` for the
      Identification section alone). Also added the first non-vanilla
      game configuration, `GameConfigurationKind.GZDoomDoom2UDMF`
      (`GZDoomDoom2UDMF.cfg`) - its own real 11-entry sector-flags list, 20
      known GZDoom damage-type strings, and the real ~94-entry UDMF
      sector-types list (replacing vanilla's 16-entry table wholesale, not
      merging - matches real UDMF's independent numbering namespace),
      authored fresh from public engine knowledge per this project's own
      licensing practice, not copied from UDB's actual `.cfg` files.
      Thing/linedef data is temporarily reused from vanilla Doom2 pending
      the Linedef/Thing dialogs.
      New sections: **Flags** (the real 11 UDMF/GZDoom sector-flag
      checkboxes, rebuilt per game configuration from
      `IGameConfiguration.GetSectorFlags()`, untouched-checkbox-leaves-
      value-alone multi-select semantics); **Height Offset** (UDB's real
      transient/UI-only convenience field - never stored, always shows
      "0", nudges floor+ceiling live including bare `++`/`--` = "by this
      sector's own current height"); **Sector damage** (type/amount/
      interval/leakiness); **Sound Sequence** + **Fog Density** added to
      Effects; and a real **Identification** section replacing the old
      plain Tag/Tags text fields - `SectorTagsEditor` ports UDB's actual
      `TagsSelector` control (verified button-by-button against its real
      Click handlers, not guessed from icons): New/Unused/Clear buttons,
      a Clear All + Add/Remove multi-tag row of clickable chips, and the
      same real per-slot-index-lockstep multi-selection model UDB itself
      uses (`List<List<long>>` internally, one list per selected sector,
      a slot shows mixed/blank when selected sectors disagree at that
      index) - not a simplified "one shared value" stand-in. Backed by two
      small new Core pieces: `TagAllocator.FindFree` and
      `MapDataTagQueries.GetUsedTags`/`GetUsedSectorTags` (map-wide vs.
      this-element-type-only free-tag scans, the New/Unused button logic).
      Floor/Ceiling Texture moved to their own real **Surfaces** tab
      (an earlier pass had mistakenly folded them into Properties, modeled
      after the older classic-format dialog's single combined group box -
      corrected once checked against the real UDMF dialog directly). Also
      introduced `GroupBox` (`Scripts/View/GroupBox.cs`/`Scenes/UI/GroupBox.tscn`)
      as a genuine reusable `Container` subclass (real `_GetMinimumSize`/
      `NOTIFICATION_SORT_CHILDREN` implementation, not a plain `Control`
      hand-positioning children) - a titled section box with a 1px border
      interrupted by the title, replacing every bare header-`Label`-above-
      plain-content section in this dialog; and a Sector Special browser
      (`SectorSpecialBrowserDialog`) for the Special field's "Browse..."
      button. `Assets/BaseTheme.tres` also gained a real `LineEdit` style
      and became the actual registered project-wide default theme
      (`project.godot`'s `[gui] theme/custom`) - it had only ever been
      manually applied to a few `Main.tscn` nodes before, so every dialog
      including this one was silently using Godot's raw engine default
      theme the whole time.

      **Explicitly still missing/deferred, not silent gaps:**
      - `damagetype` and Sound Sequence are plain free text, not UDB's
        real combo lists - both are populated in UDB by parsing the map's
        own DECORATE actors / SNDSEQ lumps respectively, and this project
        has neither parser yet.
      - New/Unused only scan sector `id`/`moreids` and linedef `id` -
        UDB's real search also treats a linedef's tag-type *action
        arguments* (e.g. a Teleport's destination tag) as "in use", which
        needs per-argument "is this a tag" metadata this project's
        `IGameConfiguration`/`LinedefActionInfo` schema doesn't model at
        all yet (`Linedef` itself has no typed arg0-4 accessors either,
        just the raw `Fields` bag).
      - No `>=`/`<=` (ascending/descending range across the selection) or
        `++`/`--` (per-collection-position offset) tag-distribution
        grammar - UDB can assign each selected sector a different
        sequential tag from one typed expression; this project only
        applies one absolute value to every selected sector's slot in
        lockstep. Same category of "batch distribute by position" feature
        already declined for `NumericFieldExpression` itself (its
        `+++`/`---` variant).
      - No Generalized Effects bit-flag tab/UI - `generalizedsectors` is
        set `true` in the new GZDoom config, but this project exposes only
        the plain ~94-entry named sector-types list rather than UDB's real
        bit-flag combo editor.
      - Per-field numeric step sizes for the new fields (Fog Density,
        Damage Amount/Interval/Leakiness) are sensible flat defaults, not
        verified against UDB's exact `ButtonStep`/`ButtonStepBig`/
        `ButtonStepSmall` values for these specific fields.
      - Colors, Slopes/Portals, Comment, and Custom tabs are all still
        placeholders ("Not yet implemented").
      - `SectorTagsEditor` is Sector-specific for now, scoped directly to
        `IReadOnlyList<Sector>` - real UDB shares this exact control with
        its Linedef dialog, which doesn't exist in this project yet;
        generalize behind an interface once it does, not before.

      **Update, Surfaces tab per-surface fields:** rebuilt to match UDB's
      real layout, verified directly against `SectorEditFormUDMF.Designer.cs`
      rather than assumed - two group boxes, "Ceiling" then "Floor" (not a
      single combined group, and not Properties-adjacent), each now with
      the real UDMF texture offset (`xpanningfloor`/`ypanningfloor` and the
      ceiling equivalents), scale (`xscalefloor`/`yscalefloor`/ceiling,
      default 1.0), rotation (`rotationfloor`/`rotationceiling`, default
      0.0), and per-surface light override (`lightfloor`/`lightceiling` +
      `lightfloorabsolute`/`lightceilingabsolute`) fields alongside the
      existing texture name - all OK-only, matching the established real-
      time-vs-OK-only split, since none of them have any visual effect to
      preview yet (see below). **Deliberately not built**: UDB's real
      rotation dial widget and "use linedef angles" checkbox (a plain typed
      rotation field covers the same data without an exotic custom
      widget); the render-style dropdown, terrain dropdown, and
      reflectivity field (each needs real infrastructure this project
      doesn't have - a render-style enum, a terrain-type game-config
      schema); the reset-to-default buttons UDB has for the light override
      pair. **A real rendering gap, not just a UI one**: none of these
      fields are actually applied anywhere in `SectorMeshBuilder`/
      `TextureCache` yet - texture offset/scale/rotation don't affect the
      generated UVs, and the per-surface light override doesn't affect
      `Core.Lighting.SectorBrightness`'s computed brightness. They
      round-trip correctly (readable, writable, preserved on save) but
      editing them currently changes nothing you can see - wiring them
      into the actual mesh/lighting pipeline is separate, not-yet-started
      work.
- [x] Property editing UI, Linedef (v1) - `LinedefEditDialog`, ported from
      UDB's real `LinedefEditFormUDMF` - the largest of the three main
      property dialogs, mostly because of its dynamic per-action argument
      editing UI. Properties tab: Action number + "Browse..."
      (`LinedefActionBrowserDialog`, grouped into collapsible per-category
      folders like UDB's real `ActionBrowserForm`, not one flat list -
      matters once the action table reflects real breadth, see below), 5
      fixed argument slots (never dynamically added/removed, matching
      UDB's real `ArgumentsControl`) relabeled per the selected action's
      own real arg0-arg4 metadata and toggling between a numeric field and
      an enum dropdown. Godot has no equivalent to UDB's real editable
      combo-box argument control (which lets you type an exact value even
      for an enum-backed argument) - added a manual click-to-toggle on the
      argument's own label as the adaptation, carrying the value across
      the switch (exact value enum-to-number, nearest match number-to-
      enum). Flags (26 real UDMF booleans) and Activation (10 real UDMF
      triggers) are a genuinely separate group in real UDB even though
      both are just named booleans under the hood - both rebuilt per game
      configuration the same way Sector's own Flags group already is.
      Identification reuses the Sector dialog's own tags control,
      generalized from `SectorTagsEditor` into `MapTagsEditor` (UDB really
      does share this exact control between both dialogs). Front/Back
      tabs: whole-sidedef offset (already modeled, real-time) plus per-
      texture-part (Upper/Middle/Lower) offset/scale/light-override using
      the 18 real, genuinely distinct UDMF field names (verified against
      `UniversalStreamReader`/`Writer`, not assumed). A one-sided line's
      Back tab shows disabled rather than hidden, matching UDB's real
      `Enabled = false` treatment.

      **The linedef action table needed two real correctness passes, not
      just UI work.** First pass shipped only 12 hand-picked Hexen-style
      generic actions - researched the complete real ~191-action table
      (6 parallel research passes into UDB's actual `Hexen_linedefs.cfg`/
      `ZDoom_linedefs.cfg` source) once flagged as far under real GZDoom
      UDMF's own breadth. Second pass fixed a real data bug found on
      review afterward: `keys` (`Door_LockedRaise`/`Generic_Door`'s lock
      argument) shipped Hexen's own puzzle-key names in this Doom2-
      targeted config instead of Doom's real red/blue/yellow keycard/
      skull keys - root cause was flattening UDB's own real per-game
      `enums_doom`/`enums_hexen`/etc. value-list layering into one merged
      table, losing the "which game's list is this" information that
      would have made the correct choice obvious; fixed by keeping a
      `_doom`-suffixed name plus a comment explaining the convention for
      any future Hexen/Heretic config (see [[feedback_udb_is_north_star]]
      in memory - this is now a standing rule: mirror UDB's real file/
      data layering, not just its end behavior, even when the actual
      prose still needs independent authoring for licensing). Also added
      back the numeric-value prefix UDB's own real enum titles always
      carry (e.g. "16: Slow"), dropped during independent rephrasing.
      **Deliberately out of scope:** the other ~24 real UDB argument types
      beyond plain numeric/enum (tag/texture/thing pickers, angle dials,
      etc.); ACS arg0-as-string; a Generalized (Boom) specials tab, same
      reasoning as Sector's own deferred Generalized Effects tab; sidedef
      Custom-fields button; sidedef/sector reassignment or creation;
      Comment/Custom tabs (placeholders, matching Sector's own pattern).
- [x] Property editing UI, Thing (v1) - `ThingEditDialog`, ported from
      UDB's real `ThingEditFormUDMF` (verified via source, not guessed) -
      the last of the three main property dialogs (Sector/Linedef both
      done above). 4 tabs (Properties/"Action / Tag / Misc."/Comment/
      Custom - Comment/Custom stay placeholders, matching Sector/Linedef's
      own pattern). Properties tab: a `ThingTypePicker` (new) embedded
      directly in the " Thing " group - a real, filterable category tree
      (mirroring `LinedefActionBrowserDialog`'s own grouping shape) with a
      live sprite thumbnail per row and a bigger preview for the current
      selection, backed by a new `SpriteIconCache` (same seed-warm/decode-
      on-demand shape as `TextureIconCache`, packaging `TextureSet`'s
      already-real sprite decode as a flat 2D icon) - a deliberate scope
      call (embedded, not a popup "Browse..." dialog like Sector Special/
      Linedef Action) since that's UDB's own real shape here. " Flags "
      (flat checkbox list, same `RebuildCheckboxes` pattern), " Position "
      (X/Y/Z, real-time) and Type/Angle (real-time) round out the tab;
      Pitch/Roll are OK-only (this project's sprite billboards don't apply
      either yet). " Rotation "'s 3 numeric fields each pair with a real
      rotating-compass dial (`AngleDialControl`, new - a genuine port of
      UDB's real `AngleControlEx`: tick marks every 45°, left-click/drag
      snaps to 45°, right-click/drag is free rotation) plus UDB's real
      one-shot-at-Confirm "Random" checkbox per axis (never a stored
      flag - confirmed directly against `ThingEditFormUDMF.cs`'s own
      `cbrandomangle`/etc. handlers). "Action / Tag / Misc." tab: real
      OK-only Rendering (Scale X/Y, Alpha+Reset, Render Style) and
      Behaviour (Gravity/Score/Health/Conversation ID/Float Bob Phase)
      groups (verified field names/defaults directly against
      `ThingEditFormUDMF.cs`), an Action group reusing the exact same
      argument-editor a Thing's own `special`/`arg0-4` genuinely resolve
      against (see the extraction note below), and Identification reusing
      `MapTagsEditor` via a new `SetThings` overload (a confirmed third
      real shared consumer). Wired into "Edit Selection" via a new
      `MapOverlay.EditThingsRequested`/`HandleThingInput` double-click
      case, mirroring Sector/Linedef exactly.
      **Generalized off "Linedef" once a second real consumer needed the
      identical logic**: `LinedefActionInfo`/`LinedefArgumentInfo`/
      `LinedefArgumentEnumOption`/`GetLinedefAction(s)` renamed to
      `ActionInfo`/`ArgumentInfo`/`ArgumentEnumOption`/`GetAction(s)` (a
      Thing's own action/args genuinely resolve against the identical
      table a Linedef's action number does, confirmed via UDB's own
      parallel `ArgumentsControl.SetValue(Linedef,...)`/`SetValue(Thing,...)`
      overloads) - zero behavior change, same treatment `SectorTagsEditor`
      already got becoming `MapTagsEditor`. The whole argument-slot editor
      (toggleable numeric/enum rows, action Browse button, etc.) was then
      extracted from `LinedefEditDialog`'s own inline copy into a new
      shared `ActionArgumentsEditor` control, confirmed to leave the
      Linedef dialog's own behavior unchanged (443/443 Core tests still
      green) before building the Thing dialog on top of the same control.
      Added `IGameConfiguration.GetThingTypes()`/`GetThingFlags()` +
      `ThingTypeInfo.Category` (the raw `.cfg` category key, now tracked -
      previously discarded during load) + a verified-real 25-entry
      `thingflags` block in `GZDoomDoom2UDMF.cfg` (cross-checked against
      `UDMF_misc.cfg`/`ZDoom_misc.cfg`, correcting an initial wrong guess
      of `skill1`-`skill16`/`class1`-`class3` before it ever shipped - real
      UDMF only defines 8 skill levels and 5 player classes) - plus
      `MapDataTagQueries.GetUsedThingTags()`.
      **A real, pre-existing bug found and fixed while wiring this
      dialog's own OK handler**: every OK-only field write in the already-
      shipped Sector and Linedef dialogs (Special/Gravity/damage fields/
      Flags/Activation/per-part offset-scale-light overrides/Tags) called
      `UndoStack.Record(new CommandGroup(commands))` instead of
      `Execute(...)` - `Record` assumes its command already ran (proven by
      `UndoStack`'s own `Record_AddsAnAlreadyPerformedCommandWithoutRunningItAgain`
      test) and never calls `Do()`, so every one of those fields was
      silently never actually written when clicking OK (verified
      empirically with a throwaway test before believing it). Fixed in
      all three dialogs by switching to `Execute` (harmless to re-run an
      already-live-applied command's `Do()` a second time - it just re-
      sets the same current value) plus a permanent regression test in
      `UndoStackTests`.
      **Deliberately out of scope**: the
      `ThingTypeInfo`-driven fallback argument schema for `action == 0`
      "param things" (UDB's own more obscure feature - only the already-
      built "args come from the selected action number" path is in
      scope); UDB's real dynamic-light Color picker and config-driven
      Render Style dropdown (no color-picker control or render-style
      schema in this project yet - Render Style is a plain free-text
      field instead, same simplification as Sector's own Damage Type/
      Sound Sequence); UDB's real "Absolute Height" display-mode toggle
      (needs a point-in-sector lookup this project's Core has no public
      helper for yet - `Thing.Height` is already always floor-relative, so
      the field works correctly without it); UDB's real obsolete-type
      warning styling in the type browser; sidedef/sector-style Custom-
      fields button; Comment/Custom tabs (placeholders).
- [x] Thing-type catalog, full GZDoom/ZDoom/Boom breadth - the follow-up
      flagged above, done once the dialog/picker framework was proven.
      Replaced the ~46-entry hand-curated `DoomThings.cfg` starter set
      with a scripted, verified extraction from UDB's own real
      `Doom_things.cfg`/`Doom2_things.cfg`/`ZDoom_things.cfg`/
      `GZDoom_things.cfg`/`Boom_things.cfg` (~5000 lines of real source
      across 5 files) - 346 total thing types now, up from 46. A hand-
      typed transcription at this volume was judged too error-prone (the
      same class of mistake the `keys_doom` bug already taught this
      project to avoid), so a small Python parser (matching this
      project's own real `CfgParser` grammar) extracted structured data
      instead of retyping text by hand. Per explicit user decision this
      pass: thing titles are copied verbatim from UDB (short factual
      labels like "Imp"/"Point Light", not creative prose - there's
      really only one correct English name for most of these, unlike the
      per-config `.cfg` prose this project's other bundled data
      independently rewords) - doomednum/sprite/width/height/hangs/arrow/
      category are unchanged objective-fact territory either way. `color`
      is now copied too (per a follow-up user decision, superseding the
      original "invent our own palette" call this same pass started
      with) - `Rendering.ThingCategoryColors` was replaced wholesale with
      UDB's own real shipped-default thing-color palette
      (`ColorCollection.THINGCOLOR00`-`19` in UDB's own source, 20 real
      named `System.Drawing.Color`s), so every category's own `color`
      value is now the same real UDB index, more familiar to anyone
      coming from UDB - no reason to keep diverging there once asked.
      New files mirror UDB's own real physical file/module boundaries and
      `include()` structure exactly rather than flattening everything
      into one file: `Includes/Doom2Things.cfg` (Doom2's own exclusive
      additions, now included by `Doom2.cfg` instead of an inline
      hand-curated `thingtypes` block), `Includes/BoomThings.cfg` (Boom's
      2 generic actors), `Includes/ZDoomThings.cfg` (two named sub-blocks,
      "doom" and "zdoom", matching `ZDoom_things.cfg`'s own real split -
      "zdoom" nests a `BoomThings.cfg` include exactly like UDB's own real
      nested include), `Includes/GZDoomThings.cfg` (two named sub-blocks,
      "gzdoom" and "gzdoom_lights" - the commented-out, never-active
      "lightmaplights" category in UDB's own real source was correctly
      left out, not silently dropped data). `GZDoomDoom2UDMF.cfg`'s own
      `thingtypes` block now chains all of these via `include()`, in
      UDB's own real order. A source-parsed entry contributing nothing
      this project's own `ThingTypeInfo` model tracks (e.g. a `blocking`-
      only override on an already-defined vanilla entry) is correctly
      omitted rather than emitted as meaningless noise. Verified end-to-
      end with new tests resolving a real thing from each of the 4 newly
      wired layers (a ZDoom generic actor, a GZDoom dynamic light, Boom's
      Pusher, a ZDoom stealth-monster variant) through the actual
      `GZDoomDoom2UDMF` configuration, not just that the files parse.
- [x] 2D Thing rendering, real sprites - replaced the flat single-icon
      circle/notch marker with UDB's own real approach
      (`Renderer2D.RenderThingsBatch`, verified directly against source,
      not guessed): a square (not circle - "things are square in Doom"),
      sized to the type's real radius, with the actual decoded sprite
      drawn on top at its own native colors/aspect ratio, plus a small
      separate arrow only for types with a meaningful facing
      (`ThingTypeInfo.ShowsDirection`). The square is a plain flat fill,
      this project's own choice for "draw a square" rather than a claim
      about matching UDB's own bundled `ThingTexture2D.png` atlas art.
      **The real rotation-frame selection is the substantial part**: two
      things of the identical type facing different directions now
      genuinely show different decoded sprite frames, not the same image
      rotated - `TextureSet.ResolveSpriteRotations` (new) parses the real
      public Doom sprite-rotation lump-naming convention (safe to
      implement directly from well-established public engine knowledge,
      not UDB's own creative content) into an 8-slot table given a
      config's own representative `sprite` string, correctly handling
      Doom's real mirrored-pair optimization (one drawn lump serving two
      opposite rotations, one of them flagged for a horizontal flip at
      draw time - confirmed matching UDB's own real `SpriteFrameInfo`/
      `Mirror` shape) and the "rotation 0 = doesn't rotate, one image for
      every angle" case. `SpriteIconCache.GetOrDecodeRotationFrame` (new)
      caches the resolved table per representative name (cheap, string-
      only) separately from the underlying per-lump pixel decode (shared
      with its own existing single-frame consumer, the Thing dialog's own
      type picker, so a lump referenced by multiple rotation slots or by
      the picker's own preview is only ever decoded once). The angle-to-
      slot formula is UDB's own real one, copied exactly since it's
      simple, objective math, not creative content: `spriteangle =
      ClampAngle(-angle + 270) / 45`. Covered by new `TextureSetTests`
      cases (separate-lump-per-rotation, mirrored-pair, single non-
      rotating sprite, no-match fallback) verifying the parser against
      constructed WAD fixtures, not just eyeballing the render.
- [x] Texture picker (v1) - `TextureBrowserDialog`, ported from UDB's real
      `TextureBrowserForm`: a per-resource tree ("All" plus one node per
      loaded WAD/PK3, matching UDB's real `ResourceTextureSet` tree
      shape) alongside a live-filtered icon gallery; single-click selects,
      double-click/Enter confirms and closes, exactly like UDB. Wired into
      `SectorEditDialog`'s Floor/Ceiling Texture fields via "Browse..."
      buttons. Surfaced and fixed a real gap while building this: wall
      textures only ever came from classic `TEXTURE1`/`TEXTURE2` lumps -
      `TextureSet` now also resolves a plain image sitting in a PK3's
      `textures/` folder as a real wall texture (and symmetrically, a
      PNG-format flat in a PK3's `flats/` folder), matching UDB's actual
      `PK3StructuredReader.LoadTextures` precedence (classic lumps win,
      folder images only fill gaps) - this is the dominant convention for
      modern PK3 content, so this was blocking real use, not theoretical.
      Icons come from a new ambient `TextureIconCache` that starts warming
      the moment a map loads (main-thread, budgeted per frame - never
      blocks, never redone per-feature) rather than decoding on demand
      when the picker happens to open, specifically so a later hover-
      preview feature for lines/sectors can also read from it instantly
      with zero decode logic of its own.
      **Deliberately not built**: UDB's `MatchingTextureSet` category tree
      (separate entry below); PK3 internal folder sub-trees within one
      resource node; "used textures at the top" grouping, width/height
      filter spinners, the All/Textures/Flats/type-mixing combo, "classic
      view" toggle; real background-thread decoding (main-thread frame-
      budgeting instead, to avoid new locking around `TextureSet`'s
      non-thread-safe internal caches); `roottextures`/`rootflats`/the
      text-based `TEXTURES` lump DSL (already-deferred game-config
      options, unrelated to this fix).

      **Update, real shape + reused across dialogs:** the per-field
      preview (Sector's Floor/Ceiling, Linedef's Front/Back Upper/Middle/
      Lower) was originally a horizontal label+small-preview+field row -
      checked directly against UDB's real `ImageSelectorControl` and
      found that's not its actual layout at all (an earlier pass modeled
      it off a screenshot). Extracted a reusable `TexturePreviewEdit`
      matching the real control: preview stacked above a plain name field
      (no caption label, freeing it to be shown much bigger - 96x96 up
      from 40x40) with a floating corner label showing the decoded
      texture's own real pixel dimensions (UDB's real `labelSize`). Also
      fixed two real bugs in `TextureBrowserDialog`'s own gallery, found
      once actually compared side by side with UDB's real
      `TextureBrowserForm`: Godot's `ItemList.max_columns` defaults to 1
      (single column) regardless of `icon_mode`, so the "grid" was
      silently rendering as a plain list - set explicitly to 0 (auto-wrap
      by width); and the resource/category tree was on the wrong side -
      UDB's own real form puts the gallery on the left and the tree on
      the right (`splitter.Panel1`/`Panel2`, confirmed directly in
      `TextureBrowserForm.Designer.cs`), swapped to match. Also enlarged
      the dialog and bumped the default thumbnail size to 128px, matching
      UDB's own real default `ImageSize`.
- [ ] Texture browser category tree (UDB's real `MatchingTextureSet`) -
      UDB's texture/flat browser has a second tree branch alongside
      per-resource grouping: named categories ("Wood", "Metal", "Base",
      etc.) defined in the game configuration's `.cfg` data via
      pattern-matching rules against texture names, entirely independent
      of which WAD/PK3 a texture actually came from. Surfaced while
      building the texture picker's per-resource tree (v1) - deliberately
      deferred there since it needs real new schema, not just wiring:
      `Core.Configuration`/`IGameConfiguration` has zero concept of
      texture categories today (confirmed - no `MatchingTextureSet`-
      equivalent data structure, no `.cfg` parsing for it), so this needs
      its own design pass for the pattern-matching rule format and how a
      category set gets defined/loaded before the browser's tree can grow
      a second branch for it.
- [x] 3D-mode plain-scroll-wheel floor/ceiling height + right-click
      properties - done 2026-09-19. User's own request, confirmed as real
      UDB behavior rather than invented from scratch by decoding its
      default keybind config: the numeric action-key values there
      (`raisesector8`/`lowersector8` = plain wheel, `raisesector1`/
      `lowersector1` = wheel+Shift for a 1-unit fine adjustment,
      `raisebrightness8`/`lowerbrightness8` = wheel+Ctrl) split cleanly
      into a base "wheel up/down" code plus a modifier bit - confirming
      plain scroll really does raise/lower height by 8 in real UDB's own
      default binds, not brightness (that's Ctrl+Scroll specifically,
      still the not-yet-built item directly below). `BaseVisualGeometrySector.OnChangeTargetHeight`/
      `ChangeHeight` confirmed it targets `Sector.FloorHeight`/`CeilingHeight`
      directly (no slopes to consider, matching this project's own scope).
      `MapView.AdjustTargetHeight` - reads `TargetSurfaceKind` off the
      already-built `MapRaycaster`/`_currentTarget` (already distinguishes
      Floor/Ceiling/Wall, exactly what this needed) and applies a
      `SetPropertyCommand<Sector, double>` through the undo stack; a Wall
      target is left untouched, matching the user's own explicit scope.
      Deliberately not ported: UDB's own real per-notch `UndoGroup`
      coalescing (this project's own `UndoStack` has no merging mechanism
      at all yet, so each notch is its own undo step) and extending the
      action to the whole 3D-mode selection at once (`_selectedSectors3D`
      only tracks *which sectors* are selected, not separately *which
      surface* of each the way UDB's own per-surface visual objects do,
      so multi-select-then-scroll would be ambiguous without a real
      redesign of that selection model) - always acts on just the live
      target instead.

      Also added in the same pass: right-click in 3D mode now opens
      Sector or Linedef properties depending on what's targeted (UDB's
      own real `visualedit` action, bound to the right mouse button by
      default exactly like classic mode's own `classicedit` - confirmed
      directly against `BaseVisualGeometrySector`/`BaseVisualGeometrySidedef.OnEditEnd`,
      which call the identical `ShowEditSectors`/`ShowEditLinedefs` this
      project's own 2D-mode dialogs already use, reusing the exact same
      `MapOverlay.RaiseEditSectorsRequested`/`RaiseEditLinedefsRequested`
      wiring). Operates on the whole 3D-mode selection of the *targeted*
      type if any (a wall target always means Linedef properties, even
      with sectors selected elsewhere), else just the targeted element -
      matches UDB's own real `GetSelectedObjects` fallback exactly. Also
      releases 3D mode's own mouse capture before popping the dialog
      (matching `FreeFlyCamera`'s already-established Escape/left-click
      capture toggle) - a popup opened while the OS cursor is still
      captured/hidden would otherwise be unreachable to actually click.
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
- [x] Saving maps / Creating new maps - done together 2026-09-16, planned
      against UDB's own real `MapManager.SaveMap`/`General.NewMap` source
      first (not guessed). New Core: `IO/WadWriter.cs` (mirrors
      `WadFile.Read`'s own format exactly, always a fresh full rebuild -
      matches UDB's own real approach, which cites GitHub issue #531 for
      why it never patches a WAD in place) and `IO/MapFileSaver.cs`
      (`BuildLumpsForSave`/`SaveUdmfMap`, the mirror image of
      `MapFileLoader`) - splices a fresh `TEXTMAP` into whatever a target
      WAD already has: replaces just `TEXTMAP` in an existing UDMF group
      (preserving `BEHAVIOR`/`ZNODES`/etc. byte-for-byte), removes an
      existing *classic* group wholesale and replaces it with a fresh UDMF
      one (saving a classic-format map is a deliberate upgrade-to-UDMF,
      since `UdmfWriter` is this project's only write path), or appends a
      fresh group if the map isn't present at all. `Undo/UndoStack.cs`
      gained real document-dirty tracking (`IsDirty`/`MarkSaved`) - each
      undo-stack entry is stamped with a permanent version id (not a
      simple counter) so undoing back to exactly the last-saved point
      correctly reads as clean again, and redoing past it re-dirties it.
      App layer: `OpenMapMenu.SaveMap`/`SaveMapAs`/`SaveMapInto` (Save
      reuses the already-open file; Save As and Save Into were both
      re-verified directly against UDB's real `MapManager.SaveMap`
      *after* an initial wrong guess shipped and was caught by the user
      hitting real data loss - Save As always rebuilds the destination
      from the *source* map's own resources, discarding whatever
      previously sat at the destination (UDB's own real
      `SavePurpose.AsNewFile`, a literal `File.Copy` of the source before
      ever touching the target); Save Into is the opposite, rebuilding
      from the *target's* own pre-existing content and only touching this
      map's own lump group, warning (UDB's own real prompt text) only on
      an actual same-map-name collision inside the target
      (`SavePurpose.IntoFile`) - both still switch the currently-open
      map's own file association to the target afterward, matching UDB's
      real `filepathname` reassignment exactly, which isn't conditioned
      on save purpose at all), a single `.bak` rename backup before every
      destination overwrite, and a new `MapSaved` event `MainMenuBar` uses
      to call `UndoStack.MarkSaved()`. New `NewMapDialog` (prompts for a map-slot
      name - per explicit user scope call, not UDB's own silent "MAP01"
      default) feeds `OpenMapMenu.ShowNewMapDialog`, which reuses the
      exact same Map Options (game config + resources) flow Open Map
      already has, just with `_pendingWad == null` branches skipping the
      WAD-as-resource-container append and `.dbs` persistence a brand-new,
      unsaved map has nothing to key either of those on yet.
      `MainMenuBar`'s File menu gained New Map.../Save Map/Save Map
      As.../Save Map Into..., with New Map/Open Map gated behind a
      "Discard unsaved changes?"
      confirmation whenever `UndoStack.IsDirty`. A real design gap caught
      and fixed before writing any code: preserving a loaded UDMF map's
      own real `namespace`/unknown-blocks on save (tracked as
      `_currentNamespace`/`_currentUnknownBlocks`, sourced from
      `UdmfDocument` at load time, which the App layer had been silently
      discarding down to just `MapData` until now); a map with no real
      namespace yet (new, or upgraded from classic) defaults to `"zdoom"`
      for this project's one UDMF-native game configuration, `"doom"`
      otherwise (matching `UdmfReader`'s own missing-`namespace` default).
      Test coverage: `WadWriterTests` (round-trip through the existing
      `WadFile.Read` as oracle), `MapFileSaverTests` (all
      `BuildLumpsForSave` branches, plus two realistic full round-trips -
      a WAD with embedded PNAMES/TEXTURE2/patches/flats sitting alongside
      the map's own classic and UDMF groups - decoded back through
      `TextureSet` afterward, not just checked as raw bytes, added while
      chasing the user-reported texture-loss bug above), `UndoStackTests`
      additions for `IsDirty`/`MarkSaved` including the undo-to-exact-
      saved-version and redo-past-it cases. Left explicitly out of scope:
      UDB's real 3-level backup rotation/autosave (v1 does one `.bak`
      rename), a real nodebuilder (`ZNODES`/`BLOCKMAP`/`REJECT` are
      preserved-if-present, never regenerated - GZDoom rebuilds stale/
      missing nodes at runtime), and UDB's real config-driven per-lump
      `MapLumps` table (v1 hardcodes the known UDMF/classic lump-name sets
      instead). Needs real manual verification in the actual Godot app -
      open/edit/save/reopen, New Map/edit/Save As, Save Into onto both an
      empty and an already-populated target WAD, the overwrite/collision
      warnings, and the discard-changes prompt - none of which this
      environment can drive itself.
- [x] Texture browser flats/textures mixing, matching UDB's real
      `mixtexturesflats` - done 2026-09-17. The Sector Floor/Ceiling
      texture browser/preview only ever offered flats, and the Linedef
      wall-texture browser/preview only ever offered wall textures - a
      user report ("I can't find the currently-used texture in the
      browser") traced to GZDoom's own real unified texture manager not
      distinguishing the two for either field. First pass hardcoded
      "sectors always show both, linedefs never do," unconditionally -
      wrong, caught by the user asking whether it had actually been
      checked against UDB (it hadn't). Re-verified directly against
      `MapManager`/`DataManager`/the real `.cfg` files: UDB always keeps
      the field-type split fixed (`FlatSelectorControl`/
      `TextureSelectorControl`, i.e. Sector vs. Linedef, never varies) but
      the *underlying collections* get cross-merged at load time only
      when the active game configuration's own real `mixtexturesflats`
      setting is true - true for the ZDoom/GZDoom-family configs
      (inherited from `ZDoom_common.cfg`), false for vanilla Doom
      (`Doom_common.cfg`'s own explicit `false`, also the real default
      when a `.cfg` doesn't set it at all). New
      `IGameConfiguration.MixTexturesAndFlats`, parsed from a real
      `mixtexturesflats` `.cfg` key, `true` only in `GZDoomDoom2UDMF.cfg`.
      `TextureBrowserDialog.Browse` gained an orthogonal
      `mixTexturesAndFlats` parameter alongside its existing `flats` mode
      bool (Sector still browses flats-first, Linedef still browses
      textures-first - only whether the *other* namespace is also offered
      changed); `TextureIconCache.GetIcon`/`GetOrDecodeIcon(name,
      preferFlat, mixTexturesAndFlats)` resolve icons the same way. Also
      fixed while there: `TextureBrowserDialog.SelectName` was an exact-
      case `IndexOf`, silently failing to preselect/scroll to the current
      selection on any casing mismatch between a map's stored texture
      name and the resource's own real lump casing - now case-insensitive,
      matching every other name lookup in this codebase.
- [ ] Drawing mode (UDB's real "Draw Lines" mode) - **Phases 1+2 done
      2026-09-17**, geometry-stitching rewritten 1:1 against UDB's real
      source **2026-09-19** (see that entry below for the full writeup);
      **Phase 3 (auto-close-across-geometry specifically, plus cardinal-
      direction snap/continuous drawing/rubber-band polish) not started**.
      User's own explicit scope call
      is full UDB parity eventually ("every possible way to draw lines,
      vectors, sectors... ported basically exactly as it is in UDB"),
      phased rather than all at once - offered a further phase split for
      Phase 2 itself (mechanical snap/split vs. the much larger join/
      inheritance half) and explicitly chose to build all of it now rather
      than defer. Planned against several research passes against UDB's
      real source (`DrawGeometryMode`/`Tools.DrawLines`/`FindClosestPath`/
      `FindPotentialSectorAt`/`MakeSector`/`JoinSector`/`Linedef.Split`/
      `EarClipPolygon`/`LinedefTracePath`) - full design in (while it still
      exists) `/home/spood/.claude/plans/steady-bubbling-gosling.md`.

      **Phase 1**: `EditMode.Draw` + `DrawOverlayHandler` - click to place
      points, click back near the first one to close the loop and commit
      a sector (or cancel, if fewer than 3 points - a degenerate loop,
      matching UDB exactly); Escape/right-click cancels, Backspace removes
      the last point. New Core: `Geometry/PolygonWinding.cs` (the shoelace
      formula, extracted out of `Loop.SignedArea()` so both share one
      implementation). Front/back sidedef assignment verified directly
      against `SectorTracer`'s own documented winding convention and
      cross-checked against the existing
      `MapDataTestExtensions.CreateClosedSector` test-helper's own real
      precedent, not guessed.

      **Phase 2**: stitching into existing geometry - the big remaining
      piece, now done. New Core: `Geometry/LinedefSide.cs` (mirrors UDB's
      real type); `Geometry/LinedefAngleSorter.cs` (the exact same angle
      formula `SectorTracer`'s own already-tested `RelativeAngle` uses,
      generalized off `Sidedef` onto the more general `LinedefSide`, reused
      rather than re-derived independently); `Geometry/BoundaryTracer.cs`
      (`FindPotentialSectorAt`/`FindOuterLines`/`FindInnerLines`/
      `FindClosestPath`-equivalent walk over the map's raw vertex/linedef
      topology - reuses this project's own already-built `Loop`/
      `PolygonNesting` for outer/hole validation instead of porting a
      second polygon class the way UDB's own separate `EarClipPolygon`
      exists); `MapData.SplitLinedef`/`AttachOrRetargetSidedef` (the real
      `Linedef.Split`/`JoinSector` primitives - `Sidedef.Sector` is now
      settable to support re-pointing an already-existing sidedef, not
      just creating fresh ones); `Undo/DrawLoopCommand.cs` (replaces
      `CreateSectorLoopCommand` - per edge, the interior side always
      resolves into a new sector, inheriting a neighbor's properties if
      the trace finds one; the exterior side either joins an existing
      neighbor directly - no new sector - or stays void).

      Two real algorithmic bugs found and fixed while writing
      `BoundaryTracerTests` (hand-constructed graphs with known-correct
      expected traces, including a diagonal-split box and a box with a
      hole): the anti-loop tie-break was missing UDB's real "never swap
      away from the start/end line" exception, so a trace could never
      actually close on real geometry; a vertex shared with the outer
      loop's own boundary was being treated as a valid interior hole seed
      (point-in-polygon containment is ambiguous exactly on a boundary
      point) - now excluded explicitly. A third real bug, found by
      reasoning through the "a drawn line splits an existing sector"
      scenario before it could even be tested: `DrawLoopCommand` was
      skipping trace entries that already had a sidedef when populating a
      newly resolved boundary, when what an old sector's own untouched
      sidedefs actually need in that scenario is *re-pointing* to the new
      sector, not being left alone - `AttachOrRetargetSidedefTracked`
      already handled that branch correctly, the outer skip was just
      wrong and has been removed.

      One deliberate simplification, flagged rather than silently
      dropped: `AttachOrRetargetSidedefTracked`'s freshly-created sidedefs
      always get `DrawLoopCommand.DefaultWallTexture` rather than first
      trying to copy a neighboring sidedef's own specific texture name the
      way UDB's real `JoinSector`/`TakeSidedefSettings` does before
      falling back to a default.

      **Post-Phase-2 fix, 2026-09-18**: user reported drawing a loop
      against an existing wall neither made it two-sided/traversable nor
      inherited its neighbor's textures. Root cause: `DrawLoopCommand.Do()`
      always created a brand-new `Linedef` between every consecutive pair
      of resolved points, even when they were already directly connected
      by an existing one (e.g. two points landing on the same old wall) -
      so no drawn loop could ever end up sharing a real edge with existing
      geometry, only a coincident duplicate or an isolated point-touch.
      Fixed via a new `CreateOrReuseEdge` that detects and reuses an
      already-existing coincident `Linedef` instead of duplicating it,
      with `front` for the interior/exterior resolution passes recomputed
      per edge against whether the reused edge's own direction matches
      this loop's own traversal direction. Separately, `MapData.SplitLinedef`/
      `AttachOrRetargetSidedef` never marked any affected *existing*
      sector's `NeedsRebuild` dirty flag (unlike `MoveVertex`, which
      already does via `Linedef.MarkAdjacentSectorsDirty`) - meant a
      reused wall's mesh could pick up the correct Front/Back sectors in
      Core yet never actually rebuild in the running app, since
      `MapView`'s dirty-sector sweep is what triggers `RebuildWallMesh`
      for an *existing* `MeshInstance3D` (a brand-new Linedef gets its
      first mesh for free when the App side notices it, but a reused one
      needs the dirty flag). Both sectors touched by `AttachOrRetargetSidedef`
      (the new target and whichever sector a re-pointed/opposite sidedef
      used to belong to) and both sides of a freshly split linedef are now
      marked dirty. Root-caused via a new failing test reproducing the
      report at the Core level
      (`Do_LoopSharingAWholeExistingWallByBothEndpoints_ReusesItAsATwoSidedWall`)
      before touching any code; a separate, pre-existing test
      (`..._InheritsPropertiesAndBecomesTwoSided`, renamed
      `..._TouchesAtAPointOnlyWithNoSharedWall`) turned out to have a
      wrong expectation of its own - a loop touching old geometry at a
      single vertex only genuinely has no shared wall to inherit from,
      matching real UDB's own identical limitation there.

      **Second post-Phase-2 fix, 2026-09-18 (same session)**: the reuse
      fix above only covered *one* split point per original wall - user's
      own hard test case (a 60-unit new sector sharing only the *middle*
      portion of a wider 128-unit wall) needs *two*. Root cause:
      `DrawPoint.SplitLinedef` captures whichever `Linedef` was hit at
      draw time - for two points on the same still-unsplit wall, that's
      the exact same object reference - and the old per-point
      `ResolveVertex` split each one independently, in whatever order
      `points` happened to list them, always calling `MapData.SplitLinedef`
      straight on that same captured reference. The *first* split
      correctly shrinks it in place; the *second* point's own position
      then usually no longer lies on what that same (now-shrunk) object
      represents, silently producing overlapping/corrupted geometry
      instead of a clean three-way division. Fixed via a new
      `ResolveVertices` that groups same-linedef split points together,
      sorts each group by distance from the original linedef's own
      `Start` (matching the order they actually lie along the wall,
      regardless of `points` order), and splits a running "tail" segment
      sequentially - the first split's own leftover far half becomes the
      second split's real target instead of the stale original. Also
      fixed alongside it, found by re-reading the user's own bug report
      more carefully ("lose its texture and become traversible"):
      `AttachOrRetargetSidedefTracked`'s freshly-created sidedef always
      got `DefaultWallTexture`, even when the wall was *becoming*
      two-sided (the opposite side already existed) - a real, opaque
      texture on both faces of a plain two-sided wall, when UDB's own
      real behavior is `"-"` on both once there's a genuine sector on
      each side (the opposite side's own cleanup to `"-"` was already
      correct; the newly-created side's own texture was the missed
      half).

      **Third fix, same session, App-layer only (no Core change)**: user
      also flagged that `WallMeshBuilder`'s walls being rendered
      genuinely double-sided (`DoubleSidedMesh`, a leftover from before
      front/back sidedefs could carry independently different textures)
      now actively causes wrong-texture-from-the-wrong-side/z-fighting
      once a two-sided linedef's front and back masked-middle textures
      actually differ (`LinedefWallBuilder.BuildTwoSided` already
      correctly builds two independent, spatially-coincident
      `WallSegment`s in that case - one per side's own texture - so
      double-siding *both* draws all four triangle-equivalents of the
      same quad, undefined draw order deciding which texture wins each
      pixel). Fixed by making wall quads single-sided, wound per
      `WallSegment.Side.IsFront`. `TextureCache`'s wall/flat material
      never disabled the engine's own default back-face culling in the
      first place (only the sprite material explicitly does, for its own
      unrelated billboard reason) - the old double-triangle trick was the
      only reason a wall ever looked double-sided at all, so no material
      change was needed, just the winding. First attempt at the winding
      direction was backwards - derived by hand via a right-hand-rule
      cross product against `VectorConversions.ToWorld`'s axis mapping
      and cross-checked numerically, but that derivation implicitly
      assumed a normal-vs-view-direction culling test; Godot's actual
      front-face/culling convention (screen-space triangle winding after
      projection) didn't match it. User caught it immediately by eye
      ("most textures are rendered on the wrong side of the lines") -
      fixed by swapping the two winding branches; no way to unit-test
      this in `DoomArchitect.Core.Tests` at all (Core is Godot-free by
      design), so this one *only* got verified by the user's own visual
      check in the running app, not by an automated test the way every
      other fix this session was. `SectorMeshBuilder` (floor/ceiling) and
      `TargetHighlight` deliberately still use `DoubleSidedMesh`
      unchanged - a sector only ever has one floor/one ceiling texture
      (no front/back conflict possible there), and the target/selection
      highlight isn't textured content at all, so per the user's own
      explicit call, only walls needed this fix.

      **Phase 3 (not started)**: cardinal-direction constrained drawing,
      full auto-close across existing geometry (the general case beyond
      "two consecutive drawn points share an already-existing edge",
      fixed above - splitting a drawn line's path across *several*
      existing linedefs/vertices along an arbitrary route still isn't
      supported, matching UDB's own real `autoclosedrawing` scope),
      `SplitOuterSectors`-equivalent post-pass, continuous drawing mode, a
      real dashed rubber-band line (none exists anywhere in this
      codebase), and `BoundaryTracer`'s own real gap (a trace that lands
      on the wrong loop on its first attempt returns "not found" rather
      than UDB's real rightward-ray-cast retry - see that file's own doc
      comment). Genuinely open (non-closed) polylines aren't supported
      either - `DrawLoopCommand` always closes back to the first point,
      unlike UDB's own real `Tools.DrawLines` (see the right-click audit
      entry below, which ran into this directly).

      **Right-click audit, 2026-09-18**: user's own muscle memory
      ("press right click to draw... in vertex mode") turned out to be
      real UDB behavior this project was missing entirely, not a false
      memory - checked every mode's real right-click ("classicedit")
      behavior directly against UDB's source
      (`VerticesMode`/`LinedefsMode`/`SectorsMode`/`ThingsMode.OnEditBegin`)
      rather than guessing. Found and fixed: right-clicking empty space
      (nothing under the cursor to select/edit) in Vertices/Linedefs/
      Sectors mode now starts Draw mode with the first point already
      placed there (UDB's own real `AutoDrawOnEdit`) -
      `ElementOverlayHandler` gained an optional `onEmptyRightClick`
      delegate, wired on those three handlers via a new
      `MapOverlay.StartDrawingAt`/`DrawOverlayHandler.BeginAt`; Vertices
      mode specifically also gained UDB's own real second priority tier -
      right-clicking near a linedef (not a vertex) splits it immediately
      via a new `SplitLinedefCommand`, without needing Draw mode at all
      (`VertexOverlayHandler` now intercepts right-click itself, ahead of
      the shared engine, to check for this case first). Draw mode's own
      right-click changed from cancelling the whole gesture to UDB's real
      `finishdraw` behavior instead - commits what's drawn so far (Escape
      remains the real cancel, genuinely distinct in UDB, not a synonym) -
      narrowed to "close the loop right now" rather than UDB's more
      general "commit open-or-closed," since `DrawLoopCommand` has no
      open-polyline support (noted above).

      Two related real gaps found in the same audit, deliberately **not**
      built this pass (each is its own separably-sized feature, not a
      right-click wiring fix) - flagged here rather than silently
      skipped: UDB's own real Vertices-mode right-click-with-no-drag on a
      highlighted vertex opens a vertex properties dialog
      (`VerticesMode.OnEditEnd`'s `ShowEditVertices`) - this project has
      no `VertexEditDialog` at all yet, unlike Linedef/Sector/Thing's own
      already-built edit dialogs; and UDB's own real Things-mode
      right-click on empty space inserts a new Thing directly (not Draw
      mode) - this is the same "Adding things" feature already tracked as
      its own TODO entry immediately below, not a new discovery.

      Also added: a live length/angle label per segment while drawing
      (`DrawOverlayHandler.DrawLengthLabel`, UDB's own real
      `LineLengthLabel` - "L:&lt;length&gt;  A:&lt;angle&gt;", shown for
      both already-placed segments and the current rubber-band one) -
      user's own second complaint this session ("it's hard to know what
      you're doing" with no length feedback at all). The perpendicular
      offset that keeps the label off the line itself is deliberately
      computed from the *projected screen points*, not a map-space
      direction scaled afterwards - keeps it a constant pixel offset
      regardless of zoom (UDB achieves the identical result by dividing
      by its own renderer's scale) without needing to reason about
      whether a map-space perpendicular direction even survives
      projection unchanged.

      **Geometry-stitching rewrite, 2026-09-19**: user's own explicit
      mandate after real-world use exposed the previous `CreateOrReuseEdge`
      look-ahead approach as fundamentally unreliable ("the intersection
      rules we have for drawing overlapping lines are exceptionally
      lackluster... we do some parts correctly and some parts wrong") -
      "let's take doom builder's algorithms 1 to 1." A full research pass
      directly against `Tools.DrawLines`/`MapSet.StitchGeometry` and every
      primitive it calls found the real architectural mismatch:
      **UDB never looks ahead** - it draws every consecutive point pair as
      a brand-new `Linedef` unconditionally, then runs a genuinely
      general-purpose reconciliation pass against whatever already
      exists. `CreateOrReuseEdge` was a different design that could only
      ever cover the one case it was explicitly built for.

      `DrawLoopCommand.Do()` rewritten to match UDB's own real "draw
      first, stitch after" shape exactly. New Core:
      `Geometry/GeometryStitcher.cs` (a direct port of `MapSet.JoinVertices`
      x2/`SplitLinesByVertices`/`RemoveLoopedLinedefs`/`JoinOverlappingLines`/
      `FlipBackwardLinedefs`, plus `Tools.DrawLines`' own per-segment
      existing-line-crossing pre-pass, guarded by its own real
      `MINIMUM_INTERSECTION_DISTANCE`); `MapData.MergeVertex`/`JoinLinedefs`
      (the real `Vertex.Join`/`Linedef.Join` primitives, the latter's full
      real sector-matching branching ported exactly - including a couple
      of checks that read as unreachable given the branch they sit in,
      kept rather than "corrected," since UDB's own source has them too);
      `BoundaryTracer.DetermineFrontInterior` (UDB's own real per-linedef,
      geometry-driven interior/exterior determination, replacing the
      `PolygonWinding.IsClockwise` shortcut that only ever worked for a
      single simple polygon and breaks down once stitching can produce a
      self-touching or multiply-connected shape). Confirmed directly
      against source and deliberately *not* ported: UDB's own real
      `SplitLinesByLines` (new-vs-new crossing splitting) is a complete
      no-op in the `CLASSIC` merge mode `Tools.DrawLines` itself always
      uses - a self-intersecting drawn polygon genuinely isn't split by
      real UDB during a normal draw either, not a gap on this project's
      side.

      Verified against the exact scenarios the user reported broken:
      drawing a loop that straddles an existing wall with zero explicit
      snapping (both crossing points now split correctly, mid-wall, with
      no `DrawPoint.OnLinedef` involved at all) and a new edge passing
      straight through an existing T-junction vertex with zero explicit
      snapping (the existing vertex gets picked up, not duplicated) - see
      `DrawLoopCommandTests.cs`'s own `..._WithNoExplicitSnapping_...`
      tests. Of the 11 pre-existing `DrawLoopCommandTests`, only one
      needed changing (an `Assert.Same` on a specific `Linedef` object's
      identity surviving a wall-sharing draw - under UDB's own real
      `JoinOverlappingLines`/`Linedef.Join` semantics, the *newly drawn*
      coincident edge survives and the original is merged into it, not
      the other way around; structurally still fully correct, just no
      longer the same object) - every other test kept passing unchanged.

      This also directly narrows (not fully closes) the "Phase 3"
      auto-close gap noted below: a drawn edge that crosses *several*
      existing linedefs/vertices along its own straight path is now
      handled correctly (the stitch pass), but finding a path *through*
      existing geometry to close an otherwise-open drawn polyline
      (UDB's own real `FindClosestPath`-based gap-closing, gated by
      `autoclosedrawing`) still isn't - remains its own separate,
      deliberately out-of-scope item, confirmed directly against source
      to be architecturally distinct from stitching correctness.
- [x] Adding things - done 2026-09-18. Right-clicking empty space in
      Things mode now places a new Thing there (UDB's own real
      `ThingsMode.OnEditBegin`/`InsertThing`), wired through
      `ElementOverlayHandler`'s existing `onEmptyRightClick` slot (already
      built for Vertices/Linedefs/Sectors' own "start Draw mode" - the
      exact same delegate shape fits here too, just with a different
      implementation). New Core: `MapData.RemoveThing` (the missing
      reverse of the already-public `CreateThing`) and
      `Undo/CreateThingCommand.cs`, with UDB's own real default settings
      (`ProgramConfiguration.ApplyDefaultThingSettings`, verified against
      source, not guessed) - Type 1 (Player 1 Start), Angle 0, classic-
      format `RawFlags` 0b0111 (Easy|Medium|Hard, from `Doom_misc.cfg`'s
      real `defaultthingflags`).

      A related, previously-invisible bug found and fixed while
      implementing this: `MapView`'s own mesh-instance sync
      (`SyncMeshInstancesWithMap`, built earlier this session for Draw
      mode's Sector/Linedef creation) never covered Things at all - a
      newly inserted Thing would have had no `MeshInstance3D` ever
      created for it, invisible in 3D mode. Fixed by adding the same
      count-gated create/remove diff (`SyncThingMeshes`) alongside the
      existing Sector/Linedef ones, mirroring `SyncWallMeshes` exactly -
      no stale-3D-reference cleanup needed there unlike Sector/Linedef's
      own, since Things never participate in 3D-mode targeting/selection
      at all (`HandleThreeDSelectClick` only targets Sector/Wall
      surfaces).

      One deliberate simplification, flagged rather than silently
      dropped: UDB's own real insert continues straight into dragging the
      newly created Thing within the very same mouse gesture
      (`editthings = new List<Thing> { t }`, picked up by its own
      `OnDragStart`) - this project's shared
      `ElementOverlayHandler<TSelectable,TDraggable>` engine has no hook
      for "the element this same press just created is now what should
      drag," so a newly inserted Thing here is created and selected, but
      a separate right-click-drag is needed afterward to reposition it.
      Also not ported: UDB's own real `defaultthingflags` being read from
      the loaded game configuration's own `.cfg` at map-load time (this
      project hardcodes the vanilla-Doom value as a constant instead,
      matching every other real-UDB-default constant already established
      this session - `DrawLoopCommand`'s own texture/height/brightness
      defaults), and its own map-boundary check before inserting
      (`LeftBoundary`/etc.) - this project doesn't model map boundaries
      anywhere else either.

      **Follow-up, same day**: `DefaultThingType` being real session
      state (not a fixed constant) *was* built after all, once the user
      asked for it directly - UDB's own real behavior confirmed by
      reading `ThingEditFormUDMF.cs`: every time the thing-edit dialog
      applies a Type value (`General.Settings.DefaultThingType = thingtype.GetResult(...)`),
      that becomes the default for the *next* inserted Thing, regardless
      of whether the dialog was opened for a fresh insert or an
      already-existing one. Ported as `MapOverlay.LastUsedThingType`
      (plain in-memory session state, outliving a single map load/unload
      since `MapOverlay` itself does - not disk-persisted, this project
      has no settings-file mechanism at all yet) - `CreateThingCommand`
      gained an explicit `type` constructor parameter (defaulting to
      `DefaultType` for callers, like Core tests, that don't have a
      "last used" concept of their own), `ThingOverlayHandler.InsertThingAt`
      passes `LastUsedThingType`, and `ThingEditDialog.ApplyRealTimeType`
      reports every resolvable typed value back out via a new
      `onTypeChanged` callback (wired in `MainMenuBar.OpenThingEditDialogFor`)
      so editing an existing Thing's type updates the default too, not
      just inserting a new one.

      **Second follow-up, same day**: user also asked to confirm/add
      UDB's own real right-click-without-dragging behavior across every
      mode - verified directly against `VerticesMode`/`LinedefsMode`/
      `SectorsMode`/`ThingsMode`'s own real `OnEditBegin`/`OnEditEnd`:
      `OnEditEnd` (mouse-up) only ever opens the properties dialog when
      no drag actually started (`OnDragStart` switches to a *different*
      mode object entirely, e.g. `DragLinedefsMode`, so the original
      mode's own `OnEditEnd` never even fires on mouse-up once a real
      drag began). This project's `ElementOverlayHandler`'s own right-
      click-release already distinguished "did the position actually
      change" for its own move-command bookkeeping - the exact same test
      doubles as the drag-vs-click distinction UDB's own gesture needs,
      so no new state was needed, just one more branch: a still-`Hovered`
      element on a release that moved nothing now also invokes the
      existing edit-dialog delegate (renamed `onDoubleClick` -> `onEdit`,
      since it's no longer only reachable from a double-click - this
      project's own left-double-click convenience is kept alongside it,
      not replaced, since it's harmless and doesn't conflict).

## Known concerns

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

