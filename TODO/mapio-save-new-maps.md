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
      rename), and UDB's real config-driven per-lump `MapLumps` table
      (v1 hardcodes the known UDMF/classic lump-name sets instead).
      Needs real manual verification in the actual Godot app -
      open/edit/save/reopen, New Map/edit/Save As, Save Into onto both an
      empty and an already-populated target WAD, the overwrite/collision
      warnings, and the discard-changes prompt - none of which this
      environment can drive itself.

## Update: the "preserved-if-present" ZNODES/BLOCKMAP/REJECT assumption above was wrong - real, reported bug

The note above said these three were "preserved-if-present, never
regenerated - GZDoom rebuilds stale/missing nodes at runtime." That
last clause only holds for *missing* nodes - a real engine given a
*present* (just stale) `ZNODES` has no way to know it no longer
matches the TEXTMAP that was just saved alongside it, and doesn't
re-validate/rebuild in that case.

Reported by the user as Test Map not reflecting a real, confirmed-
saved geometry edit. Root-caused by directly inspecting their own real
map file: the saved `TEXTMAP` genuinely had the edit (byte-for-byte
identical to what DoomArchitect's own editor correctly displayed on
reload, even after a full restart, and to the temp WAD built for
testing), but the file *also* still carried a `ZNODES` lump from
before the edit - carried over untouched by
`MapFileSaver.BuildLumpsForSave`, exactly as this note originally
intended. A real source port given that file used the old, stale BSP
tree for actual rendering/collision, which looks identical to "my
changes aren't there" despite the TEXTMAP itself being correct the
whole time. Confirmed by running the source port directly, bypassing
DoomArchitect's own launch entirely (just the IWAD and the already-
verified-correct temp WAD, no other resources) - still stale, which
ruled out every other theory considered first (a resource-folder
conflict, a path-based cache) before landing on this one.

Fixed by dropping `ZNODES`/`BLOCKMAP`/`REJECT` on save instead of
carrying them over (`MapFileSaver.IsStaleAfterGeometryEdit`) - every
UDMF-supporting source port (confirmed: GZDoom) already rebuilds these
on its own when they're simply *absent*, which is reliable, unlike
trusting a present-but-possibly-stale one. `BEHAVIOR`/`DIALOGUE`/
`SCRIPTS` (not geometry-derived) are still preserved exactly as
before. One new test
(`BuildLumpsForSave_ExistingUdmfGroupWithPrecomputedGeometryLumps_DropsThemAsStale`).
Verified against the user's own real `ProjectReaper/maps/MAP01.wad`
(which did have a real `ZNODES` lump sitting in it): re-saving through
the fixed path now correctly drops it while keeping `BEHAVIOR`/
`SCRIPTS` intact.

## Update: real node-building, matching UDB's own approach, not just a safe drop

The drop-only fix above is correct and safe (every UDMF-supporting
source port rebuilds missing nodes at load time), but it's not what
UDB actually does, and it has a real cost that approach doesn't pay:
every map load now pays the source port's own runtime node-build cost,
scaling with map complexity. Asked directly whether UDB builds these
and checked its real source (`~/Projects/ultimate-doom-builder-source`)
rather than guess: yes - `MapManager.cs`'s `SaveMap` calls `BuildNodes`
on every save where the geometry actually changed, using a *different
configured nodebuilder profile for Save vs. Testing*
(`NodebuilderSave`/`NodebuilderTest`), confirmed real defaults for a
UDMF/GZDoom config (`ZDoom_common.cfg`):
```
defaultsavecompiler = "zdbsp_udmf_normal";   // "-c -X -o%FO %FI"
defaulttestcompiler = "zdbsp_udmf_fast";     // "-R -X -o%FO %FI" (zero reject - faster)
```

Implemented the same way: built `zdbsp` from its own real upstream
source (`github.com/rheit/zdbsp`, confirmed GPL-2.0-or-later via its
own `COPYING` - the same tool, not just something UDB *also* uses,
confirmed by running UDB's own bundled Linux binary's `--help` output
side-by-side with the one built here and getting identical usage
text), bundled at `Compilers/zdbsp/linux/zdbsp` +
`Compilers/zdbsp/LICENSE-zdbsp`, same shape as `zt-bcc`'s own bundling.

- `ZdbspArguments.Build` (Core) - the exact `-c`/`-R` + `-X` + `-m` +
  `-o` flag set above, confirmed against UDB's own real `zdbsp.cfg`
  profiles for UDMF maps specifically (not the plain `zdbsp_normal`/
  `zdbsp_fast` profiles, which are the classic-format ones).
- `WadFile.WithGeometryLumpsFrom` (Core) - splices back whatever a
  node builder's own output added to this map's group that wasn't
  there before (confirmed live: just `ZNODES` for these flags) by
  name-diffing against the pre-build lumps, rather than hardcoding
  "it's always exactly ZNODES" or trusting the builder's handling of
  anything *else* in the WAD (other maps/resources) - only this one
  map's own group is ever touched.
