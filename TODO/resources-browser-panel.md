# Resource browser panel (VSCode-style source tree)

**Status:** Done (first pass - visibility/structure only)
**Area:** Resources

A permanent, collapsible left-side panel showing every currently-loaded
source (the map's own WAD, plus whatever additional WAD/PK3/folder
resources are configured) as a real, navigable tree - map marker lumps
(`MAP01`, etc.) fold their own lump run into a group, the way a WAD
actually structures them; PK3/folder resources show their real nested
folder structure. Grew directly out of a design conversation about output
format, script organization, and resource consolidation ("Bake") - the
recurring blocker across all three was not being able to *see* what's
actually inside a loaded source. No UDB equivalent exists (confirmed via
its real source - its own "Explorer"-named panels are all map-*data*
visualizers like Reject/Blockmap, not resource/file browsers) - this is
genuinely new, independently-designed UI, not a port.

Deliberately scope-limited to **visibility and structure** this pass -
the toolbar/context-menu "Add Script"/"Add Library" actions are a real,
working skeleton (enabled/disabled by the current selection's own kind)
but their actual behavior - creating a lump, a lump-backed script
document, a `#library` template - is explicitly follow-up work, same for
script compiling/texture consolidation/"Bake".

## What was built

- **Unified the three existing "find this map's own lump group" copies**
  (`WadFile.FindUdmfMapNames`/`FindClassicMapNames`, `ClassicMapReader`'s
  own `MapLumpNames` array + scan loop, `MapFileSaver`'s own two name
  arrays + `FindGroupEnd`/`FindMarkerIndex`) into one real shared
  implementation: `WadMapLumpNames.Classic`/`.Udmf` (the single source of
  truth for both name sets), `WadFile.FindMarkerIndex`/`FindGroupEnd`
  (the shared scanning primitives), and new `WadFile.FindMapLumpGroups()`
  (every map in a WAD, each with its own ordered lump list) built on top
  of them. `ClassicMapReader`/`MapFileSaver` both refactored to call the
  shared primitives instead of their own private copies - their own
  existing tests are the regression net; zero behavior change confirmed
  by running the full suite before touching a single test.
- **New `IResourceContainer.BuildTree(string displayName)`** - the
  interface had exactly 3 targeted-lookup methods before this, no
  enumeration at all. New `ResourceTreeNode`/`ResourceTreeNodeKind`
  (`src/DoomArchitect.Core/IO/ResourceTreeNode.cs`) is the container-
  agnostic shape a UI walks generically regardless of source kind.
  `WadFile.BuildTree` re-walks the same marker/group-end logic
  `FindMapLumpGroups` uses (sharing the scanning primitive, not built
  from its output - a loose lump has no representation in
  `MapLumpGroup` at all) to fold map groups in while keeping every other
  lump flat, in **real file order** (meaningful to preserve, unlike a
  PK3/folder's own arbitrary dictionary order). New
  `PathTreeBuilder.Build(...)` (`src/DoomArchitect.Core/IO/PathTreeBuilder.cs`)
  is the one shared implementation of "fold a flat list of `/`-delimited
  paths into a real nested tree," reused by both `Pk3File.BuildTree` and
  `DirectoryResource.BuildTree` (both already held an identically-shaped
  flat path dictionary) instead of writing the nesting logic twice -
  sorts its own output (folders before files, alphabetically), matching
  the sorted file-explorer convention the whole panel is modeled on.
- **`ResourceBrowserPanel`** (`Scenes/UI/ResourceBrowserPanel.tscn` +
  `Scripts/View/ResourceBrowserPanel.cs`) - a `Tree` (first use of this
  control for a file/resource browser specifically, though `Tree` and
  `PopupMenu` were both already proven elsewhere in this codebase -
  `ThingTypePicker`, `LinedefActionBrowserDialog`, `MainMenuBar`/
  `TestMapToolbar` - not actually a first use of either control) with a
  toolbar strip above it (`Add Script`/`Add Library` placeholder buttons
  + a collapse-arrow button) and a right-click `PopupMenu` context-menu
  skeleton, both gated by the current selection's own `ResourceTreeNodeKind`
  (`Add Script` only for a `MapGroup`, `Add Library` only for a
  container root) and both currently just surfacing a "not implemented
  yet" stub dialog. New `ResourceTreeIcons.cs` is the one shared icon
  lookup (a curated set - container kinds, map-group/script lumps reuse
  the existing `document_map.svg`/`document_script.svg` for visual
  continuity with the tabs; new `icon_wad.svg`/`icon_pk3.svg`/
  `icon_folder.svg`/`icon_lump.svg`/`icon_geometry.svg`/
  `icon_texture_def.svg`/`icon_chevron.svg` cover the rest - deliberately
  not exhaustive, everything unrecognized falls back to the generic lump/
  file icon).
- **Wired into `AppShell`** as a permanent sibling of `ContentArea`
  (confirmed via exploration: `ContentArea`'s own children are the
  per-tab swap target, hidden en masse on tab switch - the panel has to
  live outside that, not inside it, to survive every tab switch
  untouched). `_contentArea`'s own `OffsetLeft` is adjusted by the
  panel's real measured width - the same deferred-measurement pattern
  `AlignMapToolbarBelowTabStrip` already uses for its own vertical
  offset, never a hand-picked constant - both on initial layout and on
  every collapse/expand toggle. New `toggle_resource_browser` keybinding
  (`Ctrl+B`, the same real-world convention VSCode itself uses for
  toggling its own sidebar - a deliberate nod given the whole panel was
  explicitly modeled on that UI) in `KeyBindingRegistry`, read via a new
  `AppShell._UnhandledInput` (it had none before - the right home
  specifically because the panel is tab-independent, same reasoning as
  it being a `ContentArea` sibling rather than living inside any one
  tab's own input handling).
- Data flow: `AppShell` subscribes to `OpenMapMenu.MapLoaded`/
  `MapResourcesChanged` (already-existing events, already carrying
  exactly the `IReadOnlyList<NamedResource>` shape needed - no new
  plumbing required there at all) and calls `ResourceBrowserPanel.Refresh`
  on both, so the tree rebuilds itself automatically on every map load or
  resource-list change.

## Verification

- `dotnet test src/DoomArchitect.Core.Tests/DoomArchitect.Core.Tests.csproj` -
  new tests for `FindMapLumpGroups`, `BuildTree` on all three container
  types, `PathTreeBuilder` directly (nested folding, a file at root
  alongside a folder, empty input), plus the full existing suite (1032
  tests, zero regressions - confirms the `ClassicMapReader`/`MapFileSaver`
  refactor is real behavior-preserving de-duplication, not a silent
  change).
- `dotnet build DoomArchitect.sln` - including the Godot C# project
  itself (compiles against the real GodotSharp API surface, catching any
  `Tree`/`PopupMenu`/node-path mismatch at compile time).
- The actual panel layout, tree population from a real loaded WAD/PK3,
  icons, collapse/expand, and context-menu skeleton have no automated
  coverage in this codebase's own established convention for Godot-layer
  UI - needs manual confirmation in the running app, same as every other
  Godot-layer feature.

## Bug fix + redesign: no way back in once collapsed

The only control that toggled the browser was its own `CollapseButton`,
living inside the panel's own toolbar - collapsing the panel hid that
toolbar along with everything else, leaving only the (undiscoverable)
`Ctrl+B` keybinding as a way back in. A first fix added a second,
always-present button outside the panel, but it was a plain sibling
`Control` added to the tree before the Map tab's own code-created
`SubViewportContainer` - once that container expanded to reclaim the
browser's space on collapse, it drew *on top of* the button, hiding it
again (a `Control`'s own sibling order, not its `Visible` state,
decides draw order on the shared base canvas layer).

Replaced both the original in-panel button and that first fix with one
control: `BrowserToggleButton` (`TextureButton`, `Scenes/Main.tscn`),
living on its own dedicated `BrowserToggleLayer` `CanvasLayer` so it
always draws above the base layer regardless of what else gets added
there later. It tracks the browser's own right edge (sitting just
outside it when open, at the screen's left edge when closed), stays
vertically centered on the browser's own height, flips its chevron
icon (`TextureButton.FlipH` - plain `Button` has no such property,
which is why this control type specifically) to indicate which way it
will act, and - per request - reads as a nearly-invisible edge handle
rather than a toolbar button: 0.25 alpha at rest, tweened to full
opacity while either it or the browser itself is hovered. Hidden
entirely during immersive 3D mode (see
[Map view SubViewport fix + immersive full-view 3D mode](ui-map-subviewport-immersive-3d.md)),
same as every other piece of chrome that mode hides.

## Explicitly deferred, not forgotten

Real `Add Script`/`Add Library` behavior (creating a lump, a lump-backed
`ScriptDocument` variant mirroring UDB's own real `ScriptLumpDocumentTab`/
`ScriptFileDocumentTab` split, the `#library "name"` template with an
automated name/lump-name prefix so the two don't have to match), the
actual context-menu actions beyond the stub, script compiling (no `acc`/
`bcc`/`zt-bcc` invocation exists anywhere in this project yet - `BEHAVIOR`
is currently only ever preserved byte-for-byte on save, never generated),
texture/resource consolidation, and the "Bake"/"Bake As..." export flow
this whole panel is the foundation for.
