# Scope map settings (.dbs) to the whole mod, not the individual WAD file

**Status:** Done
**Area:** Map I/O

Reported live: a mod's maps split across several small per-map WADs
inside one pk3-style folder (the real GZDoom/ZDoom convention -
`maps/MAP01.wad`, `maps/MAP02.wad`, ...) each got their own, completely
separate `.dbs` sidecar (`MapSettingsFile.PathFor` is literally
`Path.ChangeExtension(wadPath, ".dbs")` - one sidecar per WAD file,
sitting right next to it, the same convention UDB itself uses).
Configuring the enclosing folder as a resource for MAP01 did nothing
for MAP02 - a different file, a different `.dbs`, re-asking for game
config/resources from scratch and never auto-loading the folder at all.

Confirmed from `MapSettings.cs`'s own doc comment: "per-WAD" scoping is
real, deliberate, UDB-accurate behavior *only* when a WAD genuinely
*is* the whole mod (a true standalone multi-map WAD, where "per-WAD"
and "per-mod" are the same thing anyway). It was never the right
granularity for a mod that splits its maps across several files inside
one folder - a gap, not a case needing a special fallback. The real
concept is **per-mod** settings, where "mod" resolves to either the
standalone WAD or the enclosing folder depending on how that mod
happens to store its maps - genuinely new ground, no UDB precedent
(same as this project's own resource browser and multi-Map-tab support
already are). No migration path for old per-individual-WAD `.dbs`
files - confirmed explicitly that scoping was never the intended model,
so there's nothing worth a fallback to; re-confirming once for the mod
as a whole is expected.

## What was built

- **`src/DoomArchitect.Core/IO/ModRootDetector.cs`** (new) -
  `DetectFrom(wadPath)`: if the WAD's own immediate parent directory is
  literally named `maps` (matching `OpenMapMenu.FindMapsSubfolder`'s
  own already-established convention, reused here in reverse), the mod
  root is that parent's own parent; otherwise the WAD is its own mod
  root. Pure string logic, no I/O, applied **unconditionally** every
  time a map is opened - not just when explicitly browsing a folder -
  matching "always load the whole pk3 style folder if it's detected".
- **`MapSettings.GetFolderResources()`/`WithFolderSettings(kind, resources)`** (new) -
  parallel to the existing `GetResources(mapName)`/`WithMapSettings(mapName, kind, resources)`
  but *not* nested per map name, since there's no legitimate case for
  two maps from the same folder wanting different resources (unlike a
  true multi-map WAD, where the existing per-map nesting is real,
  confirmed UDB format and stays exactly as-is, untouched).
  `gameconfig` reuses the exact same already-whole-file-scoped
  top-level field `WithMapSettings` already writes - no change needed
  there, it was already shared across every map in one `.dbs`.
