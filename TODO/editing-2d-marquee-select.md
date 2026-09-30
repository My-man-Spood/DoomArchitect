# 2D marquee/box-select

**Status:** Done  
**Area:** 2D editing

- [x] 2D marquee/box-select, ported from UDB's real mechanics
      (`Source/Plugins/BuilderModes/ClassicModes/*Mode.cs`,
      `Source/Core/Editing/ClassicMode.cs`) - the natural next piece once
      left-click stopped moving anything (see above). A real surprise
      worth recording: UDB's marquee isn't "point-in-rectangle for
      everything" - Linedefs need **both** endpoints inside by default;
      Sectors need **every** vertex inside (equivalent to a fully
      contained bounding box); Things use plain center-point with **no
      radius consideration at all**, despite Things having real radius
      data used everywhere else in this codebase (hover-picking, 2D icon
      sizing, 3D billboarding) - confirmed as UDB's own actual behavior,
      not something to "fix." `Core.Map.MarqueeSelectionMode`
      (Select/Add/Subtract/Intersect, chosen from Ctrl/Shift exactly like
      UDB's real `BaseClassicMode.GetMultiSelectionMode`) plus 4 new
      `MapData.MarqueeSelectX` methods implement the exact per-type hit
      tests and the real 4-case apply logic (iterates *every* element of
      that type, not just already-selected ones); Sectors mode also
      re-runs the existing `ResyncSectorBoundarySelection` afterward,
      same as the earlier sector-toggle fix. `MapOverlay`'s left-button
      handling had to move its plain-click toggle from press to release,
      gated on whether the drag ever crossed a 2px threshold (matching
      UDB's own `MouseSelectionThreshold`/`!selecting` split) - press no
      longer decides click-vs-drag by itself.
      **Update, the "select touching" toggle got added back in**: this
      pass initially scoped out UDB's secondary `MarqueSelectTouching`
      toggle (loosens Linedefs/Sectors to a crossing/intersecting test)
      as its own separate UI feature with no obvious home - the user
      pushed back and asked for it built too, as a real menu toggle, with
      correct terminology looked up rather than invented. UDB's own real
      strings (`Source/Plugins/BuilderModes/Interface/
      MenusForm.Designer.cs:804-815`, a toolbar `ToolStripButton`) are
      **"Select Touching"** and (from its own tooltip/status text) "select
      inside" - both reused directly for a new Preferences > **"Selection
      Box"** submenu with two radio-checkable, mutually-exclusive items
      (`MainMenuBar.cs`, `Scenes/Main.tscn`'s new `SelectionBox` PopupMenu
      node), instead of UDB's real per-mode toolbar button placement -
      this project's menu bar is where settings-like toggles already
      live. Session-only, matching UDB's own confirmed real behavior
      (`marqueSelectTouching` has no `ReadPluginSetting`/
      `WritePluginSetting` anywhere, unlike its sibling settings that do -
      always resets to "off" on relaunch) - not persisted into
      `AppSettings`. New `Core.Geometry.SegmentIntersection` (a standard
      orientation-based segment-vs-segment test, written fresh rather than
      sourced - ordinary well-known 2D math, not a map-format fact needing
      a UDB citation) backs the crossing tests for both Linedefs and
      Sectors touching-mode.
      **Deliberately not built**: UDB's `AdditiveSelect` toggle (would
      invert plain-Shift's meaning - not needed, its default already
      matches what's here); distance-ordered marquee selection (UDB sorts
      a marquee's hit-set by distance from the drag origin for index-
      label purposes - no such labeling feature exists here yet); any
      marquee/box-select concept in 3D visual mode (UDB doesn't have one
      there either).
