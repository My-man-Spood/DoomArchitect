# Sector property editing UI (v1)

**Status:** Done  
**Area:** Property editing

- [x] Property editing UI, Sector (v1) - `SectorEditDialog`, ported from
      UDB's real `SectorEditFormUDMF` (both field scope and on-screen
      layout: Properties tab grouped/ordered exactly like UDB's real
      `groupfloorceiling`/`groupeffect`/`groupaction` group boxes, plus a
      real tab strip mirroring UDB's 5 remaining tabs as placeholders).
      Floor/ceiling height+texture, brightness, gravity, special, tag -
      real-time apply while open (heights/textures/brightness) with one
      combined undo step and full revert on cancel; special/tag/gravity
      apply only on OK, matching UDB's own real split. Numeric fields got
      UDB's real blank/absolute/`++N`/`--N`/`*N`/`/N` multi-select grammar
      (`Core.Editing.NumericFieldExpression`) plus real spinner buttons
      (`StepperLineEdit`, porting UDB's actual `ButtonsNumericTextbox`
      step values - 8/16/1 for heights and brightness, 0.1/1/0.01 for
      gravity). Linedef+Sidedef and Thing dialogs were still not started
      at the time this was written - see the dedicated Linedef entry
      below (now done) and TODO.md's own tracking for Thing, the last of
      the three main property dialogs, not yet started.

      **Update, Properties tab closer to real parity + GZDoom UDMF config:**
      a real GZDoom Doom2 UDMF screenshot exposed how much v1 was missing -
      brought up to parity field-by-field against `SectorEditFormUDMF`,
      researched directly rather than guessed (two dedicated research
      passes into `TagsSelector.cs`/`SectorEditFormUDMF.cs` for the
      Identification section alone). Also added the first non-vanilla
      game configuration, `GameConfigurationKind.GZDoomDoom2UDMF`
      (`GZDoomDoom2UDMF.cfg`) - its own real 11-entry sector-flags list, 20
      known GZDoom damage-type strings, and the real ~94-entry UDMF
      sector-types list (replacing vanilla's 16-entry table wholesale, not
      merging - matches real UDMF's independent numbering namespace),
      authored fresh from public engine knowledge per this project's own
      licensing practice, not copied from UDB's actual `.cfg` files.
      Thing/linedef data is temporarily reused from vanilla Doom2 pending
      the Linedef/Thing dialogs.
      New sections: **Flags** (the real 11 UDMF/GZDoom sector-flag
      checkboxes, rebuilt per game configuration from
      `IGameConfiguration.GetSectorFlags()`, untouched-checkbox-leaves-
      value-alone multi-select semantics); **Height Offset** (UDB's real
      transient/UI-only convenience field - never stored, always shows
      "0", nudges floor+ceiling live including bare `++`/`--` = "by this
      sector's own current height"); **Sector damage** (type/amount/
      interval/leakiness); **Sound Sequence** + **Fog Density** added to
      Effects; and a real **Identification** section replacing the old
      plain Tag/Tags text fields - `SectorTagsEditor` ports UDB's actual
      `TagsSelector` control (verified button-by-button against its real
      Click handlers, not guessed from icons): New/Unused/Clear buttons,
      a Clear All + Add/Remove multi-tag row of clickable chips, and the
      same real per-slot-index-lockstep multi-selection model UDB itself
      uses (`List<List<long>>` internally, one list per selected sector,
      a slot shows mixed/blank when selected sectors disagree at that
      index) - not a simplified "one shared value" stand-in. Backed by two
      small new Core pieces: `TagAllocator.FindFree` and
      `MapDataTagQueries.GetUsedTags`/`GetUsedSectorTags` (map-wide vs.
      this-element-type-only free-tag scans, the New/Unused button logic).
      Floor/Ceiling Texture moved to their own real **Surfaces** tab
      (an earlier pass had mistakenly folded them into Properties, modeled
      after the older classic-format dialog's single combined group box -
      corrected once checked against the real UDMF dialog directly). Also
      introduced `GroupBox` (`Scripts/View/GroupBox.cs`/`Scenes/UI/GroupBox.tscn`)
      as a genuine reusable `Container` subclass (real `_GetMinimumSize`/
      `NOTIFICATION_SORT_CHILDREN` implementation, not a plain `Control`
      hand-positioning children) - a titled section box with a 1px border
      interrupted by the title, replacing every bare header-`Label`-above-
      plain-content section in this dialog; and a Sector Special browser
      (`SectorSpecialBrowserDialog`) for the Special field's "Browse..."
      button. `Assets/BaseTheme.tres` also gained a real `LineEdit` style
      and became the actual registered project-wide default theme
      (`project.godot`'s `[gui] theme/custom`) - it had only ever been
      manually applied to a few `Main.tscn` nodes before, so every dialog
      including this one was silently using Godot's raw engine default
      theme the whole time.

      **Explicitly still missing/deferred, not silent gaps:**
      - `damagetype` and Sound Sequence are plain free text, not UDB's
        real combo lists - both are populated in UDB by parsing the map's
        own DECORATE actors / SNDSEQ lumps respectively, and this project
        has neither parser yet.
      - New/Unused only scan sector `id`/`moreids` and linedef `id` -
        UDB's real search also treats a linedef's tag-type *action
        arguments* (e.g. a Teleport's destination tag) as "in use", which
        needs per-argument "is this a tag" metadata this project's
        `IGameConfiguration`/`LinedefActionInfo` schema doesn't model at
        all yet (`Linedef` itself has no typed arg0-4 accessors either,
        just the raw `Fields` bag).
      - No `>=`/`<=` (ascending/descending range across the selection) or
        `++`/`--` (per-collection-position offset) tag-distribution
        grammar - UDB can assign each selected sector a different
        sequential tag from one typed expression; this project only
        applies one absolute value to every selected sector's slot in
        lockstep. Same category of "batch distribute by position" feature
        already declined for `NumericFieldExpression` itself (its
        `+++`/`---` variant).
      - No Generalized Effects bit-flag tab/UI - `generalizedsectors` is
        set `true` in the new GZDoom config, but this project exposes only
        the plain ~94-entry named sector-types list rather than UDB's real
        bit-flag combo editor.
      - Per-field numeric step sizes for the new fields (Fog Density,
        Damage Amount/Interval/Leakiness) are sensible flat defaults, not
        verified against UDB's exact `ButtonStep`/`ButtonStepBig`/
        `ButtonStepSmall` values for these specific fields.
      - Colors, Slopes/Portals, Comment, and Custom tabs are all still
        placeholders ("Not yet implemented").
      - `SectorTagsEditor` is Sector-specific for now, scoped directly to
        `IReadOnlyList<Sector>` - real UDB shares this exact control with
        its Linedef dialog, which doesn't exist in this project yet;
        generalize behind an interface once it does, not before.

      **Update, Surfaces tab per-surface fields:** rebuilt to match UDB's
      real layout, verified directly against `SectorEditFormUDMF.Designer.cs`
      rather than assumed - two group boxes, "Ceiling" then "Floor" (not a
      single combined group, and not Properties-adjacent), each now with
      the real UDMF texture offset (`xpanningfloor`/`ypanningfloor` and the
      ceiling equivalents), scale (`xscalefloor`/`yscalefloor`/ceiling,
      default 1.0), rotation (`rotationfloor`/`rotationceiling`, default
      0.0), and per-surface light override (`lightfloor`/`lightceiling` +
      `lightfloorabsolute`/`lightceilingabsolute`) fields alongside the
      existing texture name - all OK-only, matching the established real-
      time-vs-OK-only split, since none of them have any visual effect to
      preview yet (see below). **Deliberately not built**: UDB's real
      rotation dial widget and "use linedef angles" checkbox (a plain typed
      rotation field covers the same data without an exotic custom
      widget); the render-style dropdown, terrain dropdown, and
      reflectivity field (each needs real infrastructure this project
      doesn't have - a render-style enum, a terrain-type game-config
      schema); the reset-to-default buttons UDB has for the light override
      pair. **A real rendering gap, not just a UI one**: none of these
      fields are actually applied anywhere in `SectorMeshBuilder`/
      `TextureCache` yet - texture offset/scale/rotation don't affect the
      generated UVs, and the per-surface light override doesn't affect
      `Core.Lighting.SectorBrightness`'s computed brightness. They
      round-trip correctly (readable, writable, preserved on save) but
      editing them currently changes nothing you can see - wiring them
      into the actual mesh/lighting pipeline is separate, not-yet-started
      work.
