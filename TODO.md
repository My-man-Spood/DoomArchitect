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
- [ ] Basic edit modes (Vertices / Linedefs / Sectors), matching UDB's
      mode split
- [ ] `Core.Undo`: command-based undo/redo stack (pure Core, no Godot)

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

## Process

- [ ] CI (GitHub Actions): run `dotnet test` on `Core.Tests` on every
      push - cheap to set up now since it needs no Godot install at all
