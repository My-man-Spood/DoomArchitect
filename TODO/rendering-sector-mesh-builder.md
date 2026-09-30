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
