# Open files/lumps from the resource browser

**Status:** Done
**Area:** Resources

Double-click or right-click > "Open" in the resource browser now does
something real, for four kinds of content: loose `.acs`/`.bcs`/`.zs`/
`zscript` files, a WAD's `SCRIPTS` lump, a WAD's map lumps (e.g.
`MAP01`), and a WAD's `ZSCRIPT` lump. `.pk3`-embedded scripts were
deliberately out of scope this pass - `Pk3File` had no write path
anywhere in this codebase, and opening something you can't save back is
worse than not offering it.

**Update:** that follow-up landed - see
[PK3 write-back: open and save scripts inside a real .pk3](mapio-pk3-write-back.md).

**Update:** opening a map lump this way surfaced a related, separate
bug - two maps from the same pk3-style folder (split across
`maps/MAP01.wad`/`maps/MAP02.wad`) each got their own, unrelated
`.dbs` settings file, so configuring the folder as a resource for one
did nothing for the other. Fixed, not here - see
[Scope map settings (.dbs) to the whole mod, not the individual WAD](mapio-mod-scoped-settings.md)
(which also covers two further refinements and a shared resource/
texture caching pass on top of all this, reported in later testing).

**Update:** the startup tab specifically (not any tab opened
afterward) kept showing the placeholder `"Map"` title even once a real
map loaded into it via the dev-only `--file`/`--map` command-line
flags. `AppShell.CreateMapViewTab` did its tab bookkeeping
(`_tabContents.Add`/`_tabBar.AddTab`) *after* `AddChild(mapViewportContainer)` -
but that `AddChild` call is exactly what synchronously fires
`MapLoaded` for that one launch path (already called out in the
method's own remarks, for a different reason - it's also why the
`MapLoaded`/`MapResourcesChanged` subscriptions themselves are wired
before `AddChild`). `UpdateMapTabTitle`'s `_tabContents.IndexOf(mapDocument)`
came back `-1` at that point, silently no-opped, and that one-time
synchronous load was the only `MapLoaded` the startup tab would ever
get - nothing left to correct the title afterward. Every
later-opened tab was never at risk: their own `MapLoaded` always fires
from a dialog confirmation on a later frame, long after
`CreateMapViewTab` has already returned. Fixed by moving the tab
bookkeeping before `AddChild` - which traded it for a *different*
crash, reported live immediately after: `_tabBar.AddTab("Map", ...)`
going from zero tabs to one makes `TabBar` auto-select the new tab as
current and synchronously emit its own `TabChanged` signal, which now
(moved earlier) ran `OnTabChanged`/`SwitchTo`/`Activate` against
`mapDocument` before it had even entered the tree - `MapView.SetTabActive`
null-referenced on fields `_Ready()` hadn't set up yet
(`_overlayLayer`). Exact same class of problem, same fix, as
`OnTabClosePressed`'s own `RemoveTab` call (its own remarks cover the
general pattern: `TabBar` can synchronously emit signals as a side
effect of its own mutation methods, independent of and before a
caller's own bookkeeping) - `TabChanged` disconnected for the
duration of the `AddTab` call, reconnected right after; the method
already calls `SwitchTo` explicitly once `mapDocument` is actually
ready, so the signal-driven path was never needed here anyway.

## Multi-Map-tab support (a real prerequisite, not a side effect)

Opening a map lump from the browser opens or focuses a *dedicated* tab
for that map, not the single previously-existing one - which meant real
multi-Map-tab support had to land first, a materially bigger change than
"add an Open button":