- **`OpenMapMenu`**: `_pendingFolderPath` (previously: set only by
  `OnDirSelected`, used only to pre-fill one extra resource path)
  replaced by `_pendingModRootPath`, computed in exactly one place -
  `PromptMapOptionsForPendingMap`, right after `_pendingWadPath` is
  finalized - which covers a plain file pick, a folder scan, and
  `OpenSpecificMap` (the resource browser's own map-open entry point)
  all at once, since all three already funnel through that one method.
  `LoadFromCommandLine` (a separate, non-interactive entry point) gets
  the same one-line detection directly. New `_currentModRootPath`
  (mirrors `_currentWadPath`) fixes a related bug along the way:
  `ShowMapOptionsForCurrentMap` used to reset the folder context to
  null on every revisit, rather than reusing what was already known.
  `PopulateMapOptionsDialogDefaults` branches on `Directory.Exists(modRootPath)`
  to pick folder-flat vs. per-map-name resource reading/writing, and
  always ensures a detected folder root is itself in the pre-filled
  resource list (subsuming the old, narrower `additionalResourcePath`
  parameter entirely, dropped rather than kept alongside the new logic).

Nothing about *loading map data* or *saving the map itself* changed -
`_pendingWad`/`_pendingWadPath`/`_currentWad`/`_currentWadPath` still
mean exactly what they already did (which specific WAD file to
read/write map bytes to/from); the mod-root fields are purely about
which `.dbs` file configuration lives in.

## Refinements after the first manual pass

Three follow-ups, reported together after trying the above in-app:

1. **Don't re-ask for resources opening a second map from the same
   mod.** `OpenMapMenu.PromptMapOptionsForPendingMap` now checks a new
   `HasSavedFolderSettings(modRootPath)` (a folder root with both a
   saved game configuration *and* at least one saved folder resource)
   before ever popping the dialog - if true, it goes straight to
   `PopulateMapOptionsDialogDefaults` + `OnMapOptionsConfirmed()`, the
   same "populate state without popping it, then confirm directly"
   shortcut `LoadFromCommandLine` already used. Scoped to the
   folder-rooted case only - a standalone WAD can still legitimately
   have different per-map resources, so it keeps showing the dialog
   every time, same as before.
2. **Tab title should be the map name, not the hardcoded `"Map"`.**
   New `AppShell.UpdateMapTabTitle(mapDocument)`, wired into both the
   `MapLoaded` and `MapResourcesChanged` events `CreateMapViewTab`
   already subscribed - reads `mapDocument.OpenMapMenu.CurrentMapName`.
3. **Don't re-open/re-decode the same resource containers for a second
   map from the same mod.** Traced the actual cost before touching
   anything: `ResourceListEditor.SetResourcePaths`/`OnFileSelected`
   called `ResourceContainerFactory.Open` fresh every time, for every
   tab, with zero caching anywhere. A WAD resource is genuinely
   expensive to re-open (`WadFile.Read` eagerly copies every lump's
   bytes into memory); a PK3 is cheap to open (`Pk3File.Open` only
   indexes zip entries) but the real loss on "redundant" reopening is
   each `TextureSet`'s own per-instance lazy wall/flat/sprite decode
   caches - a fresh instance always starts cold, even for
   byte-identical bytes another open tab already decoded.
   `Pk3File` is the one container type holding a live, disposable
   handle (`FileStream`+`ZipArchive`) for its whole lifetime -
   `WadFile`/`DirectoryResource` hold none and are immutable after
   construction, trivially safe to share. Checked whether anything
   disposes a container today before designing around it: nothing
   does, anywhere in the codebase, except an unrelated throwaway
   probe-then-discard usage in `TestMapLauncher`. So a cache that never
   evicts/disposes introduces no new lifetime risk - it just extends
   the existing "load once, trust it" assumption across tabs instead
   of within one. New `Scripts/Settings/ResourceContainerCache.cs`
   (static, keyed by `Path.GetFullPath`) wraps
   `ResourceContainerFactory.Open`; `ResourceListEditor`'s two call
   sites go through it instead of the factory directly. The map's own
   WAD (`_pendingWad`/`_currentWad` - the actual geometry data, read
   fresh so a save is always reflected) is deliberately untouched -
   this is scoped to *resources* only, matching what was actually
   asked for.

   What's actually meant by "don't re-cache the textures" turned out
   to be one level up from `ResourceContainerCache` itself: that cache
   only dedupes the *raw* containers (skipping a redundant disk read);
   `TextureSet.Load` still ran fresh on every map open regardless,
   building a brand-new instance with cold wall/flat/sprite decode
   caches every time, even when its underlying containers were now the
   exact same (shared) objects. New
   `Scripts/Settings/TextureSetCache.cs` caches the whole `TextureSet`
   itself.

   First attempt keyed purely on the ordered container list by
   reference, reasoning that `ResourceContainerCache` already
   guarantees two maps from the same mod share the same container
   objects for the same configured resources. Reported live as not
   working at all - the cache almost never hit, because
   `OnMapOptionsConfirmed` appends the map's own backing WAD
   (`_pendingWad`) as the highest-priority resource whenever it isn't
   already covered by another configured resource, and that WAD is
   *always* a brand-new `WadFile.Read` object, even reopening the exact
   same unmodified file (deliberately - a save must always be
   reflected). A reference-based key breaks on that entry on
   essentially every real open.

   Can't just drop that entry from the key either: a map's own WAD can
   embed its own PLAYPAL/PNAMES/TEXTURE1/TEXTURE2/graphics, layered at
   the *highest* priority, so two maps from the same mod folder can
   legitimately have different embedded textures - ignoring it risks
   silently serving one map's textures to another, which is worse than
   just redecoding. Fixed by having `TextureSetCache.Load` take an
   explicit `identity` list the caller builds, one token per container:
   every already-shared resource is passed as itself (reference
   equality, same as before), but the one entry that's always freshly
   re-read gets a `(path, LastWriteTimeUtc, length)` value token
   instead - stable across repeated opens of an unmodified file,
   correctly different the moment that file is edited or saved.
   Comparer switched from reference-only to plain `object.Equals` per
   element, since a value tuple already has structural equality and a
   container with no `Equals` override already compares by reference.

   Verified this actually works via temporary `GD.Print` diagnostics at
   each decision point, pasted back from a live run - confirmed only
   the very first-ever open of a mod decodes anything (`MISS`), both a
   second open of the same map and opening a second map from the same
   mod now `HIT`. Removed the diagnostics once confirmed.

   Reported live as *still* happening even after that fix, but as a
   visible status message ("Caching textures... x/4500"), not a log
   line - a genuinely different, further cache: `TextureIconCache`
   (`Scripts/Rendering/TextureIconCache.cs`), owned one-per-tab by
   `MapView`, unconditionally cleared and re-seeded
   (`SeedAll(textures)`) on every map load regardless of whether
   `textures` is now the same shared `TextureSet`. It's a further,
   Godot-side step *on top of* `TextureSetCache` - wrapping each
   already-decoded pixel buffer in a real GPU-backed `ImageTexture` for
   the texture picker/property dialogs - so sharing the decoded
   `TextureSet` didn't stop this layer from still re-uploading every
   icon to the GPU per tab. `SpriteIconCache` is the exact same
   shape, for sprite icons.
   New `Scripts/Settings/TextureIconCacheRegistry.cs`/
   `SpriteIconCacheRegistry.cs` - process-wide, keyed by the
   `TextureSet` reference alone (sufficient key for sprites too: the
   sprite name list is a deterministic function of the same resources/
   game configuration that already determined `textures`'s own
   identity). `MapView`'s two `readonly` fields became plain mutable
   fields, reassigned from the registry in `LoadMap`/`RefreshResources`
   instead of being re-seeded in place.
   `TextureCache` (`Scripts/Rendering/TextureCache.cs`, Godot
   `StandardMaterial3D` per texture name) has the same per-tab
   duplication in principle, but deliberately left alone - it decodes
   lazily, one name at a time, bounded by however many distinct names
   the specific map's own geometry actually references (tens, not
   thousands), nowhere near the eager, visible cost the other two had.

