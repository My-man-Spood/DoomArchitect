# 2D Thing rendering (real sprites)

**Status:** Done  
**Area:** Rendering

- [x] 2D Thing rendering, real sprites - replaced the flat single-icon
      circle/notch marker with UDB's own real approach
      (`Renderer2D.RenderThingsBatch`, verified directly against source,
      not guessed): a square (not circle - "things are square in Doom"),
      sized to the type's real radius, with the actual decoded sprite
      drawn on top at its own native colors/aspect ratio, plus a small
      separate arrow only for types with a meaningful facing
      (`ThingTypeInfo.ShowsDirection`). The square is a plain flat fill,
      this project's own choice for "draw a square" rather than a claim
      about matching UDB's own bundled `ThingTexture2D.png` atlas art.
      **The real rotation-frame selection is the substantial part**: two
      things of the identical type facing different directions now
      genuinely show different decoded sprite frames, not the same image
      rotated - `TextureSet.ResolveSpriteRotations` (new) parses the real
      public Doom sprite-rotation lump-naming convention (safe to
      implement directly from well-established public engine knowledge,
      not UDB's own creative content) into an 8-slot table given a
      config's own representative `sprite` string, correctly handling
      Doom's real mirrored-pair optimization (one drawn lump serving two
      opposite rotations, one of them flagged for a horizontal flip at
      draw time - confirmed matching UDB's own real `SpriteFrameInfo`/
      `Mirror` shape) and the "rotation 0 = doesn't rotate, one image for
      every angle" case. `SpriteIconCache.GetOrDecodeRotationFrame` (new)
      caches the resolved table per representative name (cheap, string-
      only) separately from the underlying per-lump pixel decode (shared
      with its own existing single-frame consumer, the Thing dialog's own
      type picker, so a lump referenced by multiple rotation slots or by
      the picker's own preview is only ever decoded once). The angle-to-
      slot formula is UDB's own real one, copied exactly since it's
      simple, objective math, not creative content: `spriteangle =
      ClampAngle(-angle + 270) / 45`. Covered by new `TextureSetTests`
      cases (separate-lump-per-rotation, mirrored-pair, single non-
      rotating sprite, no-match fallback) verifying the parser against
      constructed WAD fixtures, not just eyeballing the render.
