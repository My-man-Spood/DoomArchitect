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

      **Update, the size indicator reaches the gallery too:** the
      per-field preview's corner label told you a texture's real size
      once you'd already opened a field's picker - exactly the moment
      you'd want it is earlier, scanning the gallery for the right size
      in the first place.

      First attempt baked the "WIDTHxHEIGHT" text directly into a copy
      of each icon's own pixels, to work around `ItemList` (the
      gallery's control at the time) having no per-item overlay slot to
      hang a real label off of. Wrong call, reverted outright rather than
      patched: baking into the source pixels means the badge's own
      apparent size on screen necessarily varies with the texture's
      native resolution (legible on a 128x128 texture, oversized on a
      tiny 8x8 one, borderline illegible on a 256x256 one even after
      compensating for it), and on a thin, narrow texture (e.g. a door
      track) the badge's background rectangle could run past the real
      image content and get visually clipped. Both problems are inherent
      to the technique, not tuning mistakes.

      Fixed properly instead by changing what the gallery *is*: replaced
      `ItemList` with a plain `ScrollContainer` > `HFlowContainer` (the
      wrapping-grid layout `ItemList` provided before, now via a real
      container instead of a monolithic built-in widget) of a new
      `TextureGalleryCell` per name - a real `PanelContainer` with a
      `TextureRect` preview and a floating `PanelContainer`/`Label`
      corner badge, the *exact* same node layout and styling
      `TexturePreviewEdit`'s own per-field corner label already uses
      successfully (anchored at a fixed pixel offset from the preview
      box's own corner, not from the image content's own bounds - so it
      can never be clipped by a thin texture, and its apparent size
      never depends on the source resolution, since it isn't drawn into
      the image at all). `TextureBrowserDialog` now manages a
      `List<TextureGalleryCell>` parallel to its own `_displayedNames`
      instead of `ItemList`'s index-based API; selection highlighting,
      single-click-select, and double-click-confirm are now real
      per-cell logic (`TextureGalleryCell.Selected`/`Pressed`/
      `Activated`) instead of built-in `ItemList` behavior. Deliberately
      not ported: `ItemList`'s own keyboard arrow-key navigation between
      items - out of scope for this pass, nothing currently depends on
      it since no explicit keyboard handling existed before either.

      Also surfaced along the way, still not fixed (pre-existing gap,
      not introduced by either attempt): UDB's own real equivalent
      (`ImageBrowserItem.OnPaint`) gates this whole feature behind a
      real, user-facing `ShowTextureSizes`/`TextureSizesBelow`
      Preferences pair this project has never had - the per-field
      preview's own corner label has always shown unconditionally here.

      **Update, the real-nodes gallery was abysmally slow on a large
      resource set:** giving every single displayed name its own
      permanent `TextureGalleryCell` inside an `HFlowContainer` fixed
      the visual problem but traded it for a real performance one -
      `ItemList` never has this problem in the first place because it
      never creates a child node per item at all (same reasoning as
      UDB's own real `ImageBrowserItem`, which paints every item itself
      in one pass rather than being backed by one widget per entry); a
      resource set with thousands of names means thousands of real
      `Control` nodes all laid out by `HFlowContainer` at once, almost
      all of them never actually scrolled into view.

      Fixed by virtualizing rather than reverting to baked pixels:
      `HFlowContainer` is gone, replaced by a plain `Control`
      ("GalleryContent") inside the `ScrollContainer`, sized to the
      *full* virtual extent of every displayed name
      (`columns * pitch`/`rows * pitch`, so the scrollbar's range is
      correct) but holding only a small, bounded pool of real
      `TextureGalleryCell` instances - however many fit in the visible
      viewport plus a short buffer (`VisibleRowBuffer`). Cells are
      manually positioned (`Control.Position`, index-to-row/column math
      done by hand) and recycled as the user scrolls - never freed and
      re-instantiated, just hidden, repointed at a different display
      index (`TextureGalleryCell.LogicalIndex`), and recontented
      (`SetContent`). `Pressed`/`Activated` are each subscribed exactly
      once per cell instance, at creation - never re-subscribed on
      reuse - with the handler reading `LogicalIndex` at invocation
      time rather than closing over a value that would go stale the
      moment the cell is recycled for a different name.

      `_Process` now only ever touches the pooled handful of active
      cells (recomputing the visible range, polling
      `TextureIconCache` for their icons) instead of looping every
      displayed name every frame - the other real cost the all-real-
      nodes pass had, on top of the one-node-per-item layout cost
      itself.

      **Update, extracted into a reusable `VirtualizedGrid<TItem, TCell>`:**
      the pooling/positioning/scrolling logic above was never going to
      be a one-off need - any future large-collection-by-thumbnail
      picker would want the identical mechanism. Pulled out into
      `Scripts/Controls/VirtualizedGrid.cs` (`DoomArchitect.Controls`,
      a new location for genuinely generic, feature-agnostic UI
      mechanisms with no scene of their own - see
      `TODO/architecture-notes.md`), a plain C# class rather than a real
      `Control`/`Node` (Godot's node/property/signal system doesn't
      support open generic types, so a generic *controller* that drives
      an existing `ScrollContainer`/`Control` pair is what lets this be
      generic at all). A consumer's cell type implements the small new
      `IVirtualizedGridCell<TItem>` interface (`LogicalIndex`/
      `Selected`/`Pressed`/`Activated`/`SetContent`); `TextureGalleryCell`
      now implements it against a new `TextureGalleryItem` record
      struct (name + its already-decoded icon, or null) rather than the
      two-argument `SetContent(name, icon)` it used directly before
      (kept as its own overload - still the natural call for anything
      constructing/testing a cell directly). `TextureBrowserDialog`
      itself shrank considerably: all of its own pool/position/scroll
      bookkeeping is gone, replaced by one `VirtualizedGrid` instance
      and calls to `SetItems`/`UpdateItem`/`SelectIndex`/`ScrollToIndex`/
      `ActiveItems`.
