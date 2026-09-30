# Texture pipeline (Doom picture/flat/patch decode)

**Status:** Done  
**Area:** Textures

- [x] Texture pipeline: Doom picture format decode (`Core.Textures`) ->
      `Godot ImageTexture` (`Rendering.TextureCache`). Ported from UDB's
      actual data-loading code (`DoomPictureReader`, `DoomFlatReader`,
      `Playpal`, `PatchNames`, `TextureImage`/`WADReader`,
      `ImageDataFormat`), verified against the real source rather than
      guessed: patch/sprite posts (with the exact tall-patch topdelta
      accumulation quirk and the whole-patch-aborts-on-overflow bounds
      check); flats sized from lump length (perfect-square, else forced
      64x64 truncated read for a malformed >4096-byte lump); PLAYPAL
      (palette 0 only, matching UDB, with a gray fallback if missing);
      PNAMES + TEXTURE1/TEXTURE2 composite-texture parsing (Doom/Strife
      per-entry auto-detect, TEXTURE1's reserved index-0 slot dropped,
      TEXTURE2 not); patch compositing (`CompositeTextureBuilder`) with
      UDB's real vanilla negative-patch-offset rendering-bug emulation
      ported verbatim, including its exact (and, on inspection, not
      remotely a "half-opacity" test) alpha-transparency semantics -
      `PixelColor.a` is a raw byte compared against the float literal
      `0.5f`, which C#'s byte->float promotion collapses to "alpha != 0",
      not a half-opacity threshold; caught in review after an initial
      port used a naive 127-midpoint threshold instead.
      Two deliberate deviations from UDB's literal algorithm, both agreed
      with the user beforehand: TEXTURE1/2 entries are read via their
      real stored offsets rather than UDB's unstated assume-sequential-
      layout behavior (identical output on every real file, more correct
      on a theoretical non-sequential one); and the per-entry validation
      condition is fixed from UDB's actual operator-precedence bug
      (`(width>0 && height>0 && patches>0 && scalex!=0) || scaley!=0`,
      which passes almost unconditionally since `scaley` is virtually
      never zero) to the evidently-intended all-AND condition.
      Modern image format support (PNG/JPEG signature-sniffing, matching
      UDB's own always-on fallback even inside plain WAD lumps) is
      implemented via a swappable `IModernImageDecoder` interface backed
      by SixLabors.ImageSharp, so standalone lookups *and* patches used
      inside a composited texture both decode PNG/JPEG identically. PCX/
      TGA are signature-detected (UDB's exact heuristics, including the
      width/height/bpp range checks - an earlier, narrower two-field
      version of this check had a real false-positive rate against
      legitimate Doom patch data, caught and fixed in review) but not
      decoded - no comparably lightweight decoder exists, and both
      formats are essentially unused in real Doom content.
      Scope, flagged rather than silent: (IWAD+PWAD layering landed later,
      see "Multi-resource support" below) COLORMAP isn't implemented
      (confirmed from the actual source that UDB's own texture/flat
      rendering never applies it); `MixTexturesFlats` and the two
      negative-offset compatibility flags are hardcoded to their vanilla
      defaults (no game-config system yet to make them configurable);
      HiRes lump-range replacement, PK3/directory containers, and the
      text-based `TEXTURES` lump DSL are all confirmed inactive for
      vanilla IWADs and out of scope.
      App-side: `Rendering.TextureCache` converts decoded pixels to
      cached `Godot.StandardMaterial3D`s (nearest-neighbor filtering, a
      deliberate rendering choice - not a UDB behavior - to keep Doom's
      chunky low-res art from being smoothed by Godot's default filter);
      `SectorMeshBuilder`/`WallMeshBuilder` now generate real UVs (flats
      at their conventional 64-map-unit tile size; walls top-pegged,
      offset by the sidedef's own OffsetX/OffsetY) and `MapView` assigns
      the resulting materials per mesh surface. The map-format sentinel
      `"-"` is never passed into `TextureSet`'s lookups (that's reserved
      for a genuinely unresolvable name, which shows a placeholder
      checkerboard) - a `"-"` surface simply gets no material override,
      left at Godot's own default, since "no texture" isn't an error.
      This also unblocks the two-sided *masked* middle texture noted as
      deferred in the wall-mesh-generation entry right below - now
      implemented, see there for details.
