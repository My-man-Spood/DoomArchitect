# 3D-mode texture nudge + auto-align

**Status:** Done  
**Area:** 3D editing

- [x] 3D-mode texture-offset nudge (arrow keys) + auto-align (A/Shift+A/
      Ctrl+A) - done 2026-09-19, user's own request, planned and approved
      before implementation given the size. Verified against UDB's own
      source/default keybind config rather than assumed: plain arrow = 1
      pixel, Shift+arrow = 8 pixels, Ctrl+arrow = the *current grid size*
      (not a fixed 16 as guessed going in - already exposed as
      `MapOverlay.GridSize`); `A` = X-only auto-align, `Shift+A` = Y-only,
      `Ctrl+A` = both (the one most mappers actually reach for).

      New `WallPartKind` (Upper/Lower/Middle) on `WallSegment`
      (`LinedefWallBuilder`) - each `Add*IfVisible`/`BuildOneSided` already
      knows its own role, so tagging it costs nothing, and it's what lets
      a caller holding a `WallSegment` know exactly which UDMF field
      (`offsetx_top`/`_bottom`/`_mid`) to write back to, instead of
      inferring it unreliably from texture-name comparisons.
      `LinedefWallBuilder.GetPartTransform`/a new `PartSuffix`/
      `GetPartTexture` were made public/added for reuse by both new
      features. New `Core.Geometry.TextureOffsetMath.Nudge` ports UDB's
      own real `GetRoundedTextureOffset` wrap math exactly (delta that's a
      whole multiple of the texture size is a true no-op; wrapping that
      would otherwise land back on the exact old value bumps by one pixel
      in the nudge's own direction, matching UDB's own "why?"-commented
      guard). New `Core.Geometry.TextureAutoAligner.Align` ports UDB's own
      real stack-based flood-fill (`AutoAlignTexturesUDMF`/
      `AddSidedefsForAlignment`, read directly from source) - X
      accumulates by each wall's own real length as the walk proceeds
      (forward/backward jobs handled asymmetrically, exactly matching
      UDB's own real split, which is what keeps a chain aligned correctly
      walking outward in both directions from one starting point at once);
      Y is **not** distance-accumulated (two connected walls can be
      completely different heights) - instead each wall's own *natural*
      (unclipped) texture-top world Z is solved to match the start's,
      reusing `LinedefWallBuilder` itself as the oracle (`Top +
      VerticalTextureOffset` *is* that world Z, for any pegging state,
      since `VerticalTextureOffset` already encodes whichever rule
      applies) instead of re-deriving UDB's own separate per-part Y-anchor
      formulas a second time. Both new Core types return *data*
      (`TextureAlignResult`), not commands - no `MapData`/undo dependency,
      directly testable by asserting on the returned offsets, matching how
      `GeometryStitcher`'s own primitives stay orchestration-only.

      `MapView.HandleTextureNudge`/`HandleTextureAutoAlign` wire it in,
      acting on just `_currentTarget` (never the whole 3D-mode selection)
      for the identical, already-established reason `AdjustTargetHeight`
      documents its own same choice: this project's selection model
      tracks *which linedefs* are selected, not separately *which wall
      part* of each, so multi-select semantics would be ambiguous without
      a real redesign. Arrow-key nudging is wired *before* the
      `Echo: false` filter every other keyboard action goes through -
      UDB's own real `movetexture*` actions are all `repeat = true`
      (held-down-arrow keeps nudging), unlike `A`'s three variants, which
      aren't. `SetFieldCommand` has no dirty-marking callback (unlike
      `SetPropertyCommand`, which does) - both new handlers mark the
      touched sector(s) dirty directly right after executing, since a
      live keyboard-driven nudge needs to actually redraw as it happens;
      deliberately *not* copied into `LinedefEditDialog`'s own pre-
      existing per-part offset/scale field commits, which - a real,
      separate, already-existing gap noticed along the way, not something
      to silently fix here - don't mark dirty at all today.

      Deliberately scoped down from UDB's own full real feature, flagged
      rather than silently expanded into:
      - `visualautoaligntoselection*` (the "restrict to selection"
        variants) - the plain/global auto-align above covers the dominant
        use case; natural follow-up.
      - UDB's real *cross-role* flood-fill matching (chaining from an
        upper into a neighbor's lower if they happen to share a texture
        name, via its own `VisualSidedefParts` triangle-count machinery) -
        scoped to same-role-only propagation (upper-to-upper, etc), which
        covers the actual mapping use case without needing an equivalent
        of that machinery.
      - 3D-floor (`middle3d`)/`GetControlSides` participation - not
        modeled in this project at all yet.
      - Per-part texture *scale* (`scalex_mid` etc) affecting the nudge
        amount or the auto-align walk/Y-solve itself - UDB scales both;
        deferred as a follow-up once the simpler unscaled version proves
        out, flagged inline in both new Core types' own doc comments.
      - Y-alignment for a masked-middle part specifically (X still aligns
        normally) - the "reuse `LinedefWallBuilder` as the oracle" trick
        above relies on `Top` being independent of the offset being
        solved for, true for upper/lower (fixed by sector heights alone)
        but *not* true for a masked middle, whose own `Top`/`Bottom` shift
        together with its Y offset - skipped rather than risk a subtly-
        wrong solve near a clip boundary.
      - Arrow-key sign was a best-effort call, not verifiable without
        actually running the GUI - user confirmed *all four* directions
        read backwards once tried live, fixed same day: Up = <c>+delta</c>,
        Down = <c>-delta</c> (Y is never camera-relative - a wall is
        always vertical and this project's camera never rolls, so world-up
        is unambiguous regardless of facing direction). **Left/Right
        needed more than a sign flip**: the user correctly diagnosed the
        real bug as a missing dependency on camera facing, not just a
        wrong constant - a fixed sign for X can only ever be right for
        walls that happen to run the same way relative to the camera as
        whichever wall it was tuned against (wrong for a wall in a
        different room, or the same wall viewed from its opposite end).
        Fixed by comparing the targeted wall's own Start-End direction
        against the perspective camera's own current right vector
        (`_perspectiveCamera.GlobalTransform.Basis.X`, converted to Doom
        2D space) and flipping the base UDB-literal sign (`Right` = moving
        along the wall's own <c>+U</c>) whenever that direction and the
        camera's own right vector disagree - so "Left"/"Right" now always
        shift the texture left/right as currently seen, matching the
        user's own explicit requirement. Needed one more correction after
        this first landed: the comparison's polarity was inverted (100%
        backwards, confirmed repeatably, not an intermittent case-by-case
        bug) - fixed by flipping the comparison operator itself, not the
        base per-key sign convention.

        **A real correction to this entry's own earlier claim, pushed back
        on by the user and worth recording precisely**: an earlier version
        of this said UDB's real wall texture-offset-nudge is "genuinely not
        camera-aware at all" - true only as a narrow, verified claim about
        `BaseVisualGeometrySidedef.OnChangeTextureOffset` specifically (its
        `doSurfaceAngleCorrection` parameter is received but never read
        anywhere in that ~20-line method, read in full, twice, and no
        wall-part class overrides it) - but stated as if it generalized to
        UDB's texture-offset system as a whole, which is false:
        `BaseVisualGeometrySector.OnChangeTextureOffset` (floors/ceilings)
        genuinely does consume that same parameter, snapping
        `General.Map.VisualCamera.AngleXY` (the camera's real current
        horizontal viewing angle) plus the flat's own `rotationfloor`/
        `rotationceiling` field into one of 4 quadrants to decide which raw
        axis "up"/"right" actually means for a texture that can be rotated
        to any angle. Walls don't need that specific mechanism (a wall's
        own U-axis is always its fixed Start-End direction, never
        arbitrarily rotatable the way a flat is) - but the user's own
        real-world UDB experience of direction "sometimes correct,
        sometimes not" for walls has a real, verified structural
        explanation too: a two-sided wall's Front and Back sidedefs each
        store their own independent offset field against that same fixed
        Start-End line, and you can only ever see/target Front from one
        side and Back from the other - i.e. from roughly opposite viewing
        directions by definition - so the identical raw offset change
        visibly scrolls opposite ways on screen depending on which one
        you're editing, with no explicit formula needed to produce exactly
        the view-direction-correlated behavior described. The camera-
        relative fix above (measuring the real current orientation rather
        than a fixed per-side convention) already accounts for this
        correctly, without needing separate Front/Back-specific logic.

      **Real keybind collision, caught by the user the same day**:
      `FreeFlyCamera`'s own pre-existing fly-down control was bound to
      `Shift` (polled every frame via `Input.IsKeyPressed`, independent of
      whatever else is happening with the key) - the same real UDB 8-pixel
      nudge modifier. Holding Shift+Arrow to nudge also silently drifted
      the camera downward for as long as Shift stayed held. First tried
      moving fly-down off Shift instead (onto `C`, after a same-day back-
      and-forth over which of `C`/`Space` should be up vs. down) - reverted
      at the user's own explicit request: `FreeFlyCamera` stays exactly as
      it was before this feature existed (`Space` = up, `Shift` = down),
      and the *nudge* modifier moved instead, off Shift onto **Alt** (an
      intentional, flagged deviation from UDB's own real Shift-for-8px
      keybind, not a port of it - Alt has no other binding in this
      project and doesn't collide with anything). Ctrl (grid-size) was
      never part of the collision and is untouched.

      **A real, previously-latent picking bug, caught by the user testing
      a two-sided masked middle**: `MapRaycaster.TryWall` had no way to
      break a tie between two segments hit at the *exact same* distance -
      it just kept whichever was found first. A two-sided masked middle's
      front and back segments build at the identical world position (same
      "opening" bounds, by construction) - a real, common tie, not a
      degenerate edge case - and `LinedefWallBuilder.BuildTwoSided` always
      adds front before back, so front silently won every time regardless
      of which face the viewer was actually looking at. Invisible before
      this session (hover-highlighting a coincident pair looks the same
      either way), but a real bug once something side-specific - nudging a
      masked middle's own offset field - could actually target the wrong
      one. First fix attempt broke the tie by which sidedef's own sector
      the viewer is standing in (`SectorHitTest.FindContaining` against
      the ray's own origin) - the user reported it still wasn't working,
      and a check against their own real map found why: **105 two-sided
      masked middles exist in it, and several are self-referencing -
      front and back both belong to the exact same sector** (a real,
      common trick - a flag/curtain decoration hanging inside a single
      room). A sector-identity tie-break is structurally blind to that
      case; it can never tell the two sides apart at all, since they
      report the identical sector. Replaced with a purely geometric tie-
      break instead: which side of the wall's own line
      (`GeometryMath.SideOfLine`, the same primitive `SectorTracer`
      already uses) the viewer is actually standing on, matched against
      which sidedef is `Front` - correct regardless of whether front and
      back share a sector, since it never looks at sector identity at
      all. Verified two ways: a new `MapRaycasterTests` case builds the
      exact self-referencing shape (one room, one interior dividing wall,
      front and back both on that room's own sector) and confirms each
      viewing side resolves correctly; a standalone check against the
      user's own real map data correctly distinguished front from back on
      10 of 11 sampled self-referencing cases (the one miss was a test-
      camera-placement artifact hitting unrelated nearby geometry, not a
      wrong-side resolution).

      Still not actually fixed - the user reported it back broken after a
      full rebuild/restart, ruling out a stale-build explanation. The real
      problem: the sign comparing `SideOfLine`'s result to "is this the
      front" was backwards, and the regression test that "confirmed" it
      was circular - its own hand-built Front/Back sector assignment had
      been picked to match whatever the code already did, rather than
      checked against an independent source of truth. The actual,
      already-shipped ground truth for this exact question lives in two
      other places: `WallMeshBuilder`'s own face-winding and
      `LinedefOverlayHandler.DrawFrontIndicator`'s visible 2D front tick,
      both built on "front sidedef's own sector is on the walker's right
      walking Start->End" with `right = (direction.Y, -direction.X)` -
      both proven correct by the user's own actual use of the program
      already, not just a unit test. Working that same right-vector
      through `SideOfLine`'s own formula shows a point on the right
      always yields a *negative* result, so front corresponds to
      `SideOfLine(...) < 0`, not `> 0` as the first fix had it. Rewrote
      the two regression tests' own ground truth to match this real
      convention (one needed its geometry mirrored so its Front sector
      physically sits on the correct side; the other only needed its two
      assertions swapped) - both pass with the corrected `< 0` sign, all
      587 tests pass overall, and a tightened real-WAD check (now
      requiring the tested height to be one genuinely covered by *both*
      sides' own segments - a real tie, not just a height only one side
      happened to have geometry at, which is what made the first
      untightened pass over real data noisy and hid this) resolves 26 of
      26 genuine front/back ties correctly, self-referencing and ordinary
      two-sided walls alike.

      Building the very first version of this regression test also
      surfaced a genuine `SectorTracer` winding requirement worth noting
      for any future hand-built multi-sector test map: each sector's own
      boundary loop must close back through a shared edge walked in *that
      sector's own* direction (front = Start->End, back = End->Start),
      not just "some set of edges that happens to form a ring" - getting
      this backwards doesn't throw, it just makes `SectorHitTest.Contains`
      silently return `false` for every point, which is exactly as
      confusing to debug as it sounds and cost a real detour before being
      traced back to the test's own geometry rather than the fix.
