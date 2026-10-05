# Open Map... UI

**Status:** Done  
**Area:** Map I/O

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

## Update: open a PK3-style resource folder directly, and dedup it from the browser

Reported after the resource browser panel landed: opening a map WAD that
physically lives inside a folder *also* configured as a resource (the
real GZDoom/ZDoom convention of a tiny `maps/MAP01.wad` holding just that
map's own geometry, with everything else - textures, sprites, scripts -
loose in the folder's own namespace folders) showed up twice in the
browser: once as the folder's own flat nested file, once again as its
own separate, fully-expanded top-level entry. Two related changes:

- **`_fileDialog` (`Scenes/MapDocument.tscn`) is now `OpenAny`** instead
  of `OpenFile` - the same "Open Map..." dialog can now target a folder
  directly, not just a `.wad`/`.pk3` file. `OpenMapMenu.OnDirSelected`
  (new) scans the folder's own `maps/` subfolder for `*.wad` files (case-
  insensitive, matching `DirectoryResource`'s own convention), reads
  each one far enough to list its map(s) via the same
  `FindUdmfMapNames`/`FindClassicMapNames` the regular file-open path
  already uses, and reuses the existing single-map/multi-map-picker
  split unchanged - `MapEntry` just gained an optional `SourceWadPath`
  so a picked entry knows which of possibly several per-map WADs to
  actually read. The folder itself gets added to the Map Options
  dialog's own pre-filled resource list (`PopulateMapOptionsDialogDefaults`'s
  new `additionalResourcePath` parameter) so it's actually configured as
  a resource, not just where the map's bytes happened to come from.
- **`IResourceContainer.ContainsFile(string absolutePath)`** (new
  interface method - `DirectoryResource` does a real normalized-path
  match against its own indexed files, `WadFile`/`Pk3File` always return
  false, neither has independently-addressable files of its own on
  disk). `OnMapOptionsConfirmed` uses it to skip adding the map's own
  WAD as a separate resource/browser entry whenever it's already
  reachable through one of the *other* chosen resources - this is a
  general rule, not tied to the new folder-open flow specifically, so it
  equally fixes the original report (a plain `.wad` opened directly,
  with its containing folder *separately* also configured as a
  resource) as well as the new flow's own output.

Save/Save As/Save Into are completely unaffected either way - they
already always wrote back to `_currentWadPath` (the map's own real file,
regardless of whether it's also exposed as a resource), never to
whichever resource happened to be flagged as "the main one."
