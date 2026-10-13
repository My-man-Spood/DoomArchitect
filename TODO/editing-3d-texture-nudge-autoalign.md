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

      **Update, nudge now applies to the whole 3D selection:** the user's
      own UDB muscle memory expected arrow-key nudging to move every
      selected wall's texture together, not just the one under the
      crosshair - confirmed against UDB's own real source
      (`MoveTextureByOffset`/`GetSelectedObjects`: the full selection when
      non-empty, falling back to just the live target otherwise) rather
      than assumed. The real complication, surfaced before writing any
      code: UDB's own 3D selection tracks individual wall *parts*
      (Upper/Middle/Lower, each side, separately selectable) - "nudge the
      selection" is unambiguous there, since every selected thing already
      knows its own part. This project's selection
      (`_selectedLinedefs3D`) only tracks whole linedefs - the very
      reason `HandleTextureNudge` (and `AdjustTargetHeight`) originally
      scoped itself to just the live target in the first place. Rather
      than guess at a resolution, asked the user directly: the part kind
      (Upper/Middle/Lower) always comes from whatever's actually under
      the crosshair - there's no other source for it at this
      granularity - applied to every selected linedef's side(s), front
      and back checked independently (one linedef's "front" means
      nothing on a different, unrelated linedef), that actually have
      that same part textured; a side without that texture is skipped
      rather than touched.

      New `MapView.ResolveNudgeTargets(Sidedef hoveredSide, WallPartKind partKind)`
      yields just the hovered side when nothing's selected (unchanged
      single-target behavior) or every qualifying side across the
      selection otherwise. The camera-relative X-flip check (see above)
      moved from a single upfront computation to per-side inside the
      loop - a multi-selection can freely mix walls running in different
      directions, each needing its own independent flip against the
      camera's current right vector; computing it once from only the
      hovered wall would nudge "Left"/"Right" the wrong screen-direction
      for any selected wall not running the same way as the one under the
      crosshair. Every affected side's own sector gets marked dirty
      (collected in a `HashSet<Sector>` first, so a multi-linedef nudge
      sharing a sector doesn't mark it dirty more than once) rather than
      just the single sector the original single-target version touched.

## Update: arrow-key nudge now works on floors/ceilings too

Reported live: `HandleTextureNudge` was hard-gated to
`TargetSurfaceKind.Wall` - aiming at a floor or ceiling and pressing an
arrow key silently did nothing. Not a regression; flats were never
wired in at all.

A wall only ever has two fixed sides, so a plain dot-product flip
already made Left/Right feel camera-relative for it - a floor/ceiling
can be viewed from any direction at all, so confirmed directly against
UDB's own real source
(`BaseVisualGeometrySector.OnChangeTextureOffset`'s own
`doSurfaceAngleCorrection`, `VisualFloor`/`VisualCeiling.MoveTextureOffset`)
that it needs a more thorough fix: snap the camera's own current
viewing angle (plus the sector's own `rotationfloor`/`rotationceiling`
field) into one of 4 quadrants and swap/negate the raw arrow-key input
accordingly - ported verbatim as new, plain/testable
`Core.Geometry.FlatNudgeCorrection.Resolve(cameraForward, sectorRotationDegrees, horizontal, vertical)`.

New `MapView.HandleFlatTextureNudge` mirrors the wall branch's own
shape exactly, just against the sector's own `xpanningfloor`/
`ypanningfloor` (or `ceiling`) UDMF fields instead of a sidedef's
per-part ones, sized against `TextureCache.GetFlatTextureSize`
(already existed, mirrors `GetWallTextureSize`) - same
`TextureOffsetMath.Nudge` wrap helper, unchanged. New
`ResolveFlatNudgeTargets` mirrors `ResolveNudgeTargets`'s own
selection-aware fallback rule, against `_selectedFloors3D`/
`_selectedCeilings3D` (already existed, tracked independently) instead
of `_selectedLinedefs3D`.

**Deliberately out of scope**: texture auto-align for flats (a
separate, real UDB feature of its own - not what was reported); UDB's
own flat-nudge wrap-by-scale behavior (`ScaledWidth / xscalefloor`) -
this project's existing wall nudge doesn't divide by scale either, so
flats stay consistent with that already-shipped convention rather than
getting a new scale-aware special case just for one surface kind.

New `FlatNudgeCorrectionTests` covering all 4 quadrants (cardinal
camera angles 0/90/180/270) plus a case confirming a sector's own
rotation field shifts which quadrant a given camera angle lands in -
6 new tests, 1203 total passing. No coverage for
`MapView.HandleFlatTextureNudge`'s own wiring, same established
convention as the existing wall-nudge code it extends.

## Update: vertical nudge direction was backwards after the flat-rendering fix

Reported live, right after shipping the flat-rendering fix below (same
day): arrow-key nudge still felt consistently backwards on the vertical
axis. Root cause: this feature's own `FlatNudgeCorrection.Resolve`
reproduced UDB's real final negation on *both* axes
(`MoveTextureOffset(-horizontal, -vertical)`) - correct for UDB, whose
own rendering has an unconditional Y-flip this project's own renderer
deliberately doesn't (see the rendering fix below). With that flip
absent, UDB's matching Y-negation became a double-negative here,
flipping the vertical direction. Verified the correct sign against this
project's own already-proven wall-nudge convention instead (confirmed
live when that feature first shipped: increasing a part's own Y offset
moves its texture *up* on screen) rather than guessing again - Y is no
longer negated in `FlatNudgeCorrection.Resolve`'s own final return; X
still is, since nothing about the removed flip ever touched X.
`FlatNudgeCorrectionTests` updated to the corrected expected signs - a
timely reminder that porting two UDB features that interact (this one
and the rendering fix below) needs their *combined* behavior checked,
not just each one against UDB in isolation.

