# 2D tag/action indicators

**Status:** Done  
**Area:** 2D editing

- [x] 2D tag/action indicators, done 2026-09-20 - UDB's own real
      `Association.cs`/`LinedefsMode.SetupSectorLabels`/`Renderer2D.DetermineLinedefColor`
      family, ported after a real correction mid-research: the first pass
      found the *mechanism* for the linedef-action color tint (a
      configurable "Linedef Color Presets" list) but reported it as user-
      configured/empty-by-default - the user, from real UDB use, pushed
      back specifically ("switches are greenish and i've never set that
      up"), and a direct second check found the real cause:
      `ConfigurationInfo`'s own load path seeds exactly one hardcoded
      preset whenever nothing's been saved yet (`Action == -1` → `System.Drawing.Color.PaleGreen`),
      which is what essentially every real UDB install actually shows.

      New Core: `MapDataTagQueries` gained `GetSectorsWithTag`/
      `GetLinedefsWithTag` (the reverse of its own existing `ParseTags`,
      needed to resolve "what does this tag actually point at"), and
      `Geometry/SectorBounds.cs` (a sector's own bbox from
      `SectorTracer.Trace`'s traced vertices - this project's own stand-in
      for UDB's real precomputed pole-of-inaccessibility label point,
      using exactly UDB's own real *fallback* for when it lacks one -
      `s.BBox.X + s.BBox.Width/2, ...` - not an arbitrary shortcut).

      New App: `Scripts/View/ScreenLabel.cs` - `DrawOverlayHandler.DrawLengthLabel`'s
      own screen-space-anchored, background-panel-behind-text technique,
      extracted so the new sector tag label could reuse it exactly rather
      than duplicate it (`DrawLengthLabel` itself now calls into this too).
      `SectorOverlayHandler` gained a public static `Fill` (its own hover-
      fill triangulation loop, extracted the same way, so the tag-arrow's
      own target-sector highlight can reuse the identical trace ->
      nest -> cut -> ear-clip pipeline). `LinedefOverlayHandler`/
      `SectorOverlayHandler` both gained a public `Hovered` passthrough
      onto their own generic `ElementOverlayHandler`'s already-tracked
      hover state (only ever live while that specific mode is actually
      engaged - matches UDB's own real per-mode `Highlight()`, not a
      mode-agnostic global hover, so no compromise there). New
      `TagIndicatorOverlayHandler` (its own handler, not folded into
      either element handler, since it spans both and needs to read
      hover from outside either one) does the real work: every tagged
      sector gets a permanent "Tag N"/"Tags N, M" label (not hover-gated,
      matching UDB's own real `LinedefsMode.SetupSectorLabels`); hovering
      a tagged linedef (Linedefs mode) or tagged sector (Sectors mode)
      resolves the other side via the new reverse lookup and draws an
      arrow - UDB's own real open "V" arrowhead shape/constants (16px,
      0.46 rad half-angle), computed directly in projected screen space
      via a standard rotation formula rather than copying UDB's own map-
      space-plus-inverse-scale one (which assumes a coordinate handedness
      this project's own Y-down screen space doesn't share) - with the
      target sector(s) flood-filled in the hover color at ~50% alpha,
      matching UDB's own real forward-only fill behavior (hovering a
      sector draws arrows *from* its linked linedefs but fills nothing,
      confirmed directly from `Association.Render`'s own source: only its
      forward, linedef-to-sector branch ever populates the sector list it
      fills). `LinedefOverlayHandler.Draw` also applies UDB's real
      PaleGreen action tint whenever `special != 0`, with hover/selection
      still winning over it first, matching `DetermineLinedefColor`'s own
      real priority order.

      One new keybind action, `toggle_tag_indicators` (default `I`,
      matching UDB's own real `gztoggleeventlines` key) plus a toolbar
      button (`GridToolbar`, the closest existing panel in the right
      screen location - a whole new toolbar for one button would be more
      machinery than this needs) - gates the labels and arrows together;
      the linedef action tint stays unconditional, matching UDB's own
      real always-on behavior (no toggle gates it there either).

      Scope, flagged rather than silently narrowed: Doom-format plain tag
      matching only (`id`/`moreids` overlap) - UDB's own real Hexen/UDMF
      "generalized" path additionally resolves tags out of a linedef's own
      action *arguments*, which needs per-argument "this is a tag"
      metadata `MapDataTagQueries`'s own doc comment already flags as an
      existing, separate gap; one combined toggle instead of UDB's own
      real two independent settings (`GZShowEventLines` for arrows,
      `ViewSelectionEffects` for labels); a single arrow/label color
      (`InfoLine`) instead of UDB's own real per-action-type round-robin
      palette; and no text label on the arrows themselves (the sector's
      own tag label plus the arrow shape and fill already answer "what
      does this point at," and UDB's own real arrow-label placement has
      its own "merge nearby, clamp to screen" complexity not worth this
      pass's scope).
