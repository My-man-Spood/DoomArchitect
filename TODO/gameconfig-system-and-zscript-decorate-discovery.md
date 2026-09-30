# Game configuration system (.cfg, thing types, ZScript/DECORATE discovery)

**Status:** Done  
**Area:** Game config

- [x] Game configuration system (linedef actions, thing types, sector
      specials) - a real, from-scratch parser for UDB's actual `.cfg`
      grammar (`Core.Configuration.CfgParser`/`CfgLoader`), not hardcoded
      C# data. Two decisions changed this from the original "port UDB's
      game configs" wording, both discussed with the user directly:
      **(1) External file, not hardcoded C#** - the first draft of this
      plan proposed a static C# table, reasoning that two known,
      unchanging vanilla games didn't need a parsed external format. The
      user pushed back with this project's actual end goal in view:
      DoomArchitect targets full UDB feature parity plus custom
      extensions (recorded as its own standing principle,
      `feedback_aim_for_full_udb_port` in memory), and UDB itself uses
      external, user-authorable `.cfg` files specifically because it's an
      open-ended, moddable editor - a future DECORATE-defined-actor
      feature (confirmed in scope, not built yet) and a stated wish to let
      someone bring their own real UDB `.cfg` file over both need that
      same foundation. **(2) Licensing** - UDB's own bundled `.cfg` data
      files are GPLv3, this repo is MIT. Building a parser for the
      *grammar* has no licensing issue (freshly written from reading
      UDB's real `Configuration.cs`, not copied from it - the same
      "read the source, write fresh code" approach used everywhere else in
      this project); a user pointing DoomArchitect at their own existing
      UDB `.cfg` file isn't a licensing issue either (no redistribution by
      DoomArchitect). What needed care: DoomArchitect's own *bundled and
      shipped* `Doom.cfg`/`Doom2.cfg`/`Includes/*.cfg`
      (`src/DoomArchitect.Core/Configuration/GameConfigs/`, embedded
      resources so they're covered by `DoomArchitect.Core.Tests` with no
      Godot dependency) are authored fresh from public, decades-established
      vanilla Doom engine knowledge (DoomWiki-level facts, independently
      republished across dozens of differently-licensed source ports),
      not copied or closely derived from UDB's actual files.
      `CfgParser`/`CfgLoader`'s grammar and merge semantics were verified
      against UDB's real `Source/Core/IO/Configuration.cs`, not assumed -
      notably, `include()`'s merge favors the *included* file's values
      over whatever the including scope already had at that point for a
      plain leaf conflict (`Combine(cs, inc)` - `inc` wins), which is the
      opposite of the "local overrides always win" assumption it'd be easy
      to guess instead; self-inclusion is rejected using UDB's own real
      (narrow) check - the include argument as literally written compared
      against the including file's bare filename, not a general cycle
      detector (UDB's own doesn't have one either, though a lightweight
      recursion guard was added on top here purely to turn a genuine
      multi-file cycle into a clear exception instead of a stack
      overflow - a robustness addition, not a ported behavior). Doom2's
      own `.cfg` needs no C#-level "fall back to Doom's table" logic at
      all: it `include()`s the shared linedeftypes/sectortypes and base
      thingtypes, then layers its own exclusive monsters/items into the
      very same categories - the merge combines them automatically before
      `GameConfigurationLoader` ever sees the result. `IGameConfiguration`
      is deliberately an interface (mirroring the `IMapTargetFinder`
      precedent) so a future DECORATE-lump-driven or user-supplied-file
      implementation can sit behind the same seam later. Wired into the
      concrete, visible payoff promised back in the Things pass: real
      per-type radius/height and the real sprite texture (looked up by
      the exact frame name a `.cfg` entry specifies, e.g. `"POSSA1"` - no
      rotation-frame guessing needed) now render in both `MapView`'s 3D
      billboard and `MapOverlay`'s 2D icon sizing, replacing the one
      shared generic placeholder for any recognized type; `Hangs`
      (ceiling-attached decorations) is modeled and honored, measuring
      down from the sector's ceiling instead of up from its floor.
      Game selection is an always-shown, explicit picker in
      `OpenMapMenu` - matching UDB's own real "Configurations" dialog UX,
      not a silent auto-detect - pre-selected by
      `GameConfigurationDetector`'s best guess (map name pattern, then a
      Doom2-exclusive-doomednum content signature, then a filename hint,
      else Doom) but always requiring confirmation. No persistence of the
      choice yet (asks again on every load - matches the multi-map
      picker's existing precedent; UDB's own real mechanism, best
      understanding not verified in source, is a `.dbs` sidecar file this
      project has no equivalent concept for yet). **Deliberately not
      built**: linedef-action/sector-special data is parsed and tested but
      not wired into any UI yet (no Property editing UI exists); the
      bundled `.cfg` content is an intentionally partial starter set (all
      vanilla monsters/weapons/ammo/keys/health-armor for both games, a
      representative handful of decorations and linedef actions/sector
      specials, not an exhaustive transcription of every one that ever
      shipped - each entry authored and spot-checked individually, not
      mass-generated); viewing-angle sprite rotation selection (still just
      one canonical frame per type, exactly as specified in the `.cfg`
      data); DECORATE/ZScript custom-actor parsing (the real reason
      `IGameConfiguration` is an interface, not a concrete class); and any
      persistent per-WAD project-file storage of the chosen game
      configuration.

      **Update, license change + real `.cfg` swap-over (2026):** the
      project moved from MIT to GPLv3, removing the licensing half of the
      "why authored fresh instead of copied" reasoning above - and the
      bundled starter set was then actually replaced with UDB's own real
      files: `Doom_DoomDoom.cfg`/`Doom_Doom2Doom.cfg`/`GZDoom_DoomUDMF.cfg`
      and their full `include()` closure (25 more files), copied verbatim
      from `Assets/Common/Configurations/` apart from renaming the three
      top-level files to match this project's existing
      `GameConfigurationKind` names. Dramatically more complete than the
      old hand-authored set (Doom: 97 thing types/140 actions/16 sector
      specials; Doom2: 123/140/16; GZDoom UDMF: 346/221/94, versus a
      deliberately partial starter roster before).

      Two real compatibility gaps this surfaced, both genuine parser/
      loader bugs rather than anything specific to these particular files
      (a synthetic test file just never happened to exercise them):
      `FileSystemCfgFileSource`/`EmbeddedResourceCfgFileSource.ResolveRelative`
      only handled forward slashes, but every real UDB `include()` path
      is written with a backslash (its own Windows-native convention) -
      normalized now, on both. `CfgValue.AsBool()` only accepted the
      literal `true`/`false` keyword, but real data writes plenty of
      boolean-semantic fields (`hangs`, `arrow`) as a plain `0`/`1`
      instead - now coerced the same way `AsDouble()` already coerces an
      int-kinded value; `AsDouble()` itself also gained a numeric-string
      fallback (`width = "16";` shows up a handful of times, an authoring
      inconsistency in UDB's own decades-old data, not a distinct
      format). `GameConfigurationLoader.LoadFlagInfoDictionary` also
      gained support for a nested-block flag entry (`linedefactivations`'
      own `repeatspecial`/`passuse`, which carry a `name` field plus
      metadata this project's simple key/title record doesn't model,
      instead of the far more common plain `key = "Title";` assignment)-
      previously silently dropped rather than erroring, so easy to miss.

      A few existing tests had asserted a title/category string this
      project had itself guessed rather than checked (`"Secret area"` vs
      real UDB's own `"Secret"`, `"doors"` vs real UDB's own `"door"`),
      and one had asserted vanilla Doom defines no thing flags at all -
      wrong; it defines the classic 5 (skill/ambush/multiplayer) - all
      corrected to match the real data instead of the old guesses.

      **Update, "internal:" sprite icons (2026):** the real `.cfg` data
      revealed a genuine gap this project's own hand-authored starter set
      never surfaced: 30 thing types across the GZDoom/ZDoom/Boom layers
      (`MapSpot`, `Camera`, `Teleport`, `Slope`, `SilentSector`,
      `SkyboxViewpoint`, ... - editor-only markers with no real in-game
      sprite) store a `sprite` field like `"internal:MapSpot"` instead of
      a real WAD lump name, UDB's own `DataManager.INTERNAL_PREFIX`
      convention for its own bundled marker icons. This project had zero
      handling for that prefix, so all 30 silently fell through to the
      generic missing-sprite placeholder. Fixed with a new
      `Core.Textures.InternalSprites`, backed by UDB's own real icon PNGs
      (`Assets/Common/Sprites/*.png`, all confirmed present for every name
      actually used) bundled as embedded resources and matched
      case-insensitively (real data spells some of these lowercase, e.g.
      `"internal:pointpusher"`), decoded through the same
      `PatchImageResolver` path every other modern-format image already
      goes through. `TextureSet.TryGetSpriteTexture` checks for the
      prefix before ever touching the loaded WAD's own sprite lumps.

      **Update, thing categories + 2D marker color/direction:** two more
      real UDB `.cfg` fields modeled - `arrow` (nonzero = show a facing
      indicator) and `color` (a small palette index) - both category-
      level with per-entry overrides, same inheritance rule as width/
      height. `color` resolves against DoomArchitect's own palette
      (`Rendering.ThingCategoryColors`), not UDB's actual editor color
      scheme (that's UDB's own UI design, not a vanilla-Doom fact, unlike
      radius/height/sprite names). The 2D marker now uses the plain ring
      icon (`icon_thing_nodir.svg`, unused since the Things pass) for any
      type that doesn't actually rotate in gameplay instead of the
      directional notch one for everything - showing a facing indicator
      for something with no meaningful facing is actively misleading, not
      just extra detail. Per-key coloring (blue/yellow/red keys each
      tinted their own color) was tried and reverted - ambiguous at a
      glance against other categories using similar hues; one shared
      "keys" color instead, matching how every other category works.
      This also prompted checking the actual category *names/grouping*
      against UDB's real `Doom_things.cfg`/`Doom2_things.cfg` directly
      (cross-referenced via `awk`, not guessed) - two real mistakes found
      and fixed: "Teleport landing" (14) is its own `teleports` category
      in UDB, not part of `players`; and `health` splits from `powerups`
      (soul sphere/invulnerability/berserk/partial invisibility/radiation
      suit/computer area map/light amp visor/megasphere are `powerups`,
      not `health` - a distinction easy to miss since they're all
      pickups with a similar "buff" feel, but UDB keeps them separate).
      Category names now match UDB's real ones exactly (players/
      teleports/monsters/weapons/ammunition/health/powerups/keys/
      obstacles) rather than DoomArchitect's own prior invented grouping
      (which had merged health+powerups and used "ammo"/"decorations"
      instead of "ammunition"/"obstacles"). UDB's own `lights` category
      isn't represented yet - no light-emitting decoration thing types
      are modeled in the current intentionally partial starter set.

      **Update, live ZScript/DECORATE actor discovery, done (2026-09-27):**
      the DECORATE/ZScript custom-actor parsing flagged as deferred above
      is now fully implemented and wired in - full plan at
      `/home/spood/.claude-personnal/plans/warm-yawning-music.md`, a real
      port of UDB's actual `ZScriptTokenizer`/`ZDTextParser`/`ZScriptParser`/
      `ZScriptActorStructure`/`DecorateParser`/`DecorateActorStructure`
      (verified against the real source, grammar and all - not a lighter
      approximation), so a mod's own custom actors show up as placeable
      Things instead of only this project's static `.cfg`-defined ones.
      Standing rule for this pass, per explicit user direction: nothing UDB
      does here gets permanently cut for convenience - anything not ported
      in the current phase is tracked below with why and what triggers
      picking it back up, not silently dropped.
      - **Phase 1 done**: `ZScriptTokenizer.cs` ported near-verbatim
        (genuinely zero UDB-internal coupling, confirmed by reading the
        file, not just grepping it - see
        `feedback_grep_coupling_estimates` in memory for why the first,
        grep-based estimate of a sibling file's coupling was wrong).
        `ZDTextParser.cs` ported for its real character-level parsing
        algorithms (`SkipWhitespace`/`ReadToken`/`ReadLine`/`NextTokenIs`/
        `SkipStructure`, all verbatim) - `src/DoomArchitect.Core/ZDoom/`,
        37 passing tests.
      - **Phase 2 done**: the full actor-structure parsing pipeline, both
        formats - `ActorStructure`/`StateStructure`/`StateGoto`/
        `DecorateCategoryInfo` (shared foundation, including
        `ThingTypeInfo.ClassName` and `IGameConfiguration.DecorateGames`
        additions so inheriting from a static `.cfg` actor resolves);
        `DecorateParser`/`DecorateActorStructure`/`DecorateStateStructure`/
        `DecorateStateGoto`; `ZScriptParser`/`ZScriptActorStructure`/
        `ZScriptStateStructure`/`ZScriptStateGoto`. 76 new tests exercising
        real snippets end to end (inheritance incl. ZScript's forward-
        reference support, flags, states/sprite resolution, `#include`
        resolution via an injected resolver delegate, regions-as-
        categories, `extend class`, `mixin class`, inheriting from a static
        engine actor). `$argN` metadata is captured as a reduced
        `ActorArgumentInfo` (used + title only, see its own doc comment for
        why the full render-hint richness is deferred, tracked below).
        Two more genuine UDB quirks caught and pinned as regression tests
        along the way, on top of Phase 1's own two: a DECORATE property or
        `Game` value list is silently discarded if its actor's closing `}`
        lands on the same line as the last value (no property-list ever
        reaches its own assignment in that case - same bug in real UDB,
        not this port); a ZScript `#region` name is read with no leading-
        whitespace skip, so a leading space becomes part of the category
        string itself. `uservars`/`uservar_defaults` (custom `user_*`
        ZScript field capture) are the one thing NOT ported from either
        actor-structure file - see `ActorStructure`'s own doc comment,
        tracked below alongside the render-hint `ArgumentInfo` richness.
      - **Phase 3 done**: `MapinfoParser` - a narrow port of UDB's real
        `MapinfoParser`, enough to find and parse a `DoomEdNums { }` block
        anywhere in a MAPINFO lump (which is the only reason this project
        needs MAPINFO at all - giving a ZScript-only actor, which never
        carries its own editor number, a real DoomEdNum). Every other
        MAPINFO block (`map`/`defaultmap`/`gameinfo`/`spawnnums`/anything
        else) is skipped via the already-ported `ZDTextParser.SkipStructure`
        rather than actually parsed - a full MAPINFO parser (map titles,
        sky/fog, intermissions, ...) is a substantial, separate feature with
        no current consumer, tracked below alongside the port's other
        deferred sub-pieces. 6 new tests.
      - **Phase 4 done**: `DiscoveredActorThingTypeMerge` - a close port of
        UDB's real `DataManager.ApplyZDoomThings`, mapped onto this
        project's own much simpler `ThingTypeInfo` (10 scalar fields vs.
        UDB's real rendering-property richness - alpha, renderstyle,
        per-cvar distance checks, wallsprite/flatsprite/rollsprite,
        dynamic light type, a 5-slot argument array - every field ported
        faithfully, the rest has nothing to receive it, tracked below).
        Same real merge order and collision rules: DECORATE wins over
        ZScript on a classname collision; `replaces` updates the replaced
        actor's existing `ThingTypeInfo` in place at its own DoomEdNum, not
        as a new entry; a new positive DoomEdNum inherits defaults from the
        static entry matching `InheritsClass`, when one exists; MAPINFO
        `DoomEdNums` overrides run last and can delete an entry entirely
        (`"none"`). `DiscoveredActorGameConfiguration.Load(...)` wraps a
        static `IGameConfiguration` with the merged result - only
        `GetThingType`/`GetThingTypes` differ, everything else delegates
        straight through - built fresh every time exactly like
        `TextureSet.Load`, no incremental update. 11 new tests.

        **A real, thread-safety bug found and fixed along the way** (not
        faithfully reproduced): `ZScriptTokenizer`'s named-token lookup
        tables were lazily built on first construction with a bare
        "if null, populate" check - a genuine data race under concurrent
        construction, latent in UDB's own single-threaded WinForms context
        but real and reproducible here once tests construct tokenizers in
        parallel (xUnit's default). Fixed with a static constructor
        (CLR-guaranteed run-once) instead - this is a pure implementation
        detail with no user-observable behavior, so fixing it rather than
        porting it verbatim was the right call, unlike the genuine parser
        quirks pinned as regression tests elsewhere in this port.
      - **Phase 5 done - the port is now feature-complete** (modulo the
        deferred sub-pieces below, all still deliberately tracked, not
        cut). Two new pieces this phase needed, beyond wiring: a real
        `IResourceContainer.FindByPath(path)` (WAD: delegates to
        `FindLump` on a bare title, since a WAD has no path hierarchy at
        all; PK3: an exact normalized-path match, falling back to a
        root-level title match) - the one lookup `FindLump` deliberately
        doesn't cover, needed for resolving a `#include`'s literal nested
        path; and `ZDoom.ResourceActorScanner.Scan(baseConfiguration,
        resourceSet)`, the actual entry point tying every earlier phase
        together - feeds every layered resource's own ZSCRIPT/DECORATE/
        MAPINFO root entry into one shared parser pair per format (ZScript/
        DECORATE content is genuinely cumulative across a real resource
        stack, unlike a texture lookup's highest-priority-wins, so every
        container contributes, not just the top one), wires
        `DecorateParser.ZScriptActors` to the ZScript side's own actors so
        cross-format inheritance actually resolves, then hands the result
        to `DiscoveredActorGameConfiguration.Load`.
        `OpenMapMenu.OnMapOptionsConfirmed` now calls this instead of a
        bare `GameConfigurations.Get(kind)`, so every already-existing
        consumer downstream (`MapView`'s Thing rendering/sprite seeding,
        `ThingEditDialog`, `MapOverlay`) picks up discovered actors for
        free through the same `MapLoaded`/`MapResourcesChanged` events,
        no signature changes needed anywhere. `ThingTypePicker.CategoryOrder`
        (a fixed whitelist that silently hid anything else) now falls
        through to a per-category catch-all afterward - a discovered
        actor's own `$category`/`#region` string gets its own folder,
        titled with that raw string, instead of being dropped. 17 new
        tests (`FindByPath` on both container kinds, and full end-to-end
        `ResourceActorScanner` scans - multi-file `#include` chains, a
        ZScript actor only becoming placeable once MAPINFO assigns it a
        DoomEdNum, DECORATE inheriting a ZScript base class from the same
        resource, actors contributed by multiple layered resources).
      - **Deferred sub-pieces, tracked (not cut)**:
        - A full MAPINFO parser (map titles, sky/fog settings,
          intermissions, episode/cluster definitions, ...) - `MapinfoParser`
          here only ever looks for `DoomEdNums { }`, skipping every other
          block unread. Revisit only if this project ever needs other
          MAPINFO data (e.g. a map-properties panel reading map titles).
        - Live-reload/file-watching (UDB's `ScriptResource`, "re-parse when
          a script file changes on disk") - this project has no file-
          watching for *any* resource type yet (textures, configs,
          scripts alike). Revisit only once general resource hot-reload
          becomes its own feature, not bundled into this port specifically.
        - Diagnostics UI (UDB's `TextResourceErrorItem`/`ErrorLogger` panel
          surfacing "MyMod.pk3's ZSCRIPT has an error on line 40") - the
          ported parsers already track `HasError`/`ErrorDescription`/
          `ErrorLine` internally, so this is purely a missing UI surface,
          not a missing capability. Revisit in Phase 5, once Phases 2-4
          exist and there's real parsed content to report errors about.
        - Rooted-path resolution for Directory-type resources (UDB's
          `GetRootedPath`/`CheckInvalidPathChars`, `DirectoryReader`) - this
          project's `IResourceContainer` only has WAD/PK3 implementations,
          no loose-folder resource type exists anywhere yet. Not a cut
          specific to this port - revisit only if directory-based resources
          themselves ever become a supported resource kind.

          **Update:** directory-based resources are now a real, supported
          resource kind - `IO.DirectoryResource`, a loose-folder
          `IResourceContainer` mirroring `Pk3File`'s exact lookup rules
          (same fallback-namespace order, same first-entry-wins-on-
          duplicate rule) but reading straight off disk instead of a zip
          archive - no separate handle to dispose, content is re-read via
          `File.ReadAllBytes` per lookup. `ResourceContainerFactory.Open`
          checks `Directory.Exists` first; `ResourceListEditor`'s Add
          dialog is `FileModeEnum.OpenAny` (wired to both `FileSelected`
          and `DirSelected`) so a folder can be picked the same way as a
          WAD/PK3. This was surfaced by a real user report: custom
          ZScript monster actors kept in a loose folder (not zipped into
          a PK3) never reached `ResourceActorScanner` at all, since no
          container type existed for them - they're discovered
          automatically now, no scanner changes needed (it only ever
          talked to `IResourceContainer`). **Still deferred, tracked**:
          UDB's own per-resource "roottextures"/"rootflats" checkboxes
          (`ResourceOptionsForm`'s `dir_textures`/`dir_flats`, whether
          loose images directly in the folder root - not inside a
          `textures/`/`flats/` subfolder - should also count) - this
          project's `ResourceListEditor` has no per-resource options UI
          of any kind yet (every resource is just a bare path), so
          there's nowhere to surface a checkbox for this even for WAD's
          own `strictpatches` option. Revisit if/when a per-resource
          options UI gets built for any resource kind, not directories
          specifically. Also deferred: UDB's own
          "load loose WAD files inside a directory root as its own
          sub-resources" quirk (`DirectoryReader.Initialize`) - a real
          but narrow behavior; revisit only if someone actually relies on
          mixing loose WADs inside a folder resource.
        - `uservars`/`uservar_defaults` (custom `user_*` ZScript field
          capture, and the `var TYPE user_name;` parsing that would feed
          it) - no Thing property-editing UI exists to attach custom
          per-actor fields to yet (same reasoning as the `ArgumentInfo`
          richness below). Revisit alongside a real property-editing UI.
        - The full `ArgumentInfo` render-hint richness (enum lists, default
          values, helper-circle/rectangle rendering, range colors) behind
          `$argN` metadata - captured today as a reduced
          `ZDoom.ActorArgumentInfo` (used + title only). Revisit alongside
          a real Thing-argument-editing UI, same as `uservars` above.

          **Update:** `$color` GZDB-comment parsing (UDB's
          `ZDTextParser.GetColorFromString`) was folded in as promised -
          `ZDoom.GzdbColor.TryParse`, hex (`#RGB`/`#RRGGBB`/bare hex) fully
          supported. Named colors ("red", "dodger blue") take an injectable
          lookup table instead of a hardcoded one, since resolving them for
          real means reading gzdoom.pk3's own `x11r6rgb.txt` lump - the
          exact same lump the required-archive fingerprint check
          (this item's own original motivation) keys off - which needs a
          loaded resource this pure string utility has no access to on its
          own. Revisit once something actually wires up that lookup.
      - ACS/scripting compilation (Test Map's real source-port launch,
        `%L`/`%S` placeholders etc., is separately built and working -
        see the Test Map entry below) is explicitly NOT part of this port
        and has its own separate deep-dive already done on UDB's real
        Acc/Bcc/ZtBcc compiler integration, per the user's own explicit
        call to treat it as its own future initiative once they've had
        time to digest that research - not a Phase of this item.
