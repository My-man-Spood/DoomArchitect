# Linedef property editing UI (v1)

**Status:** Done  
**Area:** Property editing

- [x] Property editing UI, Linedef (v1) - `LinedefEditDialog`, ported from
      UDB's real `LinedefEditFormUDMF` - the largest of the three main
      property dialogs, mostly because of its dynamic per-action argument
      editing UI. Properties tab: Action number + "Browse..."
      (`LinedefActionBrowserDialog`, grouped into collapsible per-category
      folders like UDB's real `ActionBrowserForm`, not one flat list -
      matters once the action table reflects real breadth, see below), 5
      fixed argument slots (never dynamically added/removed, matching
      UDB's real `ArgumentsControl`) relabeled per the selected action's
      own real arg0-arg4 metadata and toggling between a numeric field and
      an enum dropdown. Godot has no equivalent to UDB's real editable
      combo-box argument control (which lets you type an exact value even
      for an enum-backed argument) - added a manual click-to-toggle on the
      argument's own label as the adaptation, carrying the value across
      the switch (exact value enum-to-number, nearest match number-to-
      enum). Flags (26 real UDMF booleans) and Activation (10 real UDMF
      triggers) are a genuinely separate group in real UDB even though
      both are just named booleans under the hood - both rebuilt per game
      configuration the same way Sector's own Flags group already is.
      Identification reuses the Sector dialog's own tags control,
      generalized from `SectorTagsEditor` into `MapTagsEditor` (UDB really
      does share this exact control between both dialogs). Front/Back
      tabs: whole-sidedef offset (already modeled, real-time) plus per-
      texture-part (Upper/Middle/Lower) offset/scale/light-override using
      the 18 real, genuinely distinct UDMF field names (verified against
      `UniversalStreamReader`/`Writer`, not assumed). A one-sided line's
      Back tab shows disabled rather than hidden, matching UDB's real
      `Enabled = false` treatment.

      **The linedef action table needed two real correctness passes, not
      just UI work.** First pass shipped only 12 hand-picked Hexen-style
      generic actions - researched the complete real ~191-action table
      (6 parallel research passes into UDB's actual `Hexen_linedefs.cfg`/
      `ZDoom_linedefs.cfg` source) once flagged as far under real GZDoom
      UDMF's own breadth. Second pass fixed a real data bug found on
      review afterward: `keys` (`Door_LockedRaise`/`Generic_Door`'s lock
      argument) shipped Hexen's own puzzle-key names in this Doom2-
      targeted config instead of Doom's real red/blue/yellow keycard/
      skull keys - root cause was flattening UDB's own real per-game
      `enums_doom`/`enums_hexen`/etc. value-list layering into one merged
      table, losing the "which game's list is this" information that
      would have made the correct choice obvious; fixed by keeping a
      `_doom`-suffixed name plus a comment explaining the convention for
      any future Hexen/Heretic config (see [[feedback_udb_is_north_star]]
      in memory - this is now a standing rule: mirror UDB's real file/
      data layering, not just its end behavior, even when the actual
      prose still needs independent authoring for licensing). Also added
      back the numeric-value prefix UDB's own real enum titles always
      carry (e.g. "16: Slow"), dropped during independent rephrasing.
      **Deliberately out of scope:** the other ~24 real UDB argument types
      beyond plain numeric/enum (tag/texture/thing pickers, angle dials,
      etc.); ACS arg0-as-string; a Generalized (Boom) specials tab, same
      reasoning as Sector's own deferred Generalized Effects tab; sidedef
      Custom-fields button; sidedef/sector reassignment or creation;
      Comment/Custom tabs (placeholders, matching Sector's own pattern).
