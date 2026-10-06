# PK3 write-back: open and save scripts inside a real .pk3 archive

**Status:** Done
**Area:** Resources

The resource browser's "Open" action deliberately excluded anything
living inside a real `.pk3` zip archive - `Pk3File` had no write-back
path anywhere in this codebase, and opening something you can't save is
worse than not offering it (see
[Open files/lumps from the resource browser](browser-open-action.md)).
That gap is closed: a PK3's own root `SCRIPTS`/`ZSCRIPT` file, or a
nested `.acs`/`.bcs`/`.zs` script, can now be opened and saved back the
same way a loose disk file or a WAD lump already can.

Checked UDB's own real source before designing this (a shallow clone,
not guessed): `Source/Core/Data/PK3Reader.cs`'s `SaveFile` is the actual
reference. It does **not** patch the zip in place - it opens a fresh,
independent read, removes the one entry being replaced, adds the new
one, rebuilds the *entire* archive into memory, then overwrites the file
directly - no backup. This is the PK3 counterpart of something already
confirmed in this project: `WadWriter`'s own doc comment cites a real
UDB bug (GitHub #531) for why WAD saves are always a full rebuild, never
an in-place patch. UDB's own `MapManager.cs` *does* back up WAD/map
saves (a rotating `.backup1`/`.backup2`/`.backup3` scheme) - so UDB
genuinely treats the two formats asymmetrically, and this project's new
PK3 save path matches that asymmetry: no `.bak`, unlike the existing
WAD-lump save, which keeps its own `.bak`-then-overwrite - a deliberate,
evidence-based choice, not an inconsistency.

## What was built

- **`Pk3File.WithReplacedEntry(path, newData)`** (new) - the PK3
  counterpart of `WadFile.WithReplacedLumpData`, keyed by path (not
  index) since a PK3's own entries are already uniquely identified that
  way - no duplicate-name ambiguity a WAD lump can have. Decompresses
  every *other* entry's bytes too, not just the one being replaced - a
  full rebuild needs every entry's real bytes.
- **`Pk3Writer.Write(entries)`** (new, Core) - the write-side mirror of
  `WadWriter`, same split: returns finished bytes, caller owns file
  policy. Always a full, from-scratch rebuild via `ZipArchiveMode.Create`,
  never `ZipArchiveMode.Update` in place - matching both `WadWriter`'s
  own convention and UDB's real `PK3Reader.SaveFile`.
- **`ResourceContainerCache.Invalidate(path)`** (new) - drops a cached
  container for a path *without* disposing it (another tab's already-loaded
  `TextureSet` might still hold its own reference to the exact same
  instance) - called once, after a successful PK3 save, so a map tab
  opened afterward from the same mod sees the saved change instead of a
  stale cache hit.
- **`ResourceOpenRequest`** gained `Pk3EntryPath`/`Pk3EntryData`, a third
  shape alongside the existing `FilePath` (loose disk file) and
  `LumpName`/`LumpIndex`/`LumpData` (WAD lump) trio.
- **`ResourceBrowserPanel.BuildOpenRequest`**'s `File`-node case now
  branches on container type: a `Pk3File` resolves via `FindByPath`
  (already does the exact normalized-path lookup + decompression needed,
  no new read method required) instead of the `ResolveAbsolutePath` path
  a `DirectoryResource` still uses unchanged.
- **Incidental fix found along the way**: `IsOpenableFileName` didn't
  recognize a bare `SCRIPTS` (no extension) file name at all - only the
  ZScript bare-name case was special-cased. A PK3's own root-level
  `SCRIPTS` file (the real GZDoom convention, mirroring the WAD lump
  name) is exactly this shape, so it needed the same bare-name
  recognition `zscript` already had. This was a pre-existing gap, not a
  new one - it equally fixes a loose `SCRIPTS` file sitting in a
  `DirectoryResource`-backed folder mod, which wasn't openable either.
- **`ScriptDocument`** gained a third backing mode
  (`LoadPk3Entry`/`IsPk3EntryFrom`/`SavePk3Entry`), parallel to the
  existing file-backed and lump-backed ones. `SavePk3Entry` opens a
  fresh, independent `Pk3File.Open` read, splices via
  `WithReplacedEntry`, rebuilds via `Pk3Writer.Write`, overwrites
  directly (no backup - see above), then invalidates the resource cache.
  The fresh read is disposed *before* the overwrite runs (sequential,
  never a concurrent read+write handle on the same path).

**Explicitly out of scope**: a nested `maps/MAP01.wad`-style file living
*inside* a zipped `.pk3` (as opposed to a loose folder, which already
works). Not a meaningful real-world case for a pre-zipped pk3
distribution, and `Pk3File.ResolveAbsolutePath` returning null already
excludes it with no extra special-casing needed - revisit only if it
ever comes up for real.

## Verification

- `dotnet build DoomArchitect.sln` and
  `dotnet test src/DoomArchitect.Core.Tests/DoomArchitect.Core.Tests.csproj`
  (1056 tests - new coverage for `Pk3File.WithReplacedEntry` and
  `Pk3Writer.Write`, including a full open-replace-write-reopen round
  trip confirming the replaced entry has the new bytes and a sibling
  entry is untouched).
- Needs a manual pass - no automated coverage exists for the Godot-layer
  wiring: open a mod's `.pk3` as a resource, double-click its root
  `SCRIPTS` entry from the browser (should open a new script tab); edit
  it, save, confirm the `.pk3` on disk actually changed (re-extract and
  diff, or reopen in SLADE) and every other entry in the zip is
  untouched; reopen the same entry afterward and confirm it shows the
  saved edit, not a stale cached copy; open the same entry twice and
  confirm the second double-click focuses the existing tab instead of
  duplicating; confirm a nested `.acs`/`.bcs` file inside the pk3 gets
  real BCS syntax highlighting, same as a loose file.
