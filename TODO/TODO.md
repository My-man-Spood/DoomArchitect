# DoomArchitect roadmap

A from-scratch Linux/Godot reimagining of Ultimate Doom Builder. This
folder replaces the old single 3600-line `TODO.md` - that file grew
unworkable, so it's been split: this index holds a short status table per
item, and anything with real detail (why it was built a certain way, what
was ported from UDB's real source and how, what got deliberately cut and
why, bugs found and fixed along the way) lives in its own file next to
this one, linked from the table.

## How to use this

- **Looking for what's left to build?** Read the "Open" table below first -
  it's short on purpose.
- **Looking for how/why something already works the way it does?** Find
  its row in the "Done" table and open the linked file. That file is the
  authoritative writeup - don't re-derive or re-ask about a decision
  that's already documented there.
- **A row with no linked file** means the item was small enough that its
  one-line note in the table *is* the whole record - nothing more to
  read.
- **Adding a new item**: add a row to the right table (Open if it's not
  started, Done once it lands). If it needs more than a one-line note -
  which is almost always true once real work starts on it, or once a
  deferred sub-piece needs tracking - create `TODO/<kebab-case-slug>.md`
  for it and link it from the row, following the shape of any existing
  detail file (a `# Title` / `**Status:**` / `**Area:**` header, then the
  actual writeup).
- **Finishing an item**: move its row from Open to Done (or just flip its
  Status if you keep one combined table locally - this file currently
  splits them into two tables for readability), update its detail file
  with what actually happened, and note anything genuinely deferred out
  of it with a reason and a revisit trigger - never cut something without
  writing down why, per this project's own standing rule (see
  `feedback_udb_is_north_star` / the "we're building the whole thing"
  principle in project memory).
- **Status values**: `Pending` (not started), `Done` (shipped). Add
  `In Progress` if something spans multiple sessions and you want that
  visible; nothing here currently uses it since work tends to land in one
  pass.

## Reference notes

Standing design rationale, not a feature to track status on - kept here
so it isn't lost, not because it belongs in the tables below.

- [Architecture notes](architecture-notes.md) - the "one parser per format" decision.

## Open

| Status | Area | Item |
|---|---|---|
| Pending | Rendering | [Full-bright toggle + real brightness editing](rendering-fullbright-brightness-editing.md) |
| Pending | Geometry | [Slopes / 3D floors](geometry-slopes-3d-floors.md) |
| Pending | Rendering | [Dynamic/animated 3D lighting](rendering-dynamic-lighting.md) |
| Pending | Textures | [Texture browser category tree](textures-browser-category-tree.md) |

## Done

### Foundation

| Item | Note |
|---|---|
| Godot + Core project scaffold | Godot C# scaffold - one live 3D scene, ortho 2D camera + perspective 3D camera, switching is just a camera swap. |
| DoomArchitect.Core as a Godot-free library | Split out as a plain class library, structurally enforced (no GodotSharp reference). |
| Core map data model (Vertex/Linedef/Sidedef/Sector) | `Vertex`/`Linedef`/`Sidedef`/`Sector`/`MapData` aggregate root, with dirty-tracking on vertex move. |
| Core.Tests xUnit project | `DoomArchitect.Core.Tests`, runs via plain `dotnet test`, no Godot engine needed. |
| Vector2/3 Core <-> Godot conversion helpers | `VectorConversions` bridges `System.Numerics`/`Godot` vector types - no built-in bridge exists. |

### Geometry

| Item | Note |
|---|---|
| [Sector boundary tracing (SectorTracer)](geometry-sector-boundary-tracing.md) | |
| Polygon triangulation for floor/ceiling meshes | `PolygonNesting`/`PolygonCutter`/`EarClipper` - full trace -> nest -> cut -> ear-clip pipeline, holes included. |
| [Auto-remove orphaned vertices when a linedef is removed](geometry-vertex-auto-cleanup.md) | Fixes a real reported bug - `DeleteSectorsCommand`/`DeleteLinedefsCommand` left orphaned vertices behind. |
| [Slopes / 3D floors](geometry-slopes-3d-floors.md) | **Pending** - see Open table above. |

### Rendering

| Item | Note |
|---|---|
| [SectorMeshBuilder (Core triangles -> Godot ArrayMesh)](rendering-sector-mesh-builder.md) | |
| Free-fly 3D camera | WASD + mouse-look + Space/Shift for up/down, for visually checking rendering work. |
| Dirty-sector mesh rebuild loop | `MapView` rebuilds only what `MapData.GetDirtySectors()` flags, then clears it. |
| Camera render layers (2D floor-only view) | Ceiling on its own render layer; the top-down camera's cull mask excludes it. |
| [Sidedef upper/middle/lower wall mesh generation](rendering-sidedef-wall-mesh-generation.md) | |
| [Sector lighting + fake contrast](rendering-sector-lighting-fake-contrast.md) | |
| [2D Thing rendering (real sprites)](rendering-2d-thing-sprites.md) | |
| [Full-bright toggle + real brightness editing](rendering-fullbright-brightness-editing.md) | **Pending** - see Open table above. |
| [Dynamic/animated 3D lighting](rendering-dynamic-lighting.md) | **Pending** - see Open table above. |

### 2D editing