## Known limitation: none of these caches ever invalidate (deliberate, not an oversight)

`ResourceContainerCache`, `TextureSetCache`, and the two icon-cache
registries are all pure grow-only, for the whole process's lifetime -
confirmed deliberate, not an accidental gap, since nothing in this
codebase disposes a resource container
today anyway (see `Pk3File`'s own remarks), so a cache that never
evicts doesn't introduce a new disposal-safety problem. It does
introduce a real staleness one, worth writing down rather than
rediscovering later:

- **A folder-backed mod's file *list* is frozen at first open.**
  `DirectoryResource` scans its whole tree once, in its constructor,
  and never rescans. Before either cache existed, opening *any* map
  from that folder - even a second tab - did a fresh scan, so adding/
  removing a file externally (e.g. a new ZScript actor saved from
  VSCode) self-healed on the very next map open. Now, the first scan
  is what every tab gets for the rest of the session - a new file
  won't show up in the resource browser/actor discovery/texture
  picker until the app restarts. (Editing an *existing* file's
  content is unaffected either way - `DirectoryResource` always
  re-reads bytes fresh per lookup, it only caches the path list, not
  content.)
- **A `.wad`/`.pk3` rewritten on disk by another tool (e.g. SLADE)
  while DoomArchitect has it cached is served stale, not re-read.**
  `Pk3File` keeps its `ZipArchive`/`FileStream` open for its whole
  life; if the file is rewritten out from under that still-open
  handle, reads through it typically keep seeing the old bytes.
  Separately, and already true before any of this: DoomArchitect's
  own lump save-back (`ScriptDocument.Save`/`SaveLump`) already
  re-reads the target WAD fresh from disk immediately before splicing
  in just the one lump it's changing, so that specific write path
  doesn't clobber an external edit to a *different* lump made
  earlier - this limitation is about DoomArchitect's own *read* side
  (textures/resources/browser) going stale, not about losing writes.

**Revisit trigger**: if this actually bites in practice (added files
not appearing, textures not updating after an external edit, without
a restart) - the fix discussed but not built: guard a cache hit with
a cheap staleness check before reusing an entry. For `WadFile`/
`Pk3File` (one real file) that's a plain `LastWriteTimeUtc`+length
stat, cheap and correct. For `DirectoryResource` there's no equally
cheap equivalent - a directory's own mtime only reflects direct
adds/removes/renames *in that one directory*, not content edits or
changes in a nested subfolder - so a real check there means
rescanning the whole tree, which is actually fine since the original
research already found that scan itself (no byte reads) was never the
expensive part to begin with; the folder case may be better served by
just not caching the raw container at all and relying on
`TextureSetCache` (keyed by container *identity*, so a rescanned
`DirectoryResource` would need to become part of that identity too,
not resource paths) for the actual performance win.

## Verification

- `dotnet build DoomArchitect.sln` and
  `dotnet test src/DoomArchitect.Core.Tests/DoomArchitect.Core.Tests.csproj`
  (1052 tests - new coverage for `ModRootDetector.DetectFrom` and
  `MapSettings.GetFolderResources`/`WithFolderSettings`, including that
  folder-scoped and per-map-name resources coexist in one `.dbs`
  without clobbering each other).
- Needs a manual pass - settings persistence has no automated coverage
  for the Godot-layer wiring: open MAP01 from a mod folder, confirm
  resources; open MAP02 from the *same* folder and confirm the dialog
  comes up already correctly pre-filled rather than empty; confirm a
  true standalone multi-map WAD (no enclosing `maps/` folder) still
  persists its existing per-map-name resource behavior unchanged;
  confirm two maps from the same folder can be open in separate tabs
  at once with no interference.
- For the three refinements above specifically: opening MAP02 from the
  same mod folder as an already-open MAP01 should no longer show the
  Map Options dialog at all; each Map tab's title should read the real
  map name (`MAP01`, `MAP02`, ...) instead of `Map`; and with both open
  at once, textures/sprites should render correctly in both (confirming
  the shared containers/`TextureSet` work, not just that nothing
  crashed) - no automated coverage exists for either cache, since both
  sit entirely in the Godot layer.
