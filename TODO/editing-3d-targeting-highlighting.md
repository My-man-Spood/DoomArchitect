# 3D targeting + highlighting

**Status:** Done  
**Area:** 3D editing

- [x] 3D "what am I looking at" targeting + highlighting
      (`Core.Geometry.IMapTargetFinder`/`MapRaycaster`,
      `IMapSpatialIndex`/`UniformGridSpatialIndex`,
      `Scripts/Rendering/TargetHighlight.cs`) - reprioritized ahead of
      Things once it became clear this is genuinely foundational: every
      future picking feature (texture picking, wall/sector selection)
      will build on this, not just the light-level-adjustment keybind
      that originally motivated it (still not built - see below).
      A close port of UDB's own visual-mode picking
      (`VisualMode.PickObject`), rebuilt on pure Core geometry instead of
      Godot physics so it's fully unit-tested without Godot running at
      all, matching this project's whole testing culture. Two research
      passes into the actual UDB source (`VisualMode.cs`/`VisualBlockMap.cs`
      for the pick algorithm and its quadtree; `BaseVisualMode.cs` and
      `SectorInfoPanel.cs`/`ImageData.GetPreview()` for what UDB's 3D
      preview actually costs per frame) grounded every performance choice
      below in real, verified behavior rather than guesses - this was a
      deliberate, high-priority design goal given a specific concern about
      past stutter on less powerful hardware, not an afterthought:
      - The ray-cast itself is throttled to an 80ms poll, not every frame
        - UDB's own exact `PICK_INTERVAL`, a ~12x reduction over naive
        60fps polling for zero added complexity.
      - Candidates are narrowed via `IMapSpatialIndex` (a uniform grid,
        walked with the standard Amanatides-Woo "fast voxel traversal"
        algorithm) rather than testing every sector/linedef in the map.
        Deliberately simpler than UDB's own recursive quadtree
        (`VisualBlockMap`) - a real, considered trade-off (less
        implementation/correctness risk, plenty fast for typical Doom map
        geometry, less optimal only for pathological density) - and
        deliberately behind an interface specifically so a quadtree (or
        any other structure) could be swapped in later without touching
        `MapRaycaster` or anything else, if profiling on real content
        ever shows the grid isn't enough.
      - Tracing UDB's real stutter source found it wasn't the pick or the
        highlight at all (both cheap and well-throttled) - it was the
        auxiliary sector-info panel duplicating a texture-preview bitmap
        on every target switch with no evidence of disposing the previous
        one. This pipeline deliberately doesn't build any info panel yet
        (see below) - when one is, cache texture previews by name and
        reuse them, never duplicate/allocate one per switch.
      - The highlight itself is a small dedicated overlay mesh
        (`TargetHighlight`), not a port of UDB's shader-tint-the-real-
        surface technique - `WallMeshBuilder` already merges a linedef's
        wall parts sharing a texture into one mesh surface, so tinting
        "a whole surface" could over-highlight (e.g. an upper wall and a
        one-sided middle wall coincidentally sharing a texture would both
        light up together). A separate mesh built from the exact targeted
        `WallSegment`'s own geometry sidesteps this entirely, and matches
        how this codebase already highlights things in the 2D view
        (`MapOverlay`'s own separate screen-space overlay layer, not a
        mutation of the real map geometry's material).
      - Floor/ceiling hit-testing is already slope-general at essentially
        no extra cost: `PlaneMath` intersects a real `System.Numerics.Plane`
        (normal + offset) rather than hardcoding "intersect Z = height" -
        `PlaneMath.Horizontal(height)` is just the only plane this
        codebase currently knows how to build, since `Sector` has no
        slope data yet. When slopes arrive, only that one construction
        step changes; the actual ray-intersection math here won't need
        touching. **Wall hit-testing is NOT slope-future-proofed the same
        way** - a `WallSegment`'s `Bottom`/`Top` are flat doubles derived
        from a sector's constant floor/ceiling heights, and
        `MapRaycaster.TryWall`'s Z-bounds check assumes that. Making walls
        properly slope-aware is real new work belonging to
        `LinedefWallBuilder` itself (a wall's height becoming a function
        of position along it, not a constant) - flagged explicitly here
        (and in a code comment at the exact spot in `MapRaycaster`) rather
        than left as a silent trap for whenever slope support is actually
        built (see the Slopes entry below, which cross-references this).
      - Masked-middle-texture walls ARE hit-testable/highlightable:
        `MapRaycaster`/`TargetHighlight` thread an optional
        `Func<string, double>` height-lookup straight through to
        `LinedefWallBuilder.Build`, exactly like `WallMeshBuilder` already
        does - no new `Core.Geometry` -> `Core.Textures` coupling needed.
      **Deliberately not built yet**: any info panel/texture preview (see
      above - the actual historical stutter source, being avoided on
      purpose), and any editing action wired to this (sector brightness
      adjustment - the feature that originally motivated this whole
      system - texture picking, and selection are all future consumers,
      built separately later now that the foundation exists). 3D-mode
      only for now; the underlying Core system is view-mode-agnostic and
      could support a 2D-mode equivalent later without changes.
