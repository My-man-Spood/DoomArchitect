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
- [ ] `App`: `System.Numerics.Vector2/3` <-> `Godot.Vector2/3` conversion
      helpers (no built-in bridge between them, confirmed via reflection)
- [ ] `App.Rendering`: `SectorMeshBuilder` - Core triangles -> Godot
      `ArrayMesh`, one `MeshInstance3D` per sector (not one merged map
      mesh - that's what keeps a single vertex edit cheap)
- [ ] Wire the rebuild loop: App polls `MapData.GetDirtySectors()` and
      rebuilds only those meshes, clearing the flag after
- [ ] Camera render layers: hide ceiling meshes from the top-down ortho
      camera (`cull_mask`) so "2D view" shows floors, not ceilings

## Next up (editing basics)

- [ ] Screen-space overlay layer for 2D-view gizmos (grid, vertex knobs,
      linedef color-coding) via `Camera3D.unproject_position()`, kept
      separate from the 3D mesh so it stays crisp at any zoom
- [ ] Click/drag to select and move vertices in the 2D view, calling
      `MapData.MoveVertex`
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
