# SectorMeshBuilder (Core triangles -> Godot ArrayMesh)

**Status:** Done  
**Area:** Rendering

- [x] `App.Rendering`: `SectorMeshBuilder` - Core triangles -> Godot
      `ArrayMesh`. Floor and ceiling each get two real triangles per
      polygon triangle (one wound each way) rather than a single
      double-sided-material triangle - a single triangle's normal only
      shades correctly from the side it's meant to face, so viewed from
      the wrong side it renders black regardless of culling. Doom
      X/Y -> Godot X/-Z, height -> Godot Y. (Originally shipped without
      the Y negation, "confirmed not mirrored" against the sample room -
      wrong; that room is fully symmetric and can't reveal a mirror by
      inspection. Actually fixed once a recognizable real map exposed it -
      see the Map I/O section.) Still one mesh per sector call,
      not yet one `MeshInstance3D` per sector wired into a live scene -
      that's the next item

## Update: flats were scaled to fit the sector instead of tiling at native pixel size

Reported by the user: a 128x128 flat on a 64x64 sector rendered as the
*whole* texture stretched to fit, visible in full - real GZDoom (and
UDB's own preview) shows only that texture's top-left quarter instead,
since a flat tiles at 1 texel per map unit, not "whichever fraction of
itself fits the sector." Root cause: `ToFlatUv` divided world position
by a hardcoded `FlatTextureSize = 64f` regardless of the real flat's
own decoded resolution - correct only for a genuine 64x64 flat (the
classic Doom size, and the only one this constant was ever right for),
silently wrong for anything else (a modern PNG-based flat via a PK3's
own `flats/` folder or a `TEXTURES`-defined one, any real size).

Fixed to match the exact convention `WallMeshBuilder` already uses for
wall textures (`TextureCache.GetWallTextureSize`) - a new, parallel
`TextureCache.GetFlatTextureSize(name)` (the flat material's own
already-decoded `AlbedoTexture`'s real width/height, no new decode)
feeds `SectorMeshBuilder.Build`, now taking the owning `TextureCache`
directly rather than being pure-geometry-only. Unlike a wall's `"-"`
(no texture, a real, valid, never-rendered state with its own
`NoTextureSize` placeholder), a sector's Floor/Ceiling is always
rendered, so there's no analogous sentinel to special-case - whatever
a name resolves to, down to `TextureSet`'s own placeholder for a
genuinely missing one, is always the real size to build against.
