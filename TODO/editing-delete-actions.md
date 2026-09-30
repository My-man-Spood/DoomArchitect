# Delete actions (Vertices/Linedefs/Sectors/Things)

**Status:** Done  
**Area:** 2D editing

- [x] Delete actions for Vertices/Linedefs/Sectors mode (the `delete_item`
      keybind, default the Delete key) - a real gap, not previously
      tracked anywhere: this project had no delete of any kind before
      this entry. Ported UDB's own real per-mode `DeleteItem` actions
      (`ClassicModes/{Vertices,Linedefs,Sectors}Mode.cs`) exactly, each as
      its own `Core.Undo` command (`DeleteVerticesCommand`/
      `DeleteLinedefsCommand`/`DeleteSectorsCommand`) so a whole selection
      deletes as one Undo step:
      - Linedefs: just removes each selected linedef - no vertex or
        sector cleanup at all, exactly UDB's own blunt real behavior.
      - Vertices: a vertex with exactly two linedefs attached has them
        merged into one (matching UDB's `GetByIndex(0/1)` arbitrary-but-
        deterministic pick) before removal, so deleting a vertex mid-wall
        collapses it into a single edge; any other vertex (0, 1, or 3+
        linedefs) has every remaining attached linedef fully removed too -
        UDB's real `Vertex.Dispose()` cascade, which can genuinely tear
        open a sector's boundary at a junction vertex. That's UDB's real
        "Delete," not a bug here.
      - Sectors: processes one selected sector at a time (not a single
        batched pass), exactly matching UDB's own sequential loop - this
        is what makes two adjacent *selected* sectors sharing a wall
        resolve correctly for free (the first sector's removal leaves the
        shared wall one-sided; the second sector's own removal then finds
        it newly orphaned). Detaches the sector's own sidedefs, removes
        it, then per former linedef: both sides now null -> removed
        entirely; only a Back side left -> flipped
        (`GeometryStitcher.FlipBackwardLinedefs`, reused as-is); survives
        one-sided -> a simplified version of UDB's own `RemoveUnneededTextures`
        (copy Upper or Lower into an empty Middle, then clear Upper/Lower,
        since neither means anything on a one-sided wall).
      - New `MapData.RestoreSector` (mirroring `RestoreVertex`/
        `RestoreLinedef`) for the sector-delete undo.
      - `VertexOverlayHandler` gained a public `Hovered` (parity with
        `LinedefOverlayHandler`/`SectorOverlayHandler`'s own); each delete
        command falls back to the hovered element when nothing is
        selected, matching UDB's own real fallback. Dispatch lives in a
        new `MapOverlay.DeleteSelection()` (reads `Mode`, picks the right
        command), triggered from `MapView`'s existing keyboard-action
        switch, gated off while in 3D mode (3D mode's own selection is a
        separate concern - see its own remarks).
      - 18 new tests, including full Undo round-trips for the two-linedef
        vertex merge and the adjacent-sector flip/texture case.

      **Update: Things-mode delete added.** `DeleteThingsCommand` - as
      trivial as expected, matching UDB's real `ThingsMode.DeleteItem`
      exactly: a Thing has no adjacency to cascade through, so it's just
      remove/restore per selected Thing. `ThingOverlayHandler` gained the
      same `Hovered` accessor as the other three handlers;
      `MapOverlay.DeleteSelection()` gained an `EditMode.Things` case.
      Not ported: UDB's own `BaseClassicMode.DeleteThings` "path
      reconnecting" step (deleting an `InterpolationPoint`/`PathFollower`
      mid-chain retargets the chain's tag/arg links so it doesn't just
      break at the gap) - needs typed Thing `Args`/`Tag`, which this
      project doesn't model yet (raw `UniFields` only, same gap as
      below). Revisit alongside real typed Thing-argument modeling.

      **Deferred, tracked, not cut**:
      - UDB's own gentler `DissolveItem` action - a distinct action from
        Delete in UDB itself, bound to its own separate key. Investigated
        2026-09-30 and it's genuinely large, not a small addition on top
        of Delete - see `TODO/editing-dissolve-action.md` for the full
        scoping writeup (real UDB behavior researched, size assessed,
        options laid out).
      - `RemoveUnneededTextures`'s real tag/action-aware gating (skip
        clobbering a texture if the line/either sector carries a tag or
        the line an action special, since that combination is sometimes
        used to store a scripted texture-swap target) - this project has
        no typed action/tag model yet (raw `UniFields` only), so there's
        nothing to check against. Revisit once linedef specials/args get
        real typed modeling.
      - UDB's own optional "also delete Things inside the deleted
        sector(s)" step (`SectorsMode.DeleteItem`'s `SyncronizeThingEdit`
        branch) - gated behind a UDB preference this project has no
        settings surface for. Revisit alongside a real preferences page
        for editing behavior toggles, if ever wanted.