| Item | Note |
|---|---|
| [2D overlay layer (grid, vertices, linedefs)](editing-2d-overlay-layer.md) | |
| Click/drag to move vertices in 2D | Nearest-vertex pick + camera-ray/ground-plane drag, calls `MapData.MoveVertex`. |
| Basic edit modes (Vertices/Linedefs/Sectors) | `EditMode` enum, matching UDB's own numeric-key mode split (1/2/3). |
| [Per-mode hover + drag interactions](editing-2d-hover-drag-interactions.md) | |
| [Grid snapping](editing-2d-grid-snapping.md) | |
| Scroll-wheel zoom (2D view) | Zooms toward the cursor position, not the map origin, 20..2000 `Size` clamp. |
| [Adaptive multi-tier grid](editing-2d-adaptive-grid.md) | |
| [Dynamic grid size](editing-2d-dynamic-grid-size.md) | |
| [Toolbar UI (mode/grid/status bar)](editing-2d-toolbar-ui.md) | |
| [Undo/redo command stack](editing-undo-redo-stack.md) | |
| [Delete actions (Vertices/Linedefs/Sectors/Things)](editing-delete-actions.md) | |
| [Dissolve action (Vertices/Linedefs)](editing-dissolve-action.md) | |
| [2D marquee/box-select](editing-2d-marquee-select.md) | |
| [2D view panning](editing-2d-view-panning.md) | |
| [Drawing mode (Draw Lines)](editing-2d-drawing-mode.md) | |
| [2D tag/action indicators](editing-2d-tag-action-indicators.md) | |

### 3D editing

| Item | Note |
|---|---|
| [3D targeting + highlighting](editing-3d-targeting-highlighting.md) | |
| [3D-mode height edit + panning](editing-3d-height-edit-panning.md) | |
| [3D-mode Thing hover/select/edit](editing-3d-thing-hover-select-edit.md) | |
| [3D-mode texture nudge + auto-align](editing-3d-texture-nudge-autoalign.md) | |

### Map I/O

| Item | Note |
|---|---|
| [UDMF text format parser/serializer](mapio-udmf-parser-serializer.md) | |
| [WAD reader](mapio-wad-reader.md) | |
| [Classic binary-format map reader](mapio-classic-binary-map-reader.md) | |
| [Fix: north-south mirroring bug](mapio-fix-north-south-mirroring.md) | |
| Load a real map end to end | WAD -> UDMF text -> `MapData` -> rendered, opening a real id Software WAD works. |
| [Open Map... UI](mapio-open-map-ui.md) | |
| [Saving maps / creating new maps](mapio-save-new-maps.md) | |
| [Scope map settings (.dbs) to the whole mod, not the individual WAD](mapio-mod-scoped-settings.md) | Fixes per-map-WAD-in-a-folder settings never being shared. |

### Textures

| Item | Note |
|---|---|
| [Texture pipeline (Doom picture/flat/patch decode)](textures-pipeline-decode.md) | |
| [Texture picker (v1)](textures-picker-v1.md) | |
| [Texture browser category tree](textures-browser-category-tree.md) | **Pending** - see Open table above. |
| [Texture browser flats/textures mixing](textures-browser-flats-textures-mixing.md) | |

### Things

| Item | Note |
|---|---|
| [Things data model](things-data-model.md) | |
| [Things edit mode](things-edit-mode.md) | |
| [Adding things (right-click insert)](things-adding-things.md) | |

### Game config

| Item | Note |
|---|---|
| [Game configuration system (.cfg, thing types, ZScript/DECORATE discovery)](gameconfig-system-and-zscript-decorate-discovery.md) | The big one - includes the full live ZScript/DECORATE actor-discovery port and the gzdoom.pk3 required-archive fix. |
| [Thing-type catalog (full GZDoom/ZDoom/Boom breadth)](gameconfig-thing-type-catalog.md) | |

### Test Map

| Item | Note |
|---|---|
| [Test Map (F9)](testmap-feature.md) | |

### Resources

| Item | Note |
|---|---|
| [Multi-resource support (WadResourceSet)](resources-multi-resource-support.md) | |
| [PK3 resource loading](resources-pk3-loading.md) | |
| [Resource browser panel (VSCode-style source tree)](resources-browser-panel.md) | First pass - visibility/structure only, no UDB equivalent exists. |
| [Open files/lumps from the resource browser](browser-open-action.md) | Double-click/"Open"; brought real multi-Map-tab support with it. |
| [PK3 write-back (open/save scripts inside a real .pk3)](mapio-pk3-write-back.md) | Closes the pk3-embedded-scripts gap `browser-open-action.md` deferred. |

### Input

| Item | Note |
|---|---|
| [Keybinding management](input-keybinding-management.md) | |

### Property editing

| Item | Note |
|---|---|
| [Property editing foundation](propedit-foundation.md) | |
| [Sector property editing UI (v1)](propedit-sector-dialog-v1.md) | |
| [Linedef property editing UI (v1)](propedit-linedef-dialog-v1.md) | |
| [Thing property editing UI (v1)](propedit-thing-dialog-v1.md) | |

### Architecture / code health

| Item | Note |
|---|---|
| [MapOverlay.cs god-object cleanup](architecture-mapoverlay-god-object-cleanup.md) | |

### UI / App shell

| Item | Note |
|---|---|
| [Document tabs (map editor + script tabs)](documents-and-tabs.md) | |
| [Map view SubViewport fix + immersive full-view 3D mode](ui-map-subviewport-immersive-3d.md) | Fixes the browser/map-view stacking bug; adds an opt-in full-window 3D mode. |

### Scripting / LSP

| Item | Note |
|---|---|
| [BCS tokenizer + parser + minimal LSP server](bcs-lsp-foundation.md) | |
