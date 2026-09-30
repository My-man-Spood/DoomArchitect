# Adding things (right-click insert)

**Status:** Done  
**Area:** Things

- [x] Adding things - done 2026-09-18. Right-clicking empty space in
      Things mode now places a new Thing there (UDB's own real
      `ThingsMode.OnEditBegin`/`InsertThing`), wired through
      `ElementOverlayHandler`'s existing `onEmptyRightClick` slot (already
      built for Vertices/Linedefs/Sectors' own "start Draw mode" - the
      exact same delegate shape fits here too, just with a different
      implementation). New Core: `MapData.RemoveThing` (the missing
      reverse of the already-public `CreateThing`) and
      `Undo/CreateThingCommand.cs`, with UDB's own real default settings
      (`ProgramConfiguration.ApplyDefaultThingSettings`, verified against
      source, not guessed) - Type 1 (Player 1 Start), Angle 0, classic-
      format `RawFlags` 0b0111 (Easy|Medium|Hard, from `Doom_misc.cfg`'s
      real `defaultthingflags`).

      A related, previously-invisible bug found and fixed while
      implementing this: `MapView`'s own mesh-instance sync
      (`SyncMeshInstancesWithMap`, built earlier this session for Draw
      mode's Sector/Linedef creation) never covered Things at all - a
      newly inserted Thing would have had no `MeshInstance3D` ever
      created for it, invisible in 3D mode. Fixed by adding the same
      count-gated create/remove diff (`SyncThingMeshes`) alongside the
      existing Sector/Linedef ones, mirroring `SyncWallMeshes` exactly -
      no stale-3D-reference cleanup needed there unlike Sector/Linedef's
      own, since Things never participate in 3D-mode targeting/selection
      at all (`HandleThreeDSelectClick` only targets Sector/Wall
      surfaces).

      One deliberate simplification, flagged rather than silently
      dropped: UDB's own real insert continues straight into dragging the
      newly created Thing within the very same mouse gesture
      (`editthings = new List<Thing> { t }`, picked up by its own
      `OnDragStart`) - this project's shared
      `ElementOverlayHandler<TSelectable,TDraggable>` engine has no hook
      for "the element this same press just created is now what should
      drag," so a newly inserted Thing here is created and selected, but
      a separate right-click-drag is needed afterward to reposition it.
      Also not ported: UDB's own real `defaultthingflags` being read from
      the loaded game configuration's own `.cfg` at map-load time (this
      project hardcodes the vanilla-Doom value as a constant instead,
      matching every other real-UDB-default constant already established
      this session - `DrawLoopCommand`'s own texture/height/brightness
      defaults), and its own map-boundary check before inserting
      (`LeftBoundary`/etc.) - this project doesn't model map boundaries
      anywhere else either.

      **Follow-up, same day**: `DefaultThingType` being real session
      state (not a fixed constant) *was* built after all, once the user
      asked for it directly - UDB's own real behavior confirmed by
      reading `ThingEditFormUDMF.cs`: every time the thing-edit dialog
      applies a Type value (`General.Settings.DefaultThingType = thingtype.GetResult(...)`),
      that becomes the default for the *next* inserted Thing, regardless
      of whether the dialog was opened for a fresh insert or an
      already-existing one. Ported as `MapOverlay.LastUsedThingType`
      (plain in-memory session state, outliving a single map load/unload
      since `MapOverlay` itself does - not disk-persisted, this project
      has no settings-file mechanism at all yet) - `CreateThingCommand`
      gained an explicit `type` constructor parameter (defaulting to
      `DefaultType` for callers, like Core tests, that don't have a
      "last used" concept of their own), `ThingOverlayHandler.InsertThingAt`
      passes `LastUsedThingType`, and `ThingEditDialog.ApplyRealTimeType`
      reports every resolvable typed value back out via a new
      `onTypeChanged` callback (wired in `MainMenuBar.OpenThingEditDialogFor`)
      so editing an existing Thing's type updates the default too, not
      just inserting a new one.

      **Second follow-up, same day**: user also asked to confirm/add
      UDB's own real right-click-without-dragging behavior across every
      mode - verified directly against `VerticesMode`/`LinedefsMode`/
      `SectorsMode`/`ThingsMode`'s own real `OnEditBegin`/`OnEditEnd`:
      `OnEditEnd` (mouse-up) only ever opens the properties dialog when
      no drag actually started (`OnDragStart` switches to a *different*
      mode object entirely, e.g. `DragLinedefsMode`, so the original
      mode's own `OnEditEnd` never even fires on mouse-up once a real
      drag began). This project's `ElementOverlayHandler`'s own right-
      click-release already distinguished "did the position actually
      change" for its own move-command bookkeeping - the exact same test
      doubles as the drag-vs-click distinction UDB's own gesture needs,
      so no new state was needed, just one more branch: a still-`Hovered`
      element on a release that moved nothing now also invokes the
      existing edit-dialog delegate (renamed `onDoubleClick` -> `onEdit`,
      since it's no longer only reachable from a double-click - this
      project's own left-double-click convenience is kept alongside it,
      not replaced, since it's harmless and doesn't conflict).
