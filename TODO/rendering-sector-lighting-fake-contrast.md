# Sector lighting + fake contrast

**Status:** Done  
**Area:** Rendering

- [x] Sector lighting, including vanilla "fake contrast" wall shading
      (`Core.Lighting.SectorBrightness`) - reprioritized ahead of Things
      once real textures made the lack of any shading stand out. A close
      port of UDB's own `Renderer.CalculateBrightness`, verified against
      the actual source rather than guessed: below light level 192,
      brightness drops off faster than linear (`192 - (192-level)*1.5`,
      the "Doom light levels" curve every vanilla game config has on by
      default - emulates the banding of vanilla's 32-entry COLORMAP
      table without literally reimplementing palette-shifting); walls
      additionally get "fake contrast" - a flat +-16 nudge applied only
      when a wall runs exactly north-south or east-west (never on a
      diagonal wall, and never at all once the sector's own light level
      is already 253+) - a vanilla engine trick for depth perception with
      no relation to any actual light source or direction. Floors/
      ceilings never get fake contrast, matching the actual source
      exactly. No artificial minimum-visibility floor either - a light
      level of 0 renders as genuinely black, same as UDB's own preview.
      Implemented as brightness baked directly onto each mesh's own
      vertices (one flat color per sector, or per wall accounting for
      fake contrast) rather than any real Godot light: `TextureCache`'s
      materials are `Unshaded` with `VertexColorUseAsAlbedo` on, so the
      scene's own `DirectionalLight3D` no longer affects sector/wall
      shading at all - correct, not a compromise, since real light-
      direction-based shading has no equivalent in Doom's own model and
      would look wrong on a Doom map regardless. `MapView` needed zero
      changes - brightness is entirely computed inside
      `SectorMeshBuilder`/`WallMeshBuilder` from data already available
      to them (`Sector.Brightness`, a wall segment's own originating
      sidedef/sector), so this stayed fully isolated to Core.Lighting
      plus the existing mesh-builder/material-cache layer.
      **Deliberately not modeled**: UDMF's per-sidedef `light`/
      `lightabsolute` override and separate `lightfloor`/`lightceiling`
      sector fields (all ZDoom/UDMF extensions with no typed properties
      yet) and the `nofakecontrast`/`smoothlighting` UDMF flags that can
      change or disable the above - always uses UDB's own vanilla-format
      defaults in the meantime, revisit alongside the Game configuration
      system and linedef/sidedef flags below.
