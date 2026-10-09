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
the toolbar/context-menu "Add Script"/"Add Library" actions started as
a real, working skeleton (enabled/disabled by the current selection's
own kind) with their actual behavior as explicit follow-up work, same
for script compiling/texture consolidation/"Bake". "Add Script" is now
real - see its own "Update" section below; "Add Library" and the rest
are still follow-up work.

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
  continuity with the tabs; everything unrecognized falls back to the
  generic lump/file icon). The rest of the set started as hand-rolled
  SVGs this first pass, later replaced with real UDB-sourced icons -
  see "Real icons instead of AI-generated ones" below.
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

## Bug fix: a map's own WAD could show up twice in the tree

Reported after loading a map WAD that physically lives inside a folder
*also* configured as a resource - the same file showed up twice, once
as the folder's own flat nested entry, once again as its own separate,
fully-expanded top-level entry. Fixed in `OpenMapMenu`, not here - see
[Open Map... UI](mapio-open-map-ui.md)'s own "Update" section (new
`IResourceContainer.ContainsFile`, plus a new ability to open a PK3-
style resource folder directly from "Open Map...", scanning its own
`maps/*.wad` files).

## Highlighting the currently open map

The tree now visually marks whichever `MapGroup` item is the map
actually open right now - the app's own established accent color
(`Color(0.85, 0.55, 0.3, 1)`, already used for a `LineEdit` focus border
and a `Button`'s pressed-icon tint in `Assets/BaseTheme.tres`, reused
rather than inventing a new one) plus a "Currently open" tooltip.
`OpenMapMenu` gained `CurrentMapContainer` (the exact `IResourceContainer`
instance backing the loaded map - mirrors the existing `CurrentMapName`)
so `Refresh` can match by real object identity instead of a path string;
`OnMapOptionsConfirmed` now updates its own `_current*` fields *before*
firing `MapLoaded` rather than after, so this is accurate the moment the
event reaches a subscriber, not one map load behind.

**Update:** also now covers a map loaded from inside a folder (deduped
per the section above), reported missing right after this first landed -
a nested `maps/MAP01.wad` has no `MapGroup` entry of its own to mark
(still just a flat, unexpanded file leaf), so matching by container
reference doesn't apply there. New `IResourceContainer.ResolveAbsolutePath`
(`DirectoryResource` resolves a relative path back to its own real
on-disk path; `WadFile`/`Pk3File` always null, same reasoning as
`ContainsFile`) plus a new `OpenMapMenu.CurrentWadPath` let the browser
resolve a folder-nested `File` leaf's own real path and compare it
directly against the open map's real file, independent of whether that
file also has a top-level entry anywhere.

## Bug fix: tree sometimes stayed empty despite a map being loaded

`AppShell.AddMapTab()` subscribed to `OpenMapMenu.MapLoaded`/
`MapResourcesChanged` *after* adding `mapDocument` to the (eventually)
live tree - fine for the normal interactive File > Open Map... flow,
whose confirmation fires from a dialog on a later frame, long after
`AddMapTab()` has already returned. But the dev-only `--file`/`--map`
command-line launch flags (`OpenMapMenu.LoadFromCommandLine`) load a
map *synchronously*, from inside `MapView._Ready()` itself - which is
exactly what entering the tree triggers. A subscription added after
that point missed the one and only `MapLoaded` firing entirely,
leaving the browser panel permanently empty despite a map genuinely
being loaded and editable. Fixed by resolving `OpenMapMenu` directly
via `GetNode` (not `MapView.OpenMapMenu`, which stays null until
`_Ready()` runs) and subscribing before `mapDocument` ever enters the
tree.

## Real icons instead of AI-generated ones

The original SVG icon set (`icon_wad`/`icon_pk3`/`icon_folder`/
`icon_lump`/`icon_geometry`/`icon_texture_def`) was hand-rolled for the
first pass. Replaced with real UDB assets, sourced two ways:

- UDB's own `TextureBrowserForm` has a WAD/PK3/folder/texture-set tree
  conceptually close to this one - but its icons are embedded in a
  Windows-only `ImageListStreamer` blob inside the `.resx`, not
  standalone files. Mono ships an actual runtime WinForms
  implementation (confirmed present locally), so a small throwaway
  Mono/C# program (`ResXResourceReader` + `ImageList.ImageStream` +
  `Image.Save`) deserialized the real `ImageList` and dumped each
  frame as a PNG. `icon_wad.png`/`icon_pk3.png`/`icon_folder.png`/
  `icon_lump.png`/`icon_texture_def.png` are `WadTextureSet.png`/
  `PK3TextureSet.ico`/`FolderTextureSet.ico`/`TextLump.png`/
  `KnownTextureSet2.ico` from that form, extracted byte-for-byte.
  `.import` files for all of these were generated the same way a
  fresh asset normally would be, via `godot --headless --import`
  (no Godot editor session was opened to do it).
