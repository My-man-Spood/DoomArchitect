# Property editing foundation

**Status:** Done  
**Area:** Property editing

- [x] Property editing foundation (persistent selection + a renamed,
      UDB-shaped field bag + a generic property-edit undo command) -
      research into UDB's real dialog architecture (`SectorEditFormUDMF`/
      `LinedefEditFormUDMF`/etc., `UniFields`/`UniValue`/
      `UniversalFieldInfo`, `FieldsEditorControl`) surfaced a hard blocker
      before any actual Property editing UI (below) could start: every
      UDB dialog operates on "the current selection," and this project
      had no persistent selection concept at all - only transient
      hover/drag state that reset on mouse-up.
      **`CustomFields` renamed to `Fields`**, reshaped to match UDB's real
      naming/shape (`Core.Map.UniFields : Dictionary<string, UniValue>`,
      `UniValue { UniversalType Type; object Value; }`) specifically so
      porting UDB's dialog logic later needs less translation - a
      deliberate ask from the user, not just a rename for its own sake.
      Two flagged divergences from UDB's actual `UniFields`/`UniValue`:
      `Value` validates against `long` (not UDB's `int`, continuing the
      already-established `UdmfValue.ToObject()` choice) and UDB's
      `Owner`/`BeforeFieldsChange()` (its automatic undo-snapshot hook)
      aren't ported at all - incompatible with this codebase's explicit
      `ICommand.Do()/Undo()` model, used instead. `UniFieldsExtensions`
      ports UDB's `SetFloat`/`GetFloat`/`SetInteger`/`GetInteger`/
      `SetString` (as extension methods, not UDB's "static method taking
      the field bag as a param" idiom) faithfully, including its "never
      store the default value, omit the key instead" rule; `GetBool`/
      `SetBool`/`GetString` are new - real gaps in UDB's own surface
      (confirmed via grep), filled here for a complete four-primitive-type
      accessor surface. `Fields` is fully public and directly mutable
      (matching UDB's `sc.Fields["key"] = ...` exactly) rather than routed
      through a `MapData` method - not actually a new divergence from
      "MapData is the mutation boundary," since plenty of existing
      properties (`Sector.FloorHeight`, `Thing.Angle`, etc.) are already
      plain settable properties with no `MapData` indirection either;
      `MapData` methods exist only for the two properties with real
      cross-element side effects (vertex/thing moves).
      **Selection**: `Vertex`/`Linedef`/`Sector`/`Thing` (not `Sidedef` -
      no edit mode/picking exists for it) each gained
      `IsSelected { get; internal set; }`, the same dirty-flag idiom as
      `Sector.NeedsRebuild`/`Thing.NeedsUpdate` (this is the third
      instance of that exact per-type-repeated pattern - a shared
      `MapElement` base is worth a real discussion once a fourth case
      shows up, not bundled into this pass). `MapData` gained
      `SelectOnly`/`ToggleSelect`/`ClearSelected*`/`GetSelected*` per
      type. Selection is not undoable (view state, not document state,
      matching UDB) and persists independently across `EditMode`
      switches. **2D** (`MapOverlay`): click selects-only, Shift+click
      toggles, clicking empty space clears that type's selection; a new
      red `SelectedColor` (per the user's explicit choice) sits in a
      3-way priority under hover (hover always wins, no blended state) in
      `DrawVertices`/`DrawLinedefs`/`DrawThings`, and `DrawSectorHighlight`
      now fills every selected sector, not just the hovered one. **3D**
      (`MapView`/`TargetHighlight`): UDB has real multi-select in visual
      mode too, not just the classic 2D modes, so this shares the exact
      same `MapData` selection state rather than being a separate 2D-only
      feature - confirmed with the user directly rather than assumed.
      Click-to-select (now handled at all - 3D mode previously had zero
      mouse-button input) acts on the already-current hover target at
      **Sector/Linedef granularity** (a whole linedef/sector, not UDB's
      real per-wall-surface upper/middle/lower granularity - see below);
      unlike 2D, there's no "current mode" restriction, so a mixed
      Sector+Linedef selection builds up naturally. `TargetHighlight`
      was reworked from a single-mesh "one target at a time" node into a
      pooled-child-`MeshInstance3D` `UpdateHighlights(...)` that rebuilds
      every ~80ms pick tick (matching UDB's own `PICK_INTERVAL`) showing
      the hover target plus every selected Sector/Linedef at once.
      **`Core.Undo.SetFieldCommand`**: a new generic leaf `ICommand` for
      setting/removing one field on one element, auto-capturing the
      pre-existing value at construction time so a future dialog never
      has to snapshot it separately. Composes with the existing
      `CommandGroup`/`UndoStack` unchanged for multi-element/multi-field
      edits - no changes needed to either.
      **Deliberately deferred, tracked here on purpose** (surfaced by the
      user as easy to lose track of if only mentioned in passing) so none
      of it quietly falls through the cracks:
      - `.cfg`-driven `UniversalFieldInfo` schema (a `Managed` bool
        gating which fields get a hardcoded dialog control vs. a generic
        fallback row) - each future dialog instead declares its own small
        local `HashSet<string>` of field names it owns, and the generic
        fallback renders purely from each live `UniValue`'s own runtime
        `Type` (no schema lookup needed at all for that). Revisit once a
        dialog actually needs enum/texture/color-typed generic rendering.
      - `UniversalType` cases beyond Integer/Float/String/Boolean (UDB's
        real enum has 27 total) - the rest are UI-control hints,
        meaningless without the schema above.
      - `UniFields` mixed-value comparison helpers (`AllFieldsMatch`/
        `CustomFieldsMatch`/`UniValuesMatch`/`ValuesMatch`) - needed for a
        future multi-select-editing dialog to show a blank/indeterminate
        field where selected elements differ, not needed yet.
      - `UniValue.ValidateName(string)` - trivial to add whenever a UI for
        adding a brand-new custom field by name exists.
      - Marquee/box-select (drag a rectangle to multi-select), in both 2D
        and 3D.
      - Multi-element drag (dragging an entire existing multi-selection
        together as one gesture, instead of collapsing to
        select-only-this-one on drag-start) - needs per-element
        `CommandGroup` + shared drag-anchor math.
      - Sidedef selection entirely, in both 2D and 3D.
      - **3D per-wall-surface selection granularity** (UDB's real
        front-upper/front-middle/front-lower/back-* tracked
        independently, which is what its texture-alignment tools need) -
        this pass only selects at Sector/Linedef granularity, a deliberate
        scope call confirmed with the user. Needs a real "which part"
        identity added to `Core.Geometry.WallSegment` (it has none today)
        and a selection data shape finer than one flag per `Linedef`.
      - "Select all connected same-texture walls"-style flood-fill
        selection utilities (and similar UDB texture-alignment helpers) -
        raised as a "probably wanted eventually" idea, not designed or
        scoped at all yet.
      - A shared `MapElement` base class unifying the now-3x-repeated
        per-type dirty/selection boilerplate (see above).

      **Update, selection lifecycle corrections against real UDB
      behavior:** testing the foundation above surfaced three real gaps,
      each verified against the actual UDB source rather than assumed
      (the user's own recollection and this pass's first-cut
      implementation were each wrong in different ways from what UDB
      actually does):
      1. **Plain click now toggles, not selects-only.** UDB's real
         `classicselect` action (`VerticesMode.OnSelectEnd`/
         `LinedefsMode.OnSelectEnd`/etc.) genuinely adds/removes just the
         clicked element with no modifier needed - Shift/Ctrl only affect
         marquee-drag mode, which this project doesn't have. The
         Shift-vs-plain-click distinction from the first pass was
         dropped entirely, in both 2D (`MapOverlay`) and 3D (`MapView`).
      2. **Mode switching now runs a real geometric conversion**
         (`MapData.ConvertGeometrySelection`, wired into
         `MapOverlay.Mode`'s setter), a close port of UDB's actual
         `MapSet.ConvertSelection` (`Source/Core/Map/MapSet.cs:1163-1262`,
         full algorithm extracted from source, not guessed): a vertex
         selection converting to Linedefs only selects a linedef when
         *both* its vertices were selected; a Linedefs selection
         converting to Sectors only selects a sector when *every* linedef
         bordering it - across every loop it has, holes included, with
         zero special-casing - is selected, OR the sector was already
         selected (the one place prior selection is preserved rather than
         replaced). Neither a plain clear (what the user's own memory
         expected) nor the original persist-untouched behavior (what this
         pass first built) matches this - it's a real third thing.
         `Core.Map.GeometrySelectionType` (Vertices/Linedefs/Sectors, no
         Things) is the new small enum for it. Switching to Things mode
         runs no conversion at all - Things selection is fully
         independent of this, matching UDB exactly.
      3. **3D visual-mode selection is now genuinely separate from 2D
         classic-mode selection**, bridged only at the moment of
         entering/leaving 3D (`MapView`'s `Key.Tab` handler), instead of
         always sharing the literal same `Sector.IsSelected`/
         `Linedef.IsSelected` state the first pass built. Matches UDB's
         real model (confirmed via source): its visual-mode wrapper
         objects carry their own local `selected` field, never
         initialized from the classic `Selected` flag, only synced in
         `BaseVisualMode.OnEngage`/`OnDisengage`
         (`Source/Plugins/BuilderModes/VisualModes/BaseVisualMode.cs:1441-1568`).
         New local `_selectedSectors3D`/`_selectedLinedefs3D`
         (`HashSet<Sector>`/`HashSet<Linedef>`) live in `MapView` itself
         (App-layer session state, not `Core.Map` - mirrors how
         `MapOverlay`'s own hover/drag fields already work), seeded from
         the classic selection on entering 3D and written back on
         leaving it; `TargetHighlight.UpdateHighlights` takes those two
         collections directly instead of a `MapData` reference. UDB gates
         this sync behind a `SyncSelection` setting + Shift-modifier
         combo this project has no settings surface for yet - this port
         always syncs instead (flagged, not silently simplified).
         Confirming that a *mixed* sector+linedef selection within 3D
         mode itself is fine and intentional in UDB (a single unified
         selection list there) was the other half of this research pass -
         that part of the original design was already correct.

      **Update, fixed a real reported bug - stale linedef highlighting
      after deselecting a sector:** selecting linedefs forming a sector,
      switching to Sectors mode, then toggling the sector off left its
      border linedefs stuck showing as selected, with no way to clear
      them. Root cause, verified against UDB's real source rather than
      guessed: `ConvertGeometrySelection` correctly sets a qualifying
      sector's border linedefs selected when *converting into* Sectors
      mode (matching UDB's real `MapSet.ConvertSelection` exactly), but
      nothing then kept that in sync afterward - `MapData.ToggleSelect`/
      `SelectOnly`/`ClearSelectedSectors` on a `Sector` only ever touched
      `Sector.IsSelected` itself. UDB's own real `SectorsMode.SelectSector`
      (`Source/Plugins/BuilderModes/ClassicModes/SectorsMode.cs:538-637`)
      does more: every time a sector's selection changes, it resyncs
      every bordering linedef's `Selected` to `(front sector selected) OR
      (back sector selected)` - confirmed this isn't a renderer bug either
      (UDB's real `Renderer2D.DetermineLinedefColor` shows `Selected`
      unconditionally, not gated by which classic mode is active, and
      DoomArchitect's own renderer already matched that correctly). Ported
      that resync (`MapData.ResyncSectorBoundarySelection`, called from
      `ToggleSelect(Sector)`/`SelectOnly(Sector)`/`ClearSelectedSectors()`)
      - the OR-across-both-sides rule matters for a linedef shared between
      two sectors, which must stay selected if *either* bordering sector
      still is. One real subtlety caught during implementation: the first
      attempt put this resync directly inside the shared
      `ClearSelectedSectors()` method, which `ConvertGeometrySelection`
      also calls internally as pure bookkeeping *after* already deriving
      the correct target-type selection - resyncing there stomped on
      linedef selection that had just been correctly computed. Fixed by
      splitting out a private `ClearSelectedSectorsRaw()` (flags only, no
      resync) for that internal bookkeeping use, keeping the public,
      resync-enabled `ClearSelectedSectors()` for actual user-facing
      sector-deselection gestures only.

      **Update, fixed a real reported bug - dragging a multi-selection
      only moved the hovered element and deselected it:** root cause was
      that every `Handle*Input` method conflated two different gestures
      into one left-click handler - "click to toggle selection" and
      "press-and-drag to move" - so starting a drag on an already-selected
      element immediately toggled it *off* (the same code path always
      flipped selection), then only ever moved the one field-tracked
      dragged element, never the rest of the selection. Verified against
      UDB's real source rather than reinvented (the user explicitly asked
      for this): UDB never conflates the two gestures at all - **left
      mouse button only ever selects/toggles (drag-with-left is reserved
      for box-select, not built here); right mouse button is the only
      thing that ever moves geometry**, confirmed identical across
      `VerticesMode`/`LinedefsMode`/`SectorsMode`/`ThingsMode`'s
      `OnDragStart`. Its real rule: pressing on an unselected element
      replaces the whole selection with just that one before dragging it;
      pressing on an already-selected element preserves and drags the
      *entire* current selection together, with nothing deselected on a
      successful drag. Ported this exactly: all four `Handle*Input`
      methods now split left-click (toggle only, never drags) from
      right-click-drag (the `SelectOnly`-if-unselected rule, then a
      snapshot of every selected element's start position - a
      `Dictionary<Vertex, MapVector2>`, or `Dictionary<Thing, MapVector2>`
      for Things - moved by one shared delta per frame, deduplicated via
      `.Distinct()` where a Linedef/Sector selection can share vertices
      with a neighbor, batched into one `CommandGroup` undo step on
      release). Pure App-layer change (`Scripts/View/MapOverlay.cs`) - no
      Core changes needed, every primitive it uses already existed from
      the selection foundation work (`SelectOnly` in particular had no
      real caller until this).
