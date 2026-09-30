# Sidedef upper/middle/lower wall mesh generation

**Status:** Done  
**Area:** Rendering

- [x] Sidedef upper/middle/lower wall mesh generation
      (`Core.Geometry.LinedefWallBuilder` + `Rendering.WallMeshBuilder`) -
      a close port of UDB's own visual-mode wall builders
      (`BaseVisualSector`/`VisualUpper`/`VisualLower`/`VisualMiddleSingle`,
      verified against the actual source): a one-sided linedef spans its
      sector's full floor-to-ceiling height; a two-sided linedef gets an
      upper wall on whichever side's ceiling is higher (none if equal)
      and a lower wall on whichever side's floor is lower (none if
      equal), each clamped against the classic vanilla "closed sector"
      trick (floor above ceiling) the same defensive way UDB's own code
      does. Confirmed from the actual source that a missing texture
      (`"-"`) never gates whether a wall's *shape* gets built - only
      which material/placeholder is applied at render time - so these
      build purely off height gaps, texture-field-independent. Reuses
      the exact same trace -> mesh pipeline philosophy as
      `SectorMeshBuilder`; a new shared `DoubleSidedMesh` helper was
      pulled out of `SectorMeshBuilder` so both builders emit the same
      two-triangles-per-face double-sided pattern instead of duplicating
      it. Walls (like ceilings) live on the same "hidden from the 2D
      top-down view" render layer. Wired into `MapView`'s live rebuild
      loop: a linedef's wall mesh rebuilds whenever either of its
      sectors goes dirty (harmless if that happens on both sides in the
      same frame - rebuilds twice with an identical result).
      A two-sided linedef's *masked* middle texture (fences/bars/windows)
      was added once the texture pipeline above made real texture pixel
      height available - a port of UDB's `VisualMiddleDouble` (its
      sizing math specifically, re-fetched verbatim from the actual
      source rather than re-derived from memory): the "opening" between
      the two sectors is `[max(floor_front, floor_back),
      min(ceiling_front, ceiling_back)]` (the same bounds an upper/lower
      wall's own gap uses); the texture's top edge anchors to the
      opening's top and hangs down by its own pixel height, clipped to
      the opening on both ends - a texture shorter than the opening
      leaves the rest of the opening with no geometry at all (not
      tiled), matching UDB's own non-repeating default exactly rather
      than approximating it.
      **Real pegging and transparency added in a later pass** (2026-09-18),
      closing the two gaps flagged above: `LinedefWallBuilder` now reads
      the line's own real "lower unpegged" flag - `Core.Map.Linedef`
      still has no typed flags concept, so this reads both storage
      conventions that actually exist in the codebase directly:
      `ClassicMapReader`'s verbatim raw-flags-word `"flags"` integer
      field (bit 16, `ML_DONTPEGBOTTOM`, verified against UDB's own
      game-configuration data) and `UdmfReader`'s own named boolean
      `"dontpegbottom"` field - and, when set, anchors the texture's
      *bottom* edge to the opening's own bottom (hangs up) instead of the
      prior unconditional top-anchor/hangs-down behavior, matching
      `VisualMiddleDouble.Setup`'s own real crop-plane formula exactly.
      `Sidedef.OffsetY` now shifts that anchor point itself for a masked
      middle - moving where the texture actually sits, not just scrolling
      which part of it shows - unlike a plain upper/lower/single wall,
      where the quad's extent is fixed by sector heights alone and
      `OffsetY` only ever scrolls the image within it; `WallSegment`
      grew a `VerticalTextureOffset` field to carry the right V-origin
      for either case so the App layer doesn't need its own pegging logic
      (`WallMeshBuilder` reads that instead of `Side.OffsetY` directly).
      Transparency was a separate, purely App-layer gap: the decoded
      texture pixels themselves were already correctly alpha 0/255 per
      pixel (`DoomPictureReader`/`CompositeTextureBuilder` both zero-init
      and only ever write alpha 255 for pixels a patch post actually
      covers) - `TextureCache.CreateMaterial` just never enabled any
      `Transparency` mode on the material using them. Fixed with a new
      `TextureCache.GetMaskedWallMaterial`, reusing the same already-
      decoded/uploaded texture as the opaque `GetWallMaterial` (no double
      decode) but with `TransparencyEnum.AlphaScissor` - a hard cutout
      rather than smooth `Alpha` blending, chosen because Doom's own
      masking convention is strictly binary (confirmed via the decoders
      above), so a cutout matches the source data faithfully without
      introducing the draw-order/sorting concerns smooth blending would
      add for no real benefit. `WallMeshBuilder` now groups wall segments
      by `(Texture, IsMasked)` rather than `Texture` alone, so a masked
      middle and a same-named opaque upper/lower/single never merge into
      one surface, and picks the right material per surface accordingly.
      Still deliberately not modeled: the `wrapmidtex`/Hexen repeat-
      texture behavior, for the same underlying reason as before
      (no typed linedef-flags concept for that flag either yet) - revisit
      alongside the Game configuration system below.

      **Follow-up fix, same session** (2026-09-19): user reported broader
      "texture alignment" problems beyond masked middles, plus some
      textures decoding as "random multicolor noise." Two separate real
      bugs, found by re-reading UDB's actual `VisualUpper`/`VisualLower`/
      `VisualMiddleSingle` source rather than assuming the masked-middle
      fix above covered pegging in general:
      1. **Upper/lower/single walls never respected either pegging flag
         at all** - `AddUpperIfVisible`/`AddLowerIfVisible`/`BuildOneSided`
         always top-pegged unconditionally (`VerticalTextureOffset =
         Side.OffsetY`), the masked-middle fix above only ever touched
         `AddMaskedMiddleIfVisible`. Ported the real per-part formulas
         directly from UDB's own `TexturePlane`/`tp.tlt.y` computations
         (`VisualUpper.Setup`/`VisualLower.Setup`/`VisualMiddleSingle.Setup`):
         a single-sided wall and a two-sided wall's *lower* part both
         default to top-pegged and switch to bottom-pegged (anchored to
         the sector's own floor) when "lower unpegged" is set - the same
         flag the masked-middle fix already reads via both classic
         (`flags` bit 16) and UDMF (`dontpegbottom`) storage. The
         **upper** part is the mirror image and was the real surprise:
         it defaults to *bottom*-pegged (anchored to the opening's own
         bottom, hanging up) and only becomes top-pegged when the
         separate "upper unpegged" flag (classic `flags` bit 8, UDMF
         `dontpegtop` - a new `IsUpperUnpegged` check, same dual-storage
         pattern as `IsLowerUnpegged`) is set - easy to get backwards
         since every other wall part's default is top-pegged, so this was
         verified directly against `VisualUpper.Setup`'s own `tp.tlt.y`
         branch rather than assumed symmetric with the lower wall. Since
         most real maps never set either flag, this was silently
         mispositioning the *default* case for a large fraction of every
         map's upper textures specifically - not a rare edge case.
         `LinedefWallBuilder.Build`'s `textureHeightLookup` parameter
         (renamed from `middleTextureHeightLookup` - no longer
         masked-middle-only) is now also consulted for the upper wall's
         default case, since that one (unlike the lower wall's
         flag-set case, which needs no texture height at all) needs the
         real texture height to anchor correctly; passing `null` still
         safely falls every part back to the old always-top-pegged
         behavior rather than guessing, affecting only
         `VerticalTextureOffset`, never a segment's own `Bottom`/`Top`.
      2. **Some patch/sprite lumps decoded into pure noise instead of
         failing cleanly** - `DoomPictureReader.TryRead` never validated
         that a patch's own column-offset table actually pointed inside
         its own lump data before trusting it and reading pixels from
         wherever those offsets landed. UDB never hits this because its
         real dispatcher (`ImageDataFormat.TryLoadImage`) always calls a
         separate `Validate()` gate *before* ever invoking the equivalent
         of this reader; that gate had no counterpart here at all. Fixed
         by porting the gate inline: every column offset must sit
         strictly between the lump's own header+offset-table
         (`8 + width * 4`) and the end of the lump's own data, or the
         whole patch fails to decode (returns `null`, the existing
         "couldn't resolve this patch" contract every caller already
         handles) instead of producing garbage pixels. Verified against
         the user's own real test WAD/IWAD pair (`DOOM2.WAD` +
         `MENTICIDE.WAD`, `MAP01`) via a throwaway Core-only harness: every
         one of the 72 wall textures and 54 flats that map's own geometry
         actually references decodes cleanly (4 wall/1 flat name resolve
         to the placeholder with an explicit warning - genuine missing
         names, not noise).
      3. **Found in the course of verifying (2) against real data**: every
         one of a *sprite* range's 1472 lumps in that same WAD/IWAD pair
         failed to decode before this fix, unrelated to the column-offset
         gate above - `TextureSet.TryGetSpriteTexture` called
         `DoomPictureReader.TryRead` directly instead of going through
         `_patchResolver` the way every other patch lookup
         (`ResolvePatchByName`/`ResolveAsModernImage`) already does, so a
         sprite frame that's actually a plain PNG (common for a modern
         resource-pack decoration/monster, which is what several of that
         WAD's own custom sprites turned out to be) never got the
         PNG-signature sniff that would have routed it to the real image
         decoder - its raw PNG bytes were only ever handed to the classic
         reader, which has no way to recognize them and correctly returns
         `null`. Fixed by routing `TryGetSpriteTexture` through the same
         `PatchImageResolver` every other patch lookup already uses (sniff
         first, classic Doom picture only as the fallback) - confirmed via
         the same real-WAD harness that this took the sprite range's
         failure count from 91 (in a same-day fix pass, before this
         change) down to 0.
      4. **The real root cause of the user's own "still broken" follow-up
         report, and almost certainly the dominant cause of the broader
         "texture alignment" complaint that started this whole pass**:
         every wall part only ever read a sidedef's *shared* `OffsetX`/
         `OffsetY` - UDB's real `VisualUpper`/`VisualLower`/
         `VisualMiddleSingle`/`VisualMiddleDouble.Setup` all additionally
         read a UDMF-only per-part field pair
         (`offsetx_top`/`offsety_top`, `offsetx_bottom`/`offsety_bottom`,
         `offsetx_mid`/`offsety_mid`) and add it *on top of* the shared
         offset (`tof = Vector2D(Sidedef.OffsetX, Sidedef.OffsetY) +
         toffset`) - a real, deliberate mapping technique (e.g. placing a
         masked-middle gore/corpse decoration at a precise height without
         needing a dedicated small sector for it) that this codebase
         silently ignored entirely. Root-caused via the user's own report
         of an `SP_DUDEB` corpse decoration rendering near the ceiling
         instead of the floor (sector 222): its raw UDMF sidedef block sets
         `offsety_mid = -240.0` with no base `offsety` at all, so this
         codebase's old shared-offset-only read used 0, textureTop =
         `openingTop + 0` = 208 (segment `[80, 208]`, near the ceiling);
         the real combined offset is `0 + -240 = -240`, textureTop =
         `208 - 240` = `-32` (segment `[-160, -32]`, pinned to the sector's
         own floor) - exactly matching GZDoom's real rendering once fixed.
         Fixed with a new `LinedefWallBuilder.GetPartOffset(side,
         partSuffix)` helper (`"top"`/`"bottom"`/`"mid"`) every wall-part
         builder now calls instead of reading `Side.OffsetX`/`OffsetY`
         directly; a classic-format sidedef has no such fields to find, so
         this naturally reduces to just the shared offset there, matching
         prior behavior exactly. `WallSegment` grew a matching
         `HorizontalTextureOffset` field (X had the identical gap - the App
         layer's own `WallMeshBuilder` read `Side.OffsetX` directly for U
         too) alongside the existing `VerticalTextureOffset`. Scanning the
         user's own real map for how much this actually matters: **1595
         sidedefs set `offset*_mid`, 1470 set `offset*_bottom`, 547 set
         `offset*_top`** out of ~3800 linedefs - this wasn't a rare edge
         case, it was silently breaking texture alignment across a large
         fraction of the entire map.
      5. **Immediate regression from fix 4, caught by the user within the
         same session**: a door decoration (`ODOORE05`, sector 204)
         "completely gone" after the per-part-offset fix landed - it had
         rendered (in the wrong place) before that fix, and vanished
         entirely afterward. Root cause: that sidedef also sets
         `scalex_mid`/`scaley_mid = 2.0`, a second, independent gap - UDB's
         real `Setup` divides *both* the texture's own effective height
         (`tsz = ScaledSize / tscale`) and the combined offset by that
         scale (gated behind a `ScaledTextureOffsets` game-configuration
         flag that's `true` in every real UDB game config, vanilla and
         every ZDoom-family one alike - hardcoded true here for the same
         reason `CompositeTextureBuilder`'s own two vanilla-compat flags
         are, no game-configuration system to make it conditional). This
         sidedef's own `offsety_mid = -493` was authored assuming it would
         be interpreted against a *half-height* effective texture
         (128px / 2 = 64) and itself divided by 2 (`-493 / 2 = -246.5`) -
         applying the fix above's real offset without also applying the
         real scale pushed the whole quad below the sector's own floor
         entirely (no visible portion left to clip to), where the
         previous, offset-ignoring code had at least rendered something
         (in the wrong spot) by accident. Fixed by extending the same
         `GetPartOffset` helper into `GetPartTransform`, additionally
         reading `scalex_<part>`/`scaley_<part>` (each defaulting to 1)
         and dividing the combined offset - and, for masked-middle sizing,
         the texture height itself - by `Abs(scale)`; zero/near-zero scale
         values clamp to 1 rather than dividing by zero. `WallSegment`
         grew `TextureScaleX`/`TextureScaleY` so the App layer's own
         `WallMeshBuilder` can divide a texture's raw pixel size by the
         same scale before using it as a UV denominator (for *every* wall
         part, not just masked middles - a scaled upper/lower/single
         texture tiles at a different density too) - keeping its own U/V
         math in the same scaled space `LinedefWallBuilder`'s offset/height
         math is already computed in, rather than desyncing the two.
         Verified against the same real sector-204 linedef: before this
         fix, no geometry at all; after, a real visible quad
         (`[-102.5, -38.5]` and `[-95, -31]` for its two instances) sitting
         correctly near the sector's own floor, matching a door texture's
         real placement.
      6. **A second, much larger instance of fix 3's sprite bug, this time
         for flats**: user-reported "corrupted" (random multicolor noise)
         textures, most notably `MARBFACE`, plus the tell that most of
         them didn't show up in the texture picker at all. Root-caused to
         `TextureSet.GetFlatTexture` trying `DoomFlatReader` *before* any
         modern-format sniff, unlike every other patch/image lookup in
         this class - and unlike the classic *patch* format (which has
         real internal structure, a column-offset table, that a modern-
         format lump usually fails to satisfy, especially now that
         `DoomPictureReader` validates it), the classic *flat* format is
         just raw indexed bytes with no header or signature at all -
         `DoomFlatReader.TryRead` "successfully" reads *any* sufficiently
         large lump, PNG-encoded ones included, as pure noise, so it never
         even got the chance to fail through to the modern-image fallback
         already sitting right there in the same method. Fixed by adding
         `PatchImageResolver.TryResolveModernImage` (the signature-sniff
         half of its existing `TryResolvePatch`, without that method's own
         classic-*patch*-format fallback baked in) and trying that first
         in `GetFlatTexture`, exactly mirroring UDB's own real
         `ImageDataFormat.TryLoadImage` dispatch (an unconditional
         signature check before ever consulting the caller's own
         `guessformat`). The user's stray "names don't show up in the
         picker" observation turned out to be a real, separate clue worth
         explaining rather than coincidental: these are wall-texture-style
         names (`OIRONC01`, `ONDSTJ95`, etc. - modern resource-pack
         graphics) used directly as floor/ceiling textures by lump name, a
         common GZDoom convention that bypasses the classic flat namespace
         `GetFlatNames()`/the texture picker actually enumerates from - so
         they were never going to appear there regardless of this bug.
         Scanning the user's own real map: **41 of the 54 flats actually
         used by its sectors were PNG lumps being silently decoded as
         noise** - not a rare case, the majority of the map's own floor/
         ceiling textures.
      7. **The mirror-image case of fix 6, this time on the wall side**:
         user-reported checkerboard placeholder instead of `FLOOR7_2` on a
         lower texture face (sector 398). `FLOOR7_2` is a genuine flat -
         never a `TEXTURE1`/`TEXTURE2` entry at all - deliberately used
         directly as a wall's lower texture, a real GZDoom/ZDoom-family
         convention (a flat's own detailed texture doubling as wall
         decoration) backed by a real UDB game-configuration flag,
         `MixTexturesFlats`, verified directly against UDB's own
         `DataManager.GetTextureBitmap`: when a wall-texture lookup misses,
         it falls back to a flat lookup - gated off for vanilla/Boom-style
         configs (which never search across the classic namespace split at
         all) but *on* for every ZDoom-family config UDB ships. With no
         game-configuration system here to make it conditional, hardcoded
         on - the same precedent as this fix list's own earlier always-true
         flags. Deliberately one-directional, matching UDB's own real
         `GetFlatImage` exactly (verified it has no reverse fallback to
         `textures` at all) - only `GetWallTexture` gained the fallback,
         `GetFlatTexture` did not. Fixed by falling `GetWallTexture`
         through to `GetFlatTexture`'s own resolution (already correctly
         format-sniffed per fix 6) whenever neither a composite
         `TEXTURE1`/`TEXTURE2` definition nor a folder wall image matches,
         but the name resolves to *some* lump. Verified against the user's
         own report (`FLOOR7_2` as a wall texture: placeholder before,
         real `64x64` after) and against the map as a whole: this was the
         exact same root cause behind *all four* of the wall-texture
         resolution warnings noted back in fix 1's own verification pass
         (`CRATOP1`/`CEIL1_1`/`FLOOR7_2`/`BLOOD1`, all genuine flats used
         as wall textures) - that pass's "0 warnings, looks clean" verdict
         held for decode correctness, but missed that these four were
         still resolving to the placeholder rather than a real texture.
