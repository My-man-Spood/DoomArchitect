# Texture browser category tree

**Status:** Pending  
**Area:** Textures

- [ ] Texture browser category tree (UDB's real `MatchingTextureSet`) -
      UDB's texture/flat browser has a second tree branch alongside
      per-resource grouping: named categories ("Wood", "Metal", "Base",
      etc.) defined in the game configuration's `.cfg` data via
      pattern-matching rules against texture names, entirely independent
      of which WAD/PK3 a texture actually came from. Surfaced while
      building the texture picker's per-resource tree (v1) - deliberately
      deferred there since it needs real new schema, not just wiring:
      `Core.Configuration`/`IGameConfiguration` has zero concept of
      texture categories today (confirmed - no `MatchingTextureSet`-
      equivalent data structure, no `.cfg` parsing for it), so this needs
      its own design pass for the pattern-matching rule format and how a
      category set gets defined/loaded before the browser's tree can grow
      a second branch for it.
