# Thing-type catalog (full GZDoom/ZDoom/Boom breadth)

**Status:** Done  
**Area:** Game config

- [x] Thing-type catalog, full GZDoom/ZDoom/Boom breadth - the follow-up
      flagged above, done once the dialog/picker framework was proven.
      Replaced the ~46-entry hand-curated `DoomThings.cfg` starter set
      with a scripted, verified extraction from UDB's own real
      `Doom_things.cfg`/`Doom2_things.cfg`/`ZDoom_things.cfg`/
      `GZDoom_things.cfg`/`Boom_things.cfg` (~5000 lines of real source
      across 5 files) - 346 total thing types now, up from 46. A hand-
      typed transcription at this volume was judged too error-prone (the
      same class of mistake the `keys_doom` bug already taught this
      project to avoid), so a small Python parser (matching this
      project's own real `CfgParser` grammar) extracted structured data
      instead of retyping text by hand. Per explicit user decision this
      pass: thing titles are copied verbatim from UDB (short factual
      labels like "Imp"/"Point Light", not creative prose - there's
      really only one correct English name for most of these, unlike the
      per-config `.cfg` prose this project's other bundled data
      independently rewords) - doomednum/sprite/width/height/hangs/arrow/
      category are unchanged objective-fact territory either way. `color`
      is now copied too (per a follow-up user decision, superseding the
      original "invent our own palette" call this same pass started
      with) - `Rendering.ThingCategoryColors` was replaced wholesale with
      UDB's own real shipped-default thing-color palette
      (`ColorCollection.THINGCOLOR00`-`19` in UDB's own source, 20 real
      named `System.Drawing.Color`s), so every category's own `color`
      value is now the same real UDB index, more familiar to anyone
      coming from UDB - no reason to keep diverging there once asked.
      New files mirror UDB's own real physical file/module boundaries and
      `include()` structure exactly rather than flattening everything
      into one file: `Includes/Doom2Things.cfg` (Doom2's own exclusive
      additions, now included by `Doom2.cfg` instead of an inline
      hand-curated `thingtypes` block), `Includes/BoomThings.cfg` (Boom's
      2 generic actors), `Includes/ZDoomThings.cfg` (two named sub-blocks,
      "doom" and "zdoom", matching `ZDoom_things.cfg`'s own real split -
      "zdoom" nests a `BoomThings.cfg` include exactly like UDB's own real
      nested include), `Includes/GZDoomThings.cfg` (two named sub-blocks,
      "gzdoom" and "gzdoom_lights" - the commented-out, never-active
      "lightmaplights" category in UDB's own real source was correctly
      left out, not silently dropped data). `GZDoomDoom2UDMF.cfg`'s own
      `thingtypes` block now chains all of these via `include()`, in
      UDB's own real order. A source-parsed entry contributing nothing
      this project's own `ThingTypeInfo` model tracks (e.g. a `blocking`-
      only override on an already-defined vanilla entry) is correctly
      omitted rather than emitted as meaningless noise. Verified end-to-
      end with new tests resolving a real thing from each of the 4 newly
      wired layers (a ZDoom generic actor, a GZDoom dynamic light, Boom's
      Pusher, a ZDoom stealth-monster variant) through the actual
      `GZDoomDoom2UDMF` configuration, not just that the files parse.
