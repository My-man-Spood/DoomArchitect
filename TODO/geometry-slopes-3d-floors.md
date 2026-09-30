# Slopes / 3D floors

**Status:** Pending  
**Area:** Geometry

- [ ] Slopes / 3D floors (UDMF extensions) - should fit naturally since
      floors/ceilings are already real meshes in this architecture. Also
      the point to revisit `MapRaycaster.TryWall`'s wall hit-testing (see
      the targeting entry above) - it currently assumes flat, constant
      floor/ceiling heights and will need updating alongside
      `LinedefWallBuilder` once walls can actually tilt.
