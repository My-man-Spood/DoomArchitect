# 2D overlay layer (grid, vertices, linedefs)

**Status:** Done  
**Area:** 2D editing

- [x] Screen-space overlay layer for 2D-view gizmos (`Scripts/View/MapOverlay.cs`,
      a `Control` on its own `CanvasLayer`): grid snapped to a 64-unit
      spacing derived from the map's vertex bounds, vertex knobs, and
      linedefs color-coded one-sided (white) vs two-sided (gray). Redrawn
      every frame via `Camera3D.UnprojectPosition()` rather than baked
      into world-space geometry, so it stays crisp at any zoom; toggled
      alongside the camera swap so it only shows in the 2D view. Pulled
      the Doom-X/Y -> Godot-X/Z mapping out of `SectorMeshBuilder` into a
      shared `VectorConversions.ToWorld` since both it and the overlay
      need the exact same mapping
