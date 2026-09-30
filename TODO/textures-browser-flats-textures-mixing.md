# Texture browser flats/textures mixing

**Status:** Done  
**Area:** Textures

- [x] Texture browser flats/textures mixing, matching UDB's real
      `mixtexturesflats` - done 2026-09-17. The Sector Floor/Ceiling
      texture browser/preview only ever offered flats, and the Linedef
      wall-texture browser/preview only ever offered wall textures - a
      user report ("I can't find the currently-used texture in the
      browser") traced to GZDoom's own real unified texture manager not
      distinguishing the two for either field. First pass hardcoded
      "sectors always show both, linedefs never do," unconditionally -
      wrong, caught by the user asking whether it had actually been
      checked against UDB (it hadn't). Re-verified directly against
      `MapManager`/`DataManager`/the real `.cfg` files: UDB always keeps
      the field-type split fixed (`FlatSelectorControl`/
      `TextureSelectorControl`, i.e. Sector vs. Linedef, never varies) but
      the *underlying collections* get cross-merged at load time only
      when the active game configuration's own real `mixtexturesflats`
      setting is true - true for the ZDoom/GZDoom-family configs
      (inherited from `ZDoom_common.cfg`), false for vanilla Doom
      (`Doom_common.cfg`'s own explicit `false`, also the real default
      when a `.cfg` doesn't set it at all). New
      `IGameConfiguration.MixTexturesAndFlats`, parsed from a real
      `mixtexturesflats` `.cfg` key, `true` only in `GZDoomDoom2UDMF.cfg`.
      `TextureBrowserDialog.Browse` gained an orthogonal
      `mixTexturesAndFlats` parameter alongside its existing `flats` mode
      bool (Sector still browses flats-first, Linedef still browses
      textures-first - only whether the *other* namespace is also offered
      changed); `TextureIconCache.GetIcon`/`GetOrDecodeIcon(name,
      preferFlat, mixTexturesAndFlats)` resolve icons the same way. Also
      fixed while there: `TextureBrowserDialog.SelectName` was an exact-
      case `IndexOf`, silently failing to preselect/scroll to the current
      selection on any casing mismatch between a map's stored texture
      name and the resource's own real lump casing - now case-insensitive,
      matching every other name lookup in this codebase.