## Update: the real bug was the camera-angle convention itself, not just a sign

The Y-sign fix above wasn't the whole story - reported live immediately
after: Left/Right and Up/Down were fully swapped (not just backwards),
consistently. Root cause, this time checked directly against UDB's own
source instead of assumed: `FlatNudgeCorrection`'s camera angle was
computed as a standard `Atan2(forward.Y, forward.X)` (0 = east, winding
toward north) - but UDB's own real `AngleXY` isn't that. Checked its
actual `Vector3D.FromAngleXYZ` (the function that turns `AngleXY` back
into a direction vector): `x = sin(angle); y = -cos(angle)` - zero
faces *south*, winding the *opposite* direction from the standard math
convention. Every quadrant boundary was landing 90 degrees away from
where it should, which reads as "axes swapped" rather than "backwards"
- exactly the second report. Fixed by computing the camera angle as
`Atan2(forward.X, -forward.Y)` instead - the real inverse of UDB's own
mapping, confirmed by hand against all 4 cardinal directions, not
assumed. `FlatNudgeCorrectionTests` rewritten with each case named for
the real compass direction it exercises (facing south is the true
"zero angle" case now, not facing east) rather than patched in place,
since the old test names were themselves artifacts of the wrong
convention.

Narrowed further on a third live report: Up/Down confirmed correct
after the angle fix, Left/Right still backwards in isolation - meaning
the quadrant/swap logic itself (now proven correct, since Y relies on
exactly the same quadrant selection Y does) was fine, and the one
remaining negation on X in `Resolve`'s own final return (carried over
from the earlier Y investigation, reasoned at the time to still be
correct for X since the removed rendering flip never touched X) was
itself still wrong. Dropped it too - `Resolve` no longer negates either
axis in its own final return at all, since none of UDB's own final-
negation convention actually transfers to a renderer without its
matching flip.

## Update: rebuilt `FlatNudgeCorrection` from scratch, then fixed one more real bug (diagonal drift) before it actually worked

Even after all three fixes above, live debug logging (temporary
`GD.Print`s in `MapView.HandleFlatTextureNudge`, since guessing had
already failed three times) showed the ported quadrant system was
still internally wrong in a way no more sign flips could fix - abandoned
porting UDB's own angle/quadrant convention entirely and rebuilt
`Resolve` from first principles against this project's own real
rendering formula (`FlatTextureTransform.ComputeUv`) instead: derive
the world-space pattern-shift a key press should produce
(`cameraRight * horizontal + cameraForward * vertical`, with
`cameraRight = (forward.Y, -forward.X)` - the fixed relationship for a
yaw-only camera, verified facing north -> right is east), then solve
`ComputeUv`'s own formula for the offset delta that produces exactly
that shift (`offsetDelta = -Rotate(desiredShift, sectorRotation)`,
confirmed against a concrete worked example, not assumed).

Reported live immediately after: "moving diagonal when i press any
key" - a new, worse symptom (not an axis swap or sign flip, an actual
blend of both output axes on every single press). Root cause: pan
offset (`xpanning`/`ypanning`) is a discrete, single-axis field - a
nudge has to commit to moving along exactly *one* of them per press,
the same way the existing wall-texture nudge already does. The
rewrite above used `cameraRight`/`cameraForward` directly, and a real
camera is essentially never pointed exactly along a map axis during
normal play - so `desiredShift` generically had a nonzero component
on *both* axes for a single-axis key press, a permanent diagonal drift
scaling with how far off-cardinal the camera happened to be aimed.
This is exactly the problem UDB's own quadrant-snap design solved,
just lost when its mismatched angle convention was dropped in favor of
continuous vector math. Fixed by reintroducing a snap step - but
derived fresh against this project's own vectors rather than ported:
`SnapToNearestAxis` collapses `cameraRight`/`cameraForward` onto
whichever single world axis (+X/-X/+Y/-Y) each is closest to before
building `desiredShift`, so the result always has exactly one nonzero
component pre-rotation. `FlatNudgeCorrectionTests` gained two cases
specifically facing slightly off-cardinal (not exactly north/east) and
asserting the result is bit-identical to the exactly-cardinal case,
proving snap rather than blend - confirmed live by the user afterward
("fixed!"), the only one of these four rounds confirmed working before
ship. Debug `GD.Print`s removed from `MapView.HandleFlatTextureNudge`
once confirmed.