- `AppShell.AddMapTab()` became `CreateMapViewTab()` - instantiates a
  blank `MapView`, wraps it in its *own* `SubViewportContainer`/
  `SubViewport` (two `MapView`s can't share one - each owns its own
  `TopDownCamera`/`PerspectiveCamera`, and only one `Camera3D` can be
  `Current` per `Viewport`), and is now reusable: called once at
  startup, by `OpenMapTab(wadPath, mapName)` (the browser's own entry
  point - focuses an already-open tab if the same WAD path + map name
  is already open, matched the same normalized-path way
  `DirectoryResource.ContainsFile` already compares paths elsewhere;
  otherwise creates a tab and loads it via the new
  `OpenMapMenu.OpenSpecificMap(wadPath, mapName)`, which still shows the
  normal interactive Map Options confirmation - no new silent-auto-
  confirm shortcut), and by `MainMenuBar.CreateMapTabRequested` (below).
  The single `_mapViewportContainer` field became
  `Dictionary<MapView, SubViewportContainer> _mapViewportContainers`;
  `UpdateMapViewportLayout` now derives immersive-mode state fresh from
  whichever tab is *currently active* (`MapView.In3D`, new public
  getter) instead of a sticky field, which would otherwise go stale the
  moment the active tab becomes something that isn't a `MapView` at all.
