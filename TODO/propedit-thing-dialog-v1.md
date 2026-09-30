# Thing property editing UI (v1)

**Status:** Done  
**Area:** Property editing

- [x] Property editing UI, Thing (v1) - `ThingEditDialog`, ported from
      UDB's real `ThingEditFormUDMF` (verified via source, not guessed) -
      the last of the three main property dialogs (Sector/Linedef both
      done above). 4 tabs (Properties/"Action / Tag / Misc."/Comment/
      Custom - Comment/Custom stay placeholders, matching Sector/Linedef's
      own pattern). Properties tab: a `ThingTypePicker` (new) embedded
      directly in the " Thing " group - a real, filterable category tree
      (mirroring `LinedefActionBrowserDialog`'s own grouping shape) with a
      live sprite thumbnail per row and a bigger preview for the current
      selection, backed by a new `SpriteIconCache` (same seed-warm/decode-
      on-demand shape as `TextureIconCache`, packaging `TextureSet`'s
      already-real sprite decode as a flat 2D icon) - a deliberate scope
      call (embedded, not a popup "Browse..." dialog like Sector Special/
      Linedef Action) since that's UDB's own real shape here. " Flags "
      (flat checkbox list, same `RebuildCheckboxes` pattern), " Position "
      (X/Y/Z, real-time) and Type/Angle (real-time) round out the tab;
      Pitch/Roll are OK-only (this project's sprite billboards don't apply
      either yet). " Rotation "'s 3 numeric fields each pair with a real
      rotating-compass dial (`AngleDialControl`, new - a genuine port of
      UDB's real `AngleControlEx`: tick marks every 45°, left-click/drag
      snaps to 45°, right-click/drag is free rotation) plus UDB's real
      one-shot-at-Confirm "Random" checkbox per axis (never a stored
      flag - confirmed directly against `ThingEditFormUDMF.cs`'s own
      `cbrandomangle`/etc. handlers). "Action / Tag / Misc." tab: real
      OK-only Rendering (Scale X/Y, Alpha+Reset, Render Style) and
      Behaviour (Gravity/Score/Health/Conversation ID/Float Bob Phase)
      groups (verified field names/defaults directly against
      `ThingEditFormUDMF.cs`), an Action group reusing the exact same
      argument-editor a Thing's own `special`/`arg0-4` genuinely resolve
      against (see the extraction note below), and Identification reusing
      `MapTagsEditor` via a new `SetThings` overload (a confirmed third
      real shared consumer). Wired into "Edit Selection" via a new
      `MapOverlay.EditThingsRequested`/`HandleThingInput` double-click
      case, mirroring Sector/Linedef exactly.
      **Generalized off "Linedef" once a second real consumer needed the
      identical logic**: `LinedefActionInfo`/`LinedefArgumentInfo`/
      `LinedefArgumentEnumOption`/`GetLinedefAction(s)` renamed to
      `ActionInfo`/`ArgumentInfo`/`ArgumentEnumOption`/`GetAction(s)` (a
      Thing's own action/args genuinely resolve against the identical
      table a Linedef's action number does, confirmed via UDB's own
      parallel `ArgumentsControl.SetValue(Linedef,...)`/`SetValue(Thing,...)`
      overloads) - zero behavior change, same treatment `SectorTagsEditor`
      already got becoming `MapTagsEditor`. The whole argument-slot editor
      (toggleable numeric/enum rows, action Browse button, etc.) was then
      extracted from `LinedefEditDialog`'s own inline copy into a new
      shared `ActionArgumentsEditor` control, confirmed to leave the
      Linedef dialog's own behavior unchanged (443/443 Core tests still
      green) before building the Thing dialog on top of the same control.
      Added `IGameConfiguration.GetThingTypes()`/`GetThingFlags()` +
      `ThingTypeInfo.Category` (the raw `.cfg` category key, now tracked -
      previously discarded during load) + a verified-real 25-entry
      `thingflags` block in `GZDoomDoom2UDMF.cfg` (cross-checked against
      `UDMF_misc.cfg`/`ZDoom_misc.cfg`, correcting an initial wrong guess
      of `skill1`-`skill16`/`class1`-`class3` before it ever shipped - real
      UDMF only defines 8 skill levels and 5 player classes) - plus
      `MapDataTagQueries.GetUsedThingTags()`.
      **A real, pre-existing bug found and fixed while wiring this
      dialog's own OK handler**: every OK-only field write in the already-
      shipped Sector and Linedef dialogs (Special/Gravity/damage fields/
      Flags/Activation/per-part offset-scale-light overrides/Tags) called
      `UndoStack.Record(new CommandGroup(commands))` instead of
      `Execute(...)` - `Record` assumes its command already ran (proven by
      `UndoStack`'s own `Record_AddsAnAlreadyPerformedCommandWithoutRunningItAgain`
      test) and never calls `Do()`, so every one of those fields was
      silently never actually written when clicking OK (verified
      empirically with a throwaway test before believing it). Fixed in
      all three dialogs by switching to `Execute` (harmless to re-run an
      already-live-applied command's `Do()` a second time - it just re-
      sets the same current value) plus a permanent regression test in
      `UndoStackTests`.
      **Deliberately out of scope**: the
      `ThingTypeInfo`-driven fallback argument schema for `action == 0`
      "param things" (UDB's own more obscure feature - only the already-
      built "args come from the selected action number" path is in
      scope); UDB's real dynamic-light Color picker and config-driven
      Render Style dropdown (no color-picker control or render-style
      schema in this project yet - Render Style is a plain free-text
      field instead, same simplification as Sector's own Damage Type/
      Sound Sequence); UDB's real "Absolute Height" display-mode toggle
      (needs a point-in-sector lookup this project's Core has no public
      helper for yet - `Thing.Height` is already always floor-relative, so
      the field works correctly without it); UDB's real obsolete-type
      warning styling in the type browser; sidedef/sector-style Custom-
      fields button; Comment/Custom tabs (placeholders).
