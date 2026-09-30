# 3D-mode Thing hover/select/edit

**Status:** Done  
**Area:** 3D editing

- [x] 3D-mode Thing hover/select/edit - done 2026-09-19, user's own
      request ("we can't hover select things in 3d view"). Things had been
      entirely excluded from `MapRaycaster`/`TargetHighlight` since the
      targeting system was first built (`TargetSurfaceKind` only ever had
      Floor/Ceiling/Wall) - not an oversight at the time, just not built
      yet. Ported UDB's own real Thing pick-box exactly
      (`BaseVisualThing`'s own bounding-box setup + its `PickAccurate`
      ray-vs-AABB slab test, verified directly against source rather than
      approximated with a bounding sphere/cylinder): an axis-aligned box,
      the type's own real radius out from the Thing's X/Y position on
      every side, spanning from its resolved floor-standing/ceiling-
      hanging world Z (the same value already used to position its
      rendered billboard) up by the type's own real height - deliberately
      *not* oriented to the camera the way the billboard itself renders;
      UDB's own real pick box isn't either. New `TargetSurfaceKind.Thing`/
      `MapTarget.Thing`/`ThingPickBounds` in `Core.Geometry`; `MapTarget.Sector`
      had to become nullable alongside this - a Thing sitting outside every
      sector is a real, if degenerate, editing state UDB itself allows,
      unlike every other target kind which always has one. `UniformGridSpatialIndex`
      now also buckets Things (a fixed generous margin around each Thing's
      point position, since the index itself - deliberately kept
      Core.Geometry-only - has no access to per-type radius data to size
      cells exactly). Game-configuration data (a type's real radius/
      height/hangs) lives entirely on the App side
      (`MapView.ResolveThingPickBounds`), keeping Core.Geometry itself
      configuration-agnostic, matching the existing `middleTextureHeightLookup`
      delegate-injection pattern - falls back to `ThingMeshBuilder`'s own
      generic radius-10/height-20 dimensions for an unrecognized type, the
      same fallback its rendered mesh already uses, so what's clickable
      always matches what's drawn.

      Selection/editing given full parity with Sector/Linedef rather than
      hover-only, since `MapData` already had complete Thing-selection
      support (`SelectOnly`/`ToggleSelect`/`GetSelectedThings`/
      `ClearSelectedThings`) sitting unused by the 3D-mode system: a new
      `_selectedThings3D` set (mirroring `_selectedSectors3D`/
      `_selectedLinedefs3D` exactly, including the 2D/3D selection sync
      bridge on entering/leaving 3D mode via Tab) plus a Thing branch in
      `HandleThreeDSelectClick` (toggle) and `HandleThreeDEditClick`
      (opens `ThingEditDialog` via the already-existing
      `MapOverlay.RaiseEditThingsRequested`, same whole-selection-if-any-
      else-just-the-target fallback the Sector/Linedef branches already
      use). `AdjustTargetHeight` (scroll-wheel floor/ceiling height, see
      the entry above) explicitly excludes a Thing target, same as it
      already excluded Wall - scroll-adjusting a Thing's own height is a
      plausible future feature but out of scope for this pass.
      `TargetHighlight` draws a translucent box at the Thing's own exact
      pick-box dimensions (the same idea as UDB's own real "thing cage"
      display) rather than reusing the flat/wall offset-plane approach,
      since a Thing's pick volume never coincides with any opaque surface
      and so needs no z-fighting offset at all.

      **Caught immediately by the user, same day**: this made the 3D view
      "unbearable" with more than a handful of Things around - a real
      performance regression, not a misperception. Root cause: the first
      version of `ResolveThingPickBounds` called `ResolveThingWorldZ`
      (which itself calls `SectorHitTest.FindContaining`) *and*
      `FindContaining` a second time directly, to populate
      `ThingPickBounds.Sector` - both a brute-force per-sector scan
      `SectorHitTest.FindContaining`'s own remarks explicitly document as
      meant to run "once per Thing at load/rebuild time, never per-frame".
      `MapRaycaster.FindTarget` calls this delegate once per *candidate*
      Thing on every ~80ms pick tick, which is exactly that forbidden
      hot path - with many Things clustered in view, that's two full
      sector scans (each itself re-tracing every candidate sector's real
      boundary via `SectorTracer.Trace`, uncached) per Thing per tick.
      Fixed two ways: `ThingPickBounds.Sector` is now always `null` -
      nothing in this project actually reads a Thing target's containing
      sector, so there was no reason to compute it at all - and the world
      Z is now read directly off the Thing's own already-positioned
      `_thingMeshes` mesh instance (kept correct by the existing dirty-
      things sync loop in `_Process`, the same "resolved once, reused
      until actually dirty" contract that makes it safe for rendering)
      instead of ever being recomputed from scratch in this method.

      **Two more real bugs, caught by the user the same day**:
      1. The hover/selection box sometimes rendered *behind* a Thing's own
         billboard sprite depending on viewing angle, instead of always
         staying visually behind it. Both are separate transparent objects
         at genuinely overlapping depths (the box surrounds the sprite),
         and Godot's default transparency sort (by each object's own
         centroid distance from the camera) has no reliable way to order
         two overlapping transparent volumes consistently - the box's own
         near/far faces flip which one is "closer on average" as the
         camera moves around it. Fixed by giving the highlight material an
         explicit `RenderPriority = -1` (every other transparent material
         in the scene, sprites included, defaults to 0) - lower priority
         always draws first regardless of distance, so the sprite drawn
         after it always composites on top, deterministically, instead of
         flickering between the two per-angle.
      2. Some Things appeared partially/fully embedded in a floor or
         ceiling, and - a real functional bug, not just visual - became
         *unhoverable* in 3D once they were: a ray hits solid floor/ceiling
         geometry before it ever reaches a Thing's own pick box buried
         behind it. Root cause: `ResolveThingWorldZ` computed a Thing's
         world Z as a plain `floor + Thing.Height` (or `ceiling -
         Thing.Height` when hanging) with no clamp at all - UDB's own real
         `BaseVisualThing` Z-position resolution (verified directly against
         source) clamps this against the *opposite* surface using the
         type's own real collision height: a floor-standing Thing that
         would end up above `ceiling - info.Height` (a large mapper-set Z
         offset, or a tall type in a low room) gets pulled back down to
         it; a ceiling-hanging Thing is the mirror image, clamped against
         the floor. Ported that clamp exactly, threading a resolved
         `ThingTypeInfo` (rather than just the `bool hangs` this method
         previously took) through every caller so the clamp has the real
         collision height available - `CreateThingMeshInstance`, the
         dirty-things resync in `_Process`, and `ResolveThingPickBounds`'s
         own fallback all share the one corrected method, so a Thing's
         render position and its pick box can never disagree about where
         it actually sits. UDB's own `AbsoluteZ`/`nointeraction`/special-
         DoomEdNum-9500-9501 branches aren't modeled (no per-type
         `AbsoluteZ` flag or special-actor concept exists here yet) - every
         Thing always gets the same real clamped treatment instead.