- **Every tab is closable, including the original one** - there's no
  "never zero map tabs" invariant; closing the last tab just leaves a
  plain background, which is fine, no empty-state UI to build.
  `OnTabClosePressed` frees a Map tab's own `SubViewportContainer`
  wrapper (a *parent* of the `MapView`, not a sibling - freeing the
  `MapView` alone would've left the wrapper behind) and guards against
  calling `SwitchTo` with an invalid index once the tab list is empty.
- **Zero active map tabs exposed a real, previously-unreachable gap**:
  `MainMenuBar.Initialize(OpenMapMenu, MapOverlay)` used to be a one-time
  method that both assigned those two fields *and* unconditionally built
  the File/Map menus - calling it again per tab-switch (the obvious first
  instinct) would have duplicated every menu item and stacked a second
  `IdPressed` subscription. Split into `BuildMenus()` (called once at
  startup) and `SetActiveMap(OpenMapMenu, MapOverlay)` (re-callable,
  called by `AppShell.Activate`/`Deactivate` every time the active tab
  changes - to a different map, a Script tab, or none at all, each
  correctly re-pointing or clearing "Save Map"/"Map Options..."/etc.'s
  own target). `SetActiveMap`'s own per-`OpenMapMenu` event wiring
  (`MapSaved`, the three `MapOverlay` edit-request events) is now
  guarded by a `HashSet<OpenMapMenu>` so switching back to an
  already-visited tab doesn't re-subscribe and fire those handlers
  twice - and explicitly guarded against a null `openMapMenu` too
  (`SetActiveMap(null, null)` is how "no active map" is represented;
  `HashSet<T>.Add(null)` itself succeeds, so skipping the wiring
  specifically needs its own null check, not just the `Add` result).
  "New Map..."/"Open Map..." pressed with no active map tab has no
  existing `OpenMapMenu` instance to act on at all (one only ever lives
  inside a `MapView`'s own scene) - new `MainMenuBar.CreateMapTabRequested`
  callback lets `AppShell` create a fresh blank tab first, then show
  that tab's own dialog on it.

## WAD lump open/save

- `ResourceTreeNode` gained `LumpIndex` (nullable int) -
  `WadFile.BuildTree()` already iterates by index, just threaded
  through. Needed because a WAD can have more than one lump sharing a
  name (a Hexen-format WAD's own per-map `SCRIPTS` lump, one per map) -
  matching "the SCRIPTS lump" by name alone can't tell those apart, and
  saving back has to target the exact right occurrence.
- `WadFile.WithReplacedLumpData(lumps, index, newData)` (new, static) -
  the one generic "replace this lump's data, leave everything else
  untouched" primitive, deliberately separate from `MapFileSaver`'s own
  splicing logic (confirmed that's specific to a map's own marker-to-
  end group, not a standalone named lump). `WadWriter.Write` needed no
  changes - already fully generic, always a full rebuild, never an
  in-place patch, consistent with how every other save path here
  already works.
- `ScriptDocument` became backing-source-agnostic: `LoadFile(path)`
  unchanged for the existing loose-file case; new `LoadLump(wadPath,
  lumpIndex, lumpName, data)` populates the editor directly from
  already-read bytes. `Save()` branches accordingly - the lump case
  re-reads the WAD fresh, splices via `WithReplacedLumpData`,
  `WadWriter.Write`s, backs up (`.bak`) then overwrites - the exact
  same pattern `OpenMapMenu.WriteMapToFile` already uses for map saves.
  A `SCRIPTS` lump gets real BCS syntax highlighting (its content *is*
  genuine ACS source, same grammar `.acs`/`.bcs` files already use) -
  `ZSCRIPT` (lump or loose file) opens as plain text; no ZScript
  highlighting exists in this project yet, and building one is a
  separate, much larger undertaking (the `.acs`/`.bcs` highlighter took
  five dedicated phases). `BEHAVIOR` is explicitly excluded from
  "openable" - it's compiled ACS bytecode, not text; opening/saving it
  through a `CodeEdit` would corrupt it.

### Update: saving a lump, then reopening it, served the pre-save content

Real, reported bug - the lump-save path never called
`ResourceContainerCache.Invalidate(wadPath)` after writing, unlike
`SavePk3Entry` (`mapio-pk3-write-back.md`), which already did. The
write to disk itself was always correct; anything that reopened the
same WAD afterward through the cache (the resource browser's own tree
rebuild, a freshly-opened script tab) kept serving the stale,
pre-save `WadFile` instance instead of re-reading. Fixed by adding the
same `Invalidate` call `SaveLump` was missing - now mirrors
`SavePk3Entry` exactly.

## Resource browser wiring

- `NamedResource` gained `SourcePath` - the full path was already known
  at construction time (`OpenMapMenu.OnMapOptionsConfirmed`) but only
  the bare filename was kept; needed so the browser can resolve "what
  real file backs this top-level resource" independent of
  `IResourceContainer.ContainsFile`/`ResolveAbsolutePath` (which only
  ever answer for a `DirectoryResource`'s own *nested* entries, never a
  top-level container's own path).
- `ResourceBrowserPanel` wires `Tree.ItemActivated` (double-click/Enter,
  previously unused) and a real "Open" context-menu item alongside the
  existing "Add Script"/"Add Library" skeleton - both resolve through
  the same `BuildOpenRequest`, which returns null (nothing happens) for
  anything not openable. New `ResourceOpenRequest` (plain Godot-layer
  record) carries everything `AppShell` needs without having to re-walk
  the tree: the owning resource's own `SourcePath`, and exactly one of
  `MapName`/`FilePath`/(`LumpName`+`LumpIndex`+`LumpData`). `AppShell`
  dispatches: `MapName` set -> `OpenMapTab`; `FilePath` set -> the
  existing file-backed script-tab flow (resolved via
  `IResourceContainer.ResolveAbsolutePath`, which a `Pk3Container` entry
  never does - the mechanism that already, automatically excludes
  `.pk3`-embedded files with no extra special-casing); otherwise the new
  lump-backed flow, with its own "already open -> focus it" dedup
  (`ScriptDocument.IsLumpFrom`, the lump-backed counterpart of
  comparing `FilePath` directly).

## Verification

- `dotnet build DoomArchitect.sln` (Godot project included) and
  `dotnet test src/DoomArchitect.Core.Tests/DoomArchitect.Core.Tests.csproj`
  (1043 tests, including new coverage for `WithReplacedLumpData` and
  `BuildTree`'s `LumpIndex` threading, specifically a multi-map WAD with
  two same-named lumps at different indices - the whole reason lump
  identity needed an index, not just a name).
- Needs a thorough manual pass, not just "does it compile" - this
  touches map-tab architecture, WAD save-back, and script-document
  internals: opening a loose `.acs` file; opening `SCRIPTS` from inside
  a WAD and confirming edits survive a save + app restart, with every
  *other* lump in that WAD untouched; opening `ZSCRIPT` as plain text;
  opening a map lump into a brand-new tab, opening it again and
  confirming it focuses the existing tab instead of duplicating;
  closing a Map tab (including the original startup one) down to zero
  tabs with no crash; switching between two open Map tabs and
  confirming "Save Map"/"Map Options..." always act on whichever one is
  actually active; confirming those same File/Map menu actions no-op
  rather than crash with a Script tab (or no tab at all) active; and
  "New Map..."/"Open Map..." from the File menu with zero tabs open,
  confirming they create a fresh tab and show the right dialog on it.