- VERTEXES/LINEDEFS/SIDEDEFS/SECTORS/THINGS lumps split from one
  shared `icon_geometry` into four real, distinct icons -
  `icon_vertices.png`/`icon_linedefs.png`/`icon_sectors.png`/
  `icon_things.png`, copied directly from UDB's own classic edit-mode
  toolbar (`VerticesMode.png`/`LinesMode.png`/`SectorsMode.png`/
  `ThingsMode.png`) - real, standalone files this time, no extraction
  needed. SIDEDEFS reuses the linedef icon; UDB has no separate
  sidedef edit mode of its own to borrow from.

`icon_chevron.svg` (the browser's own collapse/reopen toggle) stays
the hand-rolled one - it's generic UI chrome, not a Doom-specific
concept UDB would have an equivalent for.

## Update: "Add Script" is real now

Right-click (or select + toolbar button) a map's own `MapGroup` node,
with no `SCRIPTS` lump of its own yet, → "Add Script" → creates that
map's own `SCRIPTS` lump and opens it, the same tab-opening path an
*existing* `SCRIPTS` lump already uses. Disabled once the map already
has one (no legitimate case for a second).

Checked UDB's own real behavior before building this: it has **no
equivalent action at all**. Its own Script Editor always shows a tab
for every map lump the current game configuration declares as
`scriptbuild = true`, regardless of whether that lump currently exists
- the lump gets created implicitly whenever the map is next saved. Not
a case of "should have copied UDB and didn't" - that model is coupled
to a "map editor with an embedded script sub-tab-strip" UI UDB has and
this project doesn't; the resource browser's own tree/right-click
Open-or-Add interaction model is already a confirmed, deliberate,
independent design (this doc's own opening paragraph - "no UDB
equivalent exists"), and this action was already part of that
skeleton before this landed.

Debated one real wrinkle before settling the design: writing a
genuinely empty lump the instant "Add Script" is clicked means
clicking it by mistake and closing without typing leaves a pointless
empty lump behind. Resolved by seeding new content with a small,
genuinely useful boilerplate (`#include "zcommon.acs"` - confirmed
`#include`, not `#import`, is the real directive for a header file
like this, against this project's own BCS work in
`TODO/bcs-lsp-foundation.md`) rather than deferring the write until
first save or introducing a virtual, not-yet-real tree node (the
browser's tree model doesn't have one anywhere else, and a virtual
node would need its own parallel open-path).

- New `WadFile.WithAddedScriptsLump(lumps, markerIndex, data)` - the
  `SCRIPTS` counterpart to `WithReplacedLumpData`, inserted as the new
  last lump of a classic/Hexen map's own group, or right before a UDMF
  one's own `ENDMAP` terminator (`WadMapLumpNames.Classic`/`.Udmf`'s
  own real lump order - `SCRIPTS` is last in both). Reuses
  `FindGroupEnd`, the same primitive `FindMapLumpGroups`/`BuildTree`
  already go through.
- New `ResourceBrowserPanel.AddScriptRequested` event (mirroring
  `OpenRequested`), new small `ResourceAddScriptRequest` record. New
  `AppShell.OnAddScriptRequested` does the actual write (`.bak`-then-
  overwrite, matching the existing WAD-lump-save convention - this is
  a WAD operation, not the no-backup PK3 one), then opens the new lump
  through the existing `OnResourceOpenRequested` lump-shaped path.
- **The refresh problem**: adding a lump changes the WAD's own
  content, but nothing before this ever needed to refresh the browser
  *structure* - every existing lump-save path only ever changes a
  lump's bytes. New `AppShell.RefreshBrowserForExternallyChangedWad`
  rebuilds just the one `NamedResource` whose path matches the WAD
  that was just written (a fresh `WadFile.Read` - `WadFile` is
  immutable once constructed), leaving every other already-loaded
  resource untouched, then re-calls `ResourceBrowserPanel.Refresh`.

## Update: nested per-map WADs (`maps/MAP01.wad`) are real lump trees now, "Add Script" included

Reported live right after "Add Script" landed: it only worked on a
*top-level* `MapGroup`. A mod folder split across several small
per-map WADs inside a real GZDoom/ZDoom-convention `maps/` subfolder
(`maps/MAP01.wad`, `maps/MAP02.wad`, ...) showed each one as a flat
`File` leaf - `IsCurrentlyOpenMap`'s own remarks already documented
this exact gap ("a nested `maps/MAP01.wad` doesn't get expanded into
its own lump structure, just shown as a plain file"). Now it expands
into a real lump tree, exactly like a top-level WAD resource - its own
`SCRIPTS`/other lumps browsable, "Add Script" usable on it directly.

Scoped to `DirectoryResource` (a loose, unzipped pk3-style folder)
only - not `Pk3File` (a real zipped `.pk3`). A WAD nested inside an
actual zip has no on-disk path of its own to write back to at all
(`Pk3File.ResolveAbsolutePath` always returns null), so "Add Script"
could never work for it regardless - browsable-but-not-editable would
be a confusing, inconsistent half-feature, matching the PK3
write-back work's own already-established scope boundary for the
identical case. Also scoped to a top-level `maps/` folder specifically
(not "any `.wad` anywhere"), matching `OpenMapMenu.FindMapsSubfolder`'s
own already-established convention, rather than inventing a broader
rule or eagerly reading an unrelated loose `.wad` a mapper might keep
around for some other reason.

- New `PathTreeBuilder.ExpandNestedWads(root, readFile)` - a second
  pass over an already-built tree (kept separate from `Build` itself,
  which stays the one generic, WAD-unaware "fold paths into a tree"
  primitive `Pk3File` also uses unchanged): replaces any `.wad`-named
  file directly inside a top-level folder literally named `maps` with
  its own real lump structure (`WadFile.BuildTree`), falling back to a
  plain file leaf on any read/parse failure - a stray or corrupt
  `.wad` shouldn't break browsing the rest of the folder. The
  replacement node deliberately keeps the original leaf's own `Path`
  (needed to resolve back to the real file) while taking on the nested
  WAD's own `Kind`/`Children` - exactly the signal
  `ResourceBrowserPanel` needs to tell "a nested, expanded WAD root"
  apart from the top-level resource's own synthetic root (which
  `WadFile.BuildTree` never gives a `Path` to at all).
- `DirectoryResource.BuildTree` wires it in, re-reading each nested
  WAD's current bytes fresh every call (`File.ReadAllBytes`, matching
  this class's own existing "nothing cached" design) - a later write
  to one is picked up automatically on the next refresh.
- `ResourceBrowserPanel.AddTreeItem` re-points `owningContainer`/
  `ownerSourcePath` the instant it crosses into one of these expanded
  roots (via `ResourceContainerCache.Open`, the same cache every other
  resource open already goes through) - everything downstream
  (`BuildOpenRequest`'s `MapGroup`/`Lump` cases, "Add Script",
  `IsCurrentlyOpenMap`'s own existing `MapGroup` match) already works
  generically off `TreeItemContext`, so none of them needed to change
  at all once this one re-point was correct.
- `AppShell.OnAddScriptRequested` gained one new
  `ResourceContainerCache.Invalidate` call - needed so the re-point
  above doesn't serve a stale cached instance for a just-modified
  nested WAD on the very next refresh (the enclosing folder's own
  `DirectoryResource` needs no equivalent invalidation - it caches no
  bytes of its own to begin with).

## Update: the tree no longer collapses itself on every refresh

Reported right after the above landed, since drilling into a nested
WAD's own lump list made it immediately obvious: `Refresh` tears down
and rebuilds every `TreeItem` from scratch on *every* call - a map
load, a resource change, opening something, "Add Script" - and
`AddTreeItem` always started a node with children collapsed,
unconditionally. Nothing remembered what the user had actually drilled
into, so every one of those silently snapped the whole tree back to
fully collapsed, even though nothing about what was expanded had
actually changed.

New `_expandedKeys` (a `HashSet<string>`, keyed by
`{ownerSourcePath}\0{node.Path}` - a node's own stable identity across
a rebuild, not the doomed-to-be-discarded `TreeItem` itself) -
`Refresh` snapshots every currently-expanded item's own key right
before tearing the tree down (`SnapshotExpandedState`), and
`AddTreeItem` consults it instead of unconditionally collapsing a node
with children. `CollapseAll` needed no changes at all - it collapses
the *live* tree directly, and the next snapshot just sees that
(correctly) as nothing being expanded anymore.

## Update: auto-reveal the active tab in the browser

Requested right after the expansion-state fix above: VSCode always
keeps its Explorer's own selection synced to whichever file is
actually open/focused. This panel already *highlighted* the
currently-open map (`IsCurrentlyOpenMap`), but nothing selected/
scrolled to it, and Script tabs had no "this is open" indicator in the
browser at all.

Discussed as a toggle rather than an unconditional default, since
auto-revealing force-expands whatever ancestors are needed to show the
active item, which can directly fight the collapse-state preservation
landed just above (deliberately collapse a folder, then switching tabs
pops it back open to reveal something inside it). Settled on: on by
default, with a real off switch in Preferences, matching the same
reasoning an equivalent "Explorer: Auto Reveal" setting exists for
elsewhere.

New `AppSettings.GetAutoRevealActiveTab`/`WithAutoRevealActiveTab`
(default `true`, mirrors `GetImmersive3DView`/`WithImmersive3DView`
exactly), a new "General" tab in `PreferencesDialog` with one
`CheckBox` (no existing tab fit - the other three are all per-game-
config resource/keybind/test-engine editors, not a plain app-wide
behavior toggle).

Two new public `ResourceBrowserPanel` methods, both reusing matching
logic that already existed rather than inventing new identity rules:
`RevealActiveMap` reuses `IsCurrentlyOpenMap` directly - the exact rule
already driving the color-highlight, so a map is "revealed" by the
same check that already calls it "open". `RevealActiveScript` runs
`BuildOpenRequest` (the same resolution "Open" itself uses) in
reverse - searching the tree for whichever node *would* produce a
request matching the already-open `ScriptDocument`, compared via its
own already-public `FilePath`/`IsLumpFrom`/`IsPk3EntryFrom`. Both
funnel into a shared private `RevealMatching`: walk every item, and on
a match, force-expand only *that item's own* ancestors
(`item.GetParent()` walked in a loop, not a blanket expand-all), then
`Select`/`ScrollToItem` - the same expand-then-select-then-scroll
sequence already proven elsewhere in this project
(`ThingTypePicker`/`LinedefActionBrowserDialog`/
`SectorSpecialBrowserDialog`, though those only need one `GetParent()`
level for their own fixed-depth trees). No match (e.g. a loose script
opened outside any configured resource) is a silent no-op, same as
`IsCurrentlyOpenMap` already tolerates.

`AppShell.Activate` calls the matching reveal method after its
existing per-tab-kind refresh/highlight call (ordering matters for the
Script-tab branch - `ClearCurrentMapHighlight` rebuilds `_itemContexts`
first), gated by `AppSettingsFile.Load().GetAutoRevealActiveTab()` read
fresh each time rather than cached, consistent with how this project
already treats every other on-disk setting read from here.

## Update: fixed a latent "currently open" match gap for nested per-map WADs

Found while testing the auto-reveal feature above: switching to a map
tab loaded from a folder's own nested `maps/MAP01.wad` never
selected/scrolled to anything, even though the toggle and the reveal
methods themselves were correct. Traced it to `IsCurrentlyOpenMap`'s
`MapGroup` branch, which only ever matched by `ReferenceEquals` against
`currentMapContainer` - fine for a top-level map (that container really
is the exact same instance), but never true for a nested one:
`OpenMapMenu.PromptMapOptionsForPendingMap` deliberately reads that
one map's own per-map WAD with a bare `WadFile.Read` (a fresh instance
every time, by design - see its own remarks on texture-cache identity),
while the tree's own re-point for that same nested WAD goes through
`ResourceContainerCache.Open` instead - two different container
instances for the same file, by original design on both sides. This
was a pre-existing gap from the nested-WAD work above, not something
the reveal feature introduced - it just made the silent non-match
obvious (nothing selects) where before it only cost a missing, easy-
to-miss orange tint.

Fixed by giving `IsCurrentlyOpenMap`'s `MapGroup` branch a path-based
fallback - comparing the node's own resolved `ownerSourcePath` against
`currentMapWadPath` (via a new shared `PathsEqual` helper) whenever the
reference check fails, the same identity the `File` branch already
used for exactly this reason. Both the color highlight and the new
reveal/select now agree for every case.

## Update: double-clicking a nested per-map WAD opens its first map directly

Requested as a usability shortcut: with a folder's `maps/` subfolder
expanded into real lump structure (see above), opening the one map a
`maps/MAP01.wad` actually holds took one extra unfold-then-click step
(open the `.wad` node, then double-click the `MapGroup` inside it) -
one layer more than opening a top-level map needs. Real per-map WADs
are one map per file in practice, so there's nothing meaningful to
pick between.

`BuildOpenRequest` gained a case for exactly the node
`AddTreeItem`'s own re-point already targets - a
`ResourceTreeNodeKind.WadContainer` with its own `Path` set (the
nested, expanded root, as opposed to a container's own synthetic
root, which never sets one) - resolving to its first `MapGroup` child
(same "take the first" rule the un-expanded fallback path,
`BuildNestedMapWadRequest`, already used for a nested `.wad` the
expansion doesn't reach). No new gating needed anywhere else - the
context menu's "Open" item and the double-click handler both already
key off `BuildOpenRequest`'s own result being non-null.

## Explicitly deferred, not forgotten

Real `Add Library` behavior (the `#library "name"` template with an
automated name/lump-name prefix so the two don't have to match - a
library isn't scoped to one map, unlike a script, so it needs its own,
different UI entirely), script compiling (no `acc`/`bcc`/`zt-bcc`
invocation exists anywhere in this project yet - `BEHAVIOR` is
currently only ever preserved byte-for-byte on save, never generated),
texture/resource consolidation, and the "Bake"/"Bake As..." export flow
this whole panel is the foundation for.