- `BundledNodeBuilder`/`NodeBuilderRunner` (Godot layer) - mirror
  `BundledScriptCompiler`/`ScriptCompilerRunner`'s own exact shape:
  resolve the per-OS bundled binary, shell out via a temp WAD
  round-trip, fall back to doing nothing (same as the drop-only fix
  above) if zdbsp isn't bundled for this OS or the build fails for any
  reason - deliberately silent, same posture
  `ScriptCompilerRunner` already established, no new UI for a node-
  build failure (UDB shows a warning dialog for this; left out here
  as a deliberate scope cut, since the fallback is always still correct).
- `OpenMapMenu.WriteMapToFile` gained a `forTesting` flag threaded
  through from `SaveMapThen` (Test Map's own forced save) down to
  picking `NodeBuilderRunner`'s profile - the real Save-vs-Test split
  above. Shares the same already-documented, accepted edge case as
  `SaveMapThen` itself (a cancelled never-saved-yet Save As prompt can
  leave the Testing profile selected for one unrelated later save) -
  harmless (nodes still get rebuilt correctly, just with the faster/
  rougher REJECT table that one time).

Verified end-to-end against the user's own real `MAP01.wad`: after
`BuildLumpsForSave` correctly drops the stale `ZNODES`, running it
through the bundled `zdbsp` and splicing the result back in produces
a real, substantial `ZNODES` lump (39657 bytes) in exactly the right
position, with `TEXTMAP`/`BEHAVIOR`/`SCRIPTS` and the file's other
content completely untouched. Five new tests
(`ZdbspArgumentsTests`, three `WithGeometryLumpsFrom` cases in
`WadFileTests` including one confirming a second map in the same file
is left completely alone) - 1152 total passing.

### Update: `NodeBuilderRunner` had a real, reported deadlock

Reported by the user: selecting New Map hung the whole editor (0% CPU,
not spinning - blocked, not looping). Root cause: `NodeBuilderRunner.Build`
redirected both zdbsp's stdout *and* stderr, then read them
*sequentially* (`StandardOutput.ReadToEnd()` fully, before ever
touching stderr) - a well-known .NET `Process` deadlock shape.
Confirmed from zdbsp's own source (`nodebuild.cpp`'s `fprintf` calls)
that its real-time BSP-progress bar goes to **stderr** specifically,
repeated once per percent of progress - a complex enough map produces
enough of it to fill stderr's own OS pipe buffer while this code was
still blocked waiting for stdout to reach EOF, at which point zdbsp
itself blocks trying to write more progress output, so it never
finishes, so stdout never reaches EOF either. Didn't reproduce on the
user's actual map as-is (not complex enough to cross that threshold
today), but reproduced and fixed it directly: a synthetic process
writing >64KB to stderr hung for 8+ seconds under the old sequential-read
pattern, and completed in 0.1s after switching to concurrent
`ReadToEndAsync` + `Task.WaitAll` (the standard, documented fix - ever
present, because this was rebuilding nodes on the user's actively-
growing test map, and would have eventually hung on a real save/test
regardless of whether *this specific* hang was its first trigger).
Confirmed `ScriptCompilerRunner` doesn't share this risk - it redirects
only stderr, never stdout, so there's no second buffered pipe to
deadlock against in the first place.

### Update: "New Map" wasn't actually hung - a popup landed off-screen on a second monitor

Reported right after the above as a second, apparently new hang on
"New Map" specifically. Wasn't a hang at all - this is the first time
"New Map" had ever actually been exercised in a real run (the original
implementation note above already flagged it as needing real manual
verification this environment can't do itself), and it surfaced a
real, separate bug: a dialog's own `PopupCentered()` call, when
triggered directly from inside a `PopupMenu`'s own `IdPressed` handler,
can resolve the wrong monitor in a multi-screen setup - the File menu's
native popup is still closing at that exact instant, and whichever
window Godot treats as "current" for a brand-new popup shown in that
same frame isn't reliably settled back to the main window yet.
Confirmed step by step with temporary diagnostic prints (reverted
after use, same technique as the parser hang investigation earlier
this project) that every line of this project's own code - including
the dialog's own `PopupCentered()`/`GrabFocus()`/`SelectAll()` calls -
completed and returned normally; the "freeze" was the user never
seeing a perfectly responsive dialog sitting in a corner of their
second monitor, mistaking it for a hang.

Fixed both places a dialog gets shown directly from a menu's own
`IdPressed` (`MainMenuBar.RunWithDiscardConfirmationIfDirty`,
`AppShell`'s `CreateMapTabRequested` for the zero-tabs case) by
deferring the actual popup one frame via `Callable.From(...).CallDeferred()` -
the same "let this frame's own processing settle first" pattern this
codebase already uses for layout timing (`AppShell`'s own deferred
`AlignMapToolbarBelowTabStrip`/`UpdateContentAreaLeftOffset` calls),
applied here to window/screen resolution instead. Not independently
re-verified live past the user's own next test (multi-monitor popup
placement isn't something this environment can drive itself either) -
worth a follow-up check if it recurs.
