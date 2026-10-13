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

## Update: flat offset/scale/rotation were never actually rendered

Surfaced by testing the just-shipped arrow-key flat nudge: pressing
arrow keys on a floor/ceiling correctly marked the map dirty (the real
UDMF fields really were being written), but the texture never visibly
moved. Root cause, confirmed directly: `ToFlatUv` computed a flat's UV
as plain `position / textureSize` - `xpanningfloor`/`ypanningfloor`,
`xscalefloor`/`yscalefloor`, and `rotationfloor`/`rotationceiling` were
referenced *nowhere* in the entire rendering codebase. Not a nudge bug -
no sector's flat had ever rendered with any offset/rotation/scale
applied at all, even though all three field sets correctly round-trip
through save/load already (the user's own real map already had
`rotationfloor = 90.0` stored and preserved).

Checked UDB's own real rendering formula directly
(`VisualFloor.cs`/`VisualCeiling.cs`'s own UV setup) rather than
guessing - rotate (CCW, about the world origin), then an *unconditional*
`pos.y = -pos.y` (applied even at zero rotation), then add pan offset,
then multiply by scale, then normalize by the texture's own real pixel
size. **Deliberately did not port the unconditional flip**: it would
silently negate V for every sector in every existing map, not just
rotated ones, and this project's own current no-offset formula is
already documented (see above) and presumed correct against real
GZDoom rendering - confirmed nothing downstream
(`DoubleSidedMesh.AddTriangle` is a pure UV pass-through) compensates
for a flip either, so adding one would have been a real, confirmed
regression for the overwhelmingly common case, not a faithful port.
Kept the rotation, dropped just that one step - at zero rotation/
offset/default scale the new formula reduces to exactly the old one,
so nothing already-correct changes. Rotation *direction* without that
flip hasn't been independently verified live - flagged directly in
`FlatTextureTransform`'s own doc comment as the one thing worth a real
rotated-flat test before trusting the sign.

New `Core.Geometry.FlatTextureTransform.ComputeUv` mirrors
`LinedefWallBuilder`'s own real split for walls: Core resolves the
actual UV from a sector's own fields (pure math, directly testable);
`SectorMeshBuilder`/`ToFlatUv` stays a thin, "dumb" consumer. Same
UDMF key convention `MapView.HandleFlatTextureNudge` already
established. New `FlatTextureTransformTests` - the pre-existing-formula
baseline (confirms no regression), pure offset, a 90-degree rotation,
offset-before-scale ordering specifically (chosen to disagree at world
position (0,0), where a wrong order would silently pass for any other
position), ceiling fields independent of floor fields, and the near-
zero-scale clamp - 6 new tests, 1209 total passing.
