# Texture picker (v1)

**Status:** Done  
**Area:** Textures

- [x] Texture picker (v1) - `TextureBrowserDialog`, ported from UDB's real
      `TextureBrowserForm`: a per-resource tree ("All" plus one node per
      loaded WAD/PK3, matching UDB's real `ResourceTextureSet` tree
      shape) alongside a live-filtered icon gallery; single-click selects,
      double-click/Enter confirms and closes, exactly like UDB. Wired into
      `SectorEditDialog`'s Floor/Ceiling Texture fields via "Browse..."
      buttons. Surfaced and fixed a real gap while building this: wall
      textures only ever came from classic `TEXTURE1`/`TEXTURE2` lumps -
      `TextureSet` now also resolves a plain image sitting in a PK3's
      `textures/` folder as a real wall texture (and symmetrically, a
      PNG-format flat in a PK3's `flats/` folder), matching UDB's actual
      `PK3StructuredReader.LoadTextures` precedence (classic lumps win,
      folder images only fill gaps) - this is the dominant convention for
      modern PK3 content, so this was blocking real use, not theoretical.
      Icons come from a new ambient `TextureIconCache` that starts warming
      the moment a map loads (main-thread, budgeted per frame - never
      blocks, never redone per-feature) rather than decoding on demand
      when the picker happens to open, specifically so a later hover-
      preview feature for lines/sectors can also read from it instantly
      with zero decode logic of its own.
      **Deliberately not built**: UDB's `MatchingTextureSet` category tree
      (separate entry below); PK3 internal folder sub-trees within one
      resource node; "used textures at the top" grouping, width/height
      filter spinners, the All/Textures/Flats/type-mixing combo, "classic
      view" toggle; real background-thread decoding (main-thread frame-
      budgeting instead, to avoid new locking around `TextureSet`'s
      non-thread-safe internal caches); `roottextures`/`rootflats`/the
      text-based `TEXTURES` lump DSL (already-deferred game-config
      options, unrelated to this fix).

      **Update, real shape + reused across dialogs:** the per-field
      preview (Sector's Floor/Ceiling, Linedef's Front/Back Upper/Middle/
      Lower) was originally a horizontal label+small-preview+field row -
      checked directly against UDB's real `ImageSelectorControl` and
      found that's not its actual layout at all (an earlier pass modeled
      it off a screenshot). Extracted a reusable `TexturePreviewEdit`
      matching the real control: preview stacked above a plain name field
      (no caption label, freeing it to be shown much bigger - 96x96 up
      from 40x40) with a floating corner label showing the decoded
      texture's own real pixel dimensions (UDB's real `labelSize`). Also
      fixed two real bugs in `TextureBrowserDialog`'s own gallery, found
      once actually compared side by side with UDB's real
      `TextureBrowserForm`: Godot's `ItemList.max_columns` defaults to 1
      (single column) regardless of `icon_mode`, so the "grid" was
      silently rendering as a plain list - set explicitly to 0 (auto-wrap
      by width); and the resource/category tree was on the wrong side -
      UDB's own real form puts the gallery on the left and the tree on
      the right (`splitter.Panel1`/`Panel2`, confirmed directly in
      `TextureBrowserForm.Designer.cs`), swapped to match. Also enlarged
      the dialog and bumped the default thumbnail size to 128px, matching
      UDB's own real default `ImageSize`.
