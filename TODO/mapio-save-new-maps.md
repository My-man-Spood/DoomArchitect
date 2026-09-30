# Saving maps / creating new maps

**Status:** Done  
**Area:** Map I/O

- [x] Saving maps / Creating new maps - done together 2026-09-16, planned
      against UDB's own real `MapManager.SaveMap`/`General.NewMap` source
      first (not guessed). New Core: `IO/WadWriter.cs` (mirrors
      `WadFile.Read`'s own format exactly, always a fresh full rebuild -
      matches UDB's own real approach, which cites GitHub issue #531 for
      why it never patches a WAD in place) and `IO/MapFileSaver.cs`
      (`BuildLumpsForSave`/`SaveUdmfMap`, the mirror image of
      `MapFileLoader`) - splices a fresh `TEXTMAP` into whatever a target
      WAD already has: replaces just `TEXTMAP` in an existing UDMF group
      (preserving `BEHAVIOR`/`ZNODES`/etc. byte-for-byte), removes an
      existing *classic* group wholesale and replaces it with a fresh UDMF
      one (saving a classic-format map is a deliberate upgrade-to-UDMF,
      since `UdmfWriter` is this project's only write path), or appends a
      fresh group if the map isn't present at all. `Undo/UndoStack.cs`
      gained real document-dirty tracking (`IsDirty`/`MarkSaved`) - each
      undo-stack entry is stamped with a permanent version id (not a
      simple counter) so undoing back to exactly the last-saved point
      correctly reads as clean again, and redoing past it re-dirties it.
      App layer: `OpenMapMenu.SaveMap`/`SaveMapAs`/`SaveMapInto` (Save
      reuses the already-open file; Save As and Save Into were both
      re-verified directly against UDB's real `MapManager.SaveMap`
      *after* an initial wrong guess shipped and was caught by the user
      hitting real data loss - Save As always rebuilds the destination
      from the *source* map's own resources, discarding whatever
      previously sat at the destination (UDB's own real
      `SavePurpose.AsNewFile`, a literal `File.Copy` of the source before
      ever touching the target); Save Into is the opposite, rebuilding
      from the *target's* own pre-existing content and only touching this
      map's own lump group, warning (UDB's own real prompt text) only on
      an actual same-map-name collision inside the target
      (`SavePurpose.IntoFile`) - both still switch the currently-open
      map's own file association to the target afterward, matching UDB's
      real `filepathname` reassignment exactly, which isn't conditioned
      on save purpose at all), a single `.bak` rename backup before every
      destination overwrite, and a new `MapSaved` event `MainMenuBar` uses
      to call `UndoStack.MarkSaved()`. New `NewMapDialog` (prompts for a map-slot
      name - per explicit user scope call, not UDB's own silent "MAP01"
      default) feeds `OpenMapMenu.ShowNewMapDialog`, which reuses the
      exact same Map Options (game config + resources) flow Open Map
      already has, just with `_pendingWad == null` branches skipping the
      WAD-as-resource-container append and `.dbs` persistence a brand-new,
      unsaved map has nothing to key either of those on yet.
      `MainMenuBar`'s File menu gained New Map.../Save Map/Save Map
      As.../Save Map Into..., with New Map/Open Map gated behind a
      "Discard unsaved changes?"
      confirmation whenever `UndoStack.IsDirty`. A real design gap caught
      and fixed before writing any code: preserving a loaded UDMF map's
      own real `namespace`/unknown-blocks on save (tracked as
      `_currentNamespace`/`_currentUnknownBlocks`, sourced from
      `UdmfDocument` at load time, which the App layer had been silently
      discarding down to just `MapData` until now); a map with no real
      namespace yet (new, or upgraded from classic) defaults to `"zdoom"`
      for this project's one UDMF-native game configuration, `"doom"`
      otherwise (matching `UdmfReader`'s own missing-`namespace` default).
      Test coverage: `WadWriterTests` (round-trip through the existing
      `WadFile.Read` as oracle), `MapFileSaverTests` (all
      `BuildLumpsForSave` branches, plus two realistic full round-trips -
      a WAD with embedded PNAMES/TEXTURE2/patches/flats sitting alongside
      the map's own classic and UDMF groups - decoded back through
      `TextureSet` afterward, not just checked as raw bytes, added while
      chasing the user-reported texture-loss bug above), `UndoStackTests`
      additions for `IsDirty`/`MarkSaved` including the undo-to-exact-
      saved-version and redo-past-it cases. Left explicitly out of scope:
      UDB's real 3-level backup rotation/autosave (v1 does one `.bak`
      rename), a real nodebuilder (`ZNODES`/`BLOCKMAP`/`REJECT` are
      preserved-if-present, never regenerated - GZDoom rebuilds stale/
      missing nodes at runtime), and UDB's real config-driven per-lump
      `MapLumps` table (v1 hardcodes the known UDMF/classic lump-name sets
      instead). Needs real manual verification in the actual Godot app -
      open/edit/save/reopen, New Map/edit/Save As, Save Into onto both an
      empty and an already-populated target WAD, the overwrite/collision
      warnings, and the discard-changes prompt - none of which this
      environment can drive itself.
