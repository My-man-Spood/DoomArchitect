# Document tabs (map editor + script tabs)

**Status:** Done  
**Area:** UI / App shell

Part of the long-term "one-stop-shop Doom modding app" vision: a
VSCode-style tab strip as the app's main navigation, eventually hosting
maps, scripts, a SLADE-style WAD browser, a sprite editor, a MAPINFO GUI,
etc. This entry covers the first concrete step: the tab infrastructure
itself, with the existing map editor folded in as a tab (not left as a
separate untabbed main view - a deliberate choice, confirmed with the
user, to avoid redoing this once multi-map/WAD-browser/sprite-editor tabs
get built) and a new, deliberately simple generic text/script tab type.

## What was built

- `Scenes/MapDocument.tscn` - today's map-editing UI (3D scene nodes, the
  2D overlay `CanvasLayer`, the toolbar/status bar `CanvasLayer`),
  extracted out of what used to be `Main.tscn`. `MapView.cs` is otherwise
  unchanged internally - see `SetTabActive` below for the one real touch.
- `Scenes/Main.tscn` + `Scripts/View/AppShell.cs` (new) - the app's own
  root shell, top to bottom: a static `MainMenuBar` (File/Edit/Map/
  Preferences), a `TabBar` (not `TabContainer` - see below), then the
  active tab's own content. The menu bar and tab strip each live on their
  own `CanvasLayer` (15 and 20) above every map document's own "UI" layer
  (10), so they reliably render in front regardless of scene-tree
  nesting. Instances one `MapDocument.tscn` as the first tab on startup
  (title "Map", `document_map.svg` icon) - not closable yet, since
  there's no "no map open" empty state.
- **The menu bar is app-level chrome, not part of the map document.** It
  originally stayed inside `MapDocument.tscn` (matching the plan, and the
  least-change option), but that turned out wrong once tried live: the
  whole map tab - menu included - hid itself whenever a Script tab was
  active, so "File > Save Map"/"Open Script..." disappeared exactly when
  you'd want them. Moved to `Main.tscn`, owned by `AppShell` (which calls
  `MainMenuBar.Initialize` once, using accessors pulled off the one Map
  tab - `MapView.OpenMapMenu`/`MapView.Overlay`), and no longer touched by
  `SetTabActive` at all. This also resolved the "Unifying MainMenuBar"
  item this entry originally deferred - it happened now, not later,
  because the bug forced the question early.
- "Open Script..." lives in that menu bar's File menu (not a dedicated
  button) - wired via `MainMenuBar.OpenScriptRequested`, set once in
  `AppShell._Ready`.
- `Scenes/UI/ScriptDocument.tscn` + `Scripts/View/ScriptDocument.cs`
  (new) - a plain-text tab: one `CodeEdit` (Godot's own built-in code
  editor control - line numbers enabled via `gutters_draw_line_numbers`,
  folding for free) plus a header showing the open file's path,
  load/save-to-disk, Ctrl+S (new `save_document` keybind). Deliberately
  not language-aware - no ACS/BCS/ZScript syntax highlighting or compiler
  integration yet; "script" here just names the kind of file this tab is
  for.
- `MapView.SetTabActive(bool)` (new, small, additive method on
  `MapView.cs`) - needed because `MapDocument.tscn`'s root is a `Node3D`,
  which can't be hosted as a plain `Control`-managed `TabContainer` page
  the way a `ScriptDocument` tab can - only one `Camera3D` can be
  `Current` across the whole viewport at a time, and `CanvasLayer`
  content renders independently of ordinary scene-tree visibility.
  Toggles both cameras' `Current`, the 2D overlay layer's `Visible`, the
  toolbar/status-bar's `Visible` (status bar's own visibility is shared
  with the pre-existing `toggle_2d_3d` 2D/3D toggle via a small
  `UpdateStatusBarVisibility` helper, so neither clobbers the other),
  `ProcessMode` (stops `_Process`/`_UnhandledInput` for the subtree when
  inactive), and mouse capture - restoring whichever of 2D/3D this tab
  was last actually in on reactivation, rather than switching between
  them.
- Three new small hand-drawn icons matching the existing flat
  white-stroke style (`icon_tag.svg` etc.): `Assets/Icons/document_map.svg`/
  `document_script.svg` (tab icons), `icon_close.svg` (the tab strip's own
  close button - Godot's `TabBar` close icon isn't covered by
  `icon_max_width`, so a small-canvas 10x10 source was needed to actually
  render small).
- `DocumentTabStripPanel`/`DocumentTabBar` theme variations in
  `Assets/BaseTheme.tres` - a darker background than the menu bar's so the
  strip visually stands out as its own band, and tight tab content
  margins instead of Godot's default padding.

## Real adjustments made during implementation (tracked here, not silently dropped)

- The originally approved plan assumed Godot's `TabContainer` (already
  used elsewhere, e.g. `PreferencesDialog`) could host the Map tab
  directly with zero changes to `MapView.cs`. Building it surfaced a real
  constraint: `TabContainer` only manages `Control`-type children as
  pages, and even if it tolerated a `Node3D` child, the single-
  `Camera3D.Current`-per-viewport rule would still need explicit
  coordination regardless of which widget manages the tab strip. Switched
  to `TabBar` with `AppShell` manually activating/deactivating each tab's
  content (`SetTabActive` above).
- The menu bar moved from the map document up to `AppShell` mid-build,
  for the reason described above - a real, reported bug, not a planned
  step.
- `AppShell`'s `ContentArea` (and `AppShell` itself) are full-screen plain
  `Control`s - a kind of node that never existed in this app's scene tree
  before (the map used to be the scene root outright). Godot's default
  `mouse_filter` for `Control` is `Stop`, so both were silently
  swallowing every mouse click/wheel event in the map view before they
  ever reached `MapView`'s own `_UnhandledInput` handling - a real,
  reported regression ("none of the map view's actions work anymore"),
  fixed by setting `mouse_filter = 2` (Ignore) on both, matching the
  pattern `MapOverlay` already used for the same reason.

## Deferred, tracked, not cut

- **A WAD-browser tab type** (SLADE-style) and **a sprite-editor tab
  type** - both named in the long-term vision, neither started.

## Update: multi-Map-tab support landed

The "Multi-map tabs" item this entry originally deferred (several Map
tabs open at once, the exact blocker named below) landed as a
prerequisite of [Open files/lumps from the resource browser](browser-open-action.md) -
opening a map lump from the browser needed somewhere real to open it
*into*. `MainMenuBar.Initialize` (the named blocker - wired to one
specific Map tab's `OpenMapMenu`/`MapOverlay`, no way to re-point it)
split into `BuildMenus()` (one-time structure) and the re-callable
`SetActiveMap` - see that entry's own "Multi-Map-tab support" section
for the rest (per-tab `SubViewportContainer`, every tab now closable
down to zero including the original one, ACS/BCS syntax highlighting
inside `ScriptDocument` also landed there alongside it, scoped to
`SCRIPTS` lumps and `.acs`/`.bcs` files specifically - still no ZScript
highlighting, and still no compiler integration).

"Per-tab close button visibility" is also moot now - every tab,
including the original Map tab, is genuinely closable, so the no-op
guard that entry described no longer exists at all.
