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
