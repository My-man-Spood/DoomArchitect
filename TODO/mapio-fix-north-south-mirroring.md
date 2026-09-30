# Fix: north-south mirroring bug

**Status:** Done  
**Area:** Map I/O

- [x] **Fixed a real north-south mirroring bug**, found by loading
      DOOM2 MAP01 (a map the user knows well enough to immediately spot
      it) - every map rendered flipped top-to-bottom relative to its
      actual layout. Root cause: `VectorConversions.ToWorld` mapped Doom
      Y directly onto Godot Z with no negation, but the top-down
      camera's -90-degree X rotation makes screen-up correspond to world
      -Z - so increasing Doom Y (north, "up" on every real Doom
      automap) moved toward the *bottom* of the screen instead. No
      camera rotation/roll can fix this: a pure rotation always
      preserves handedness, so it can only choose which world axis
      lands on screen-up, never flip one axis independently - the fix
      had to go in the shared coordinate mapping itself (`ToWorld`/
      `ToDoom` now negate Y/Z), which then correctly fixes the 2D view,
      the 3D view, and vertex-drag hit-testing all at once since
      everything already funneled through that one conversion point.
      Also fixed one place (`MapView.FitTopDownCameraToMap`) that had
      quietly bypassed `ToWorld` and hardcoded the old un-negated
      relationship by hand - now goes through the shared helper so it
      can't independently drift again. This had been latent since the
      very first rendering work; the sample room used to "confirm" the
      mapping back then is a square with a centered square hole,
      symmetric under a north-south flip, so it could never have
      revealed this
