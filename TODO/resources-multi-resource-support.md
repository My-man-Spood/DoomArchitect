# Multi-resource support (WadResourceSet)

**Status:** Done  
**Area:** Resources

- [x] Multi-resource support (`Core.IO.WadResourceSet`) - a real UDMF PWAD
      with none of its own embedded PLAYPAL/TEXTURE1/sprites (common:
      UDMF maps typically lean on the IWAD for everything) had no way to
      pull those from a separately-loaded IWAD, so Things and any
      PWAD-undefined texture/flat fell back to placeholders. Fixed by
      layering `WadFile`s (later-added = higher priority, exactly UDB's
      own real `DataManager` precedence - confirmed via source, its
      single-item lookups search backwards) with the currently-loaded map
      always forced highest priority; `TextureSet` now depends on this
      instead of a raw `WadFile` for everything (palette, patches,
      textures, flats, sprites), not just sprites -
      `TextureSet.Load(WadFile, ...)` still works unchanged (wraps it in
      `WadResourceSet.Single`) so no existing call site changed.
      This surfaced a bigger discovery while researching UDB's real
      architecture (`DataReader.cs`/`DataManager.cs`/`MapOptions.cs`, all
      read directly): UDB persists resources in two real layers - app-
      wide default resources per game configuration, and a per-map
      `.dbs` sidecar file (an INI-style `Configuration`-format file,
      literally UDB's own extension, keyed by map header name). This
      prompted a real design conversation with the user about whether to
      port `.dbs` now or wait for a much bigger, not-yet-designed
      "DoomArchitect Projects" system (PK3-style conventions + built-in
      scripting/game-extension tooling UDB doesn't have - deliberately
      NOT decided here, deferred to its own future dedicated design
      session - see memory `project_doomarchitect_projects_vision`).
      Resolved: build both real UDB persistence layers now. Added
      `Core.Configuration.CfgWriter` (the write half of the `.cfg`
      grammar `CfgParser`/`CfgLoader` already read faithfully, mirroring
      UDB's own real `Configuration.OutputStructure` formatting) and
      non-destructive `CfgBlock.WithAssignment`/`WithBlock` helpers (a
      settings file must preserve fields DoomArchitect doesn't model -
      same "preserve what you don't understand" principle as UDMF
      `CustomFields`, applied to file-level persistence). `AppSettings`
      (global, per-game-configuration default resources) and
      `MapSettings` (one WAD's `.dbs`-equivalent content) are both
      Godot-free/unit-tested Core types; `MapSettings` deliberately
      mirrors one real, easy-to-miss UDB asymmetry confirmed via source -
      `gameconfig` is a single top-level field shared by every map in a
      WAD's `.dbs`, while `resources` genuinely is nested per map header
      name. Both store ordered resource paths as numbered keys
      (`resource0`, `resource1`, ...), the same idiom UDB's own
      `Configuration`-backed code uses for ordered lists - necessary
      since the underlying storage is a plain dictionary with no
      guaranteed enumeration order on either side. `Scripts/Settings/
      AppSettingsFile.cs`/`MapSettingsFile.cs` are thin App-layer path-
      resolution + read/write-bytes wrappers (`user://settings.cfg` for
      the former, `<wad>.dbs` for the latter). `OpenMapMenu`'s per-load
      "game configuration" dialog is now a combined "Map Options" dialog
      (config selector + resource list together, matching UDB's own real
      single dialog rather than a separate always-present button) -
      pre-filled from this map's own remembered `.dbs` if present, else
      from the app-wide default for whichever game configuration is
      selected; confirming saves both back, so the *next* new map for the
      same game is pre-filled too, with no separate Preferences UI needed
      at all. **Deliberately not built**: PK3/directory resources,
      drag-and-drop resource reordering (priority is just list order),
      and every other real UDB `.dbs` field (script documents, tag
      labels, sector-drawing overrides, etc.) - present-but-uninterpreted
      on a round-trip if a real UDB `.dbs` already exists next to a WAD,
      not destroyed.

      **Update, menu bar + Preferences + in-editor Map Options + scene-
      based dialogs:** using the feature above surfaced three real gaps -
      no way to edit a game configuration's app-wide default resources
      except as a side effect of a map load; no way to add/change a
      loaded map's resources without closing and reopening it; and every
      dialog this session was hand-built in C# (`new AcceptDialog {...}`)
      rather than as real `Control`-based scenes. Added a native Godot
      4.4+ `MenuBar` (`Scenes/Main.tscn`, script `MainMenuBar.cs`) with
      **File** (Open Map...), **Map** (Map Options...), and
      **Preferences** (Resources...) - the standalone "Open Map..."
      toolbar button was removed, consolidated into the menu (redundant
      next to a real menu bar). `Map > Map Options...` re-opens the same
      combined dialog for the map that's *actually currently loaded*, not
      just at load time - `OpenMapMenu` now tracks the current map's
      identity separately from its transient in-progress load state.
      Confirming a revisit fires a new `MapResourcesChanged` event
      (distinct from `MapLoaded`) that `MapView.RefreshResources`
      handles by rebuilding meshes against the new textures/game
      configuration *without* resetting undo history or the camera -
      `LoadMap` still does the full reset, but only for a genuinely
      different map; both share a new `RebuildAllMeshes` helper extracted
      from `LoadMap`'s old body. `Preferences > Resources...` is a new
      `PreferencesDialog` scene editing `AppSettings`'s default resources
      per game configuration directly, independent of any map being open.
      Every dialog (`MapSelectDialog`, `MapOptionsDialog`, the new
      `PreferencesDialog`) is now a real `.tscn` scene under `Scenes/UI/`
      with its own script, instantiated via `PackedScene.Instantiate()` -
      including a genuinely reusable `ResourceListEditor` component (an
      `ItemList` + Add/Remove + nested file-add dialog) embedded in both
      `MapOptionsDialog` and `PreferencesDialog`, mirroring UDB's own real
      `ResourceListEditor` control, which is reused for exactly the same
      two purposes. No `DoomArchitect.Core` changes - App/Godot-layer UI
      only, so verification here is manual, not `dotnet test`.

      **Known gap, verified against UDB's real bundled configs:** what we
      call a "game configuration" (Doom/Doom2) only covers one of the
      *three* axes UDB's own real configuration identity actually
      combines - confirmed directly from UDB's real bundled filenames
      (`Assets/Common/Configurations/*.cfg`, pattern
      `<Engine>_<Game><Format>.cfg`: `Doom_DoomDoom.cfg`,
      `Boom_Doom2Doom.cfg`, `ZDoom_DoomUDMF.cfg`,
      `GZDoom_HexenHexen.cfg`, etc.) - **engine** (vanilla Doom / Boom /
      MBF21 / Eternity / ZDoom / GZDoom / Zandronum / ...), **game**
      (Doom/Doom2/Heretic/Hexen/Strife), and **map format** (Doom binary/
      Hexen binary/UDMF). Telling detail: there is no `Doom_DoomUDMF.cfg`
      - the vanilla engine has no UDMF variant at all, since actual
      vanilla Doom.exe could never read UDMF; UDB doesn't even offer that
      combination.
      DoomArchitect currently collapses this to just the **game** axis,
      applied identically regardless of map format - deliberate, not an
      oversight, for two reasons: (1) no engine-level extensions are
      modeled at all yet (no Boom generalized specials, no MBF/ZDoom
      additions), so "engine" isn't a real axis for us today - there is
      only ever the one vanilla ruleset; (2) map **format** (UDMF vs.
      classic binary) is auto-detected directly from the WAD's own
      structure (unambiguous, unlike "which game"), so there's no reason
      to make the user pick it the way UDB's users do.
      Revisit this once Boom/MBF/ZDoom-style extensions become a real
      goal (a natural step under `feedback_aim_for_full_udb_port` /
      `project_doomarchitect_projects_vision`) - at that point "engine"
      needs to become its own real, user-selectable axis again, the way
      UDB does it, most likely still decoupled from map format for the
      same reason given above.
