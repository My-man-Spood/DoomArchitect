# Things edit mode

**Status:** Done  
**Area:** Things

- [x] Things edit mode (select/hover/move) - a 4th `EditMode`, added
      ahead of Property editing UI below since that item reminded the
      user Things had no selection mode at all yet. Mirrors the existing
      Vertices/Linedefs/Sectors hover-find + drag + undo-record pattern
      in `MapOverlay.cs` exactly (`HandleThingInput`/`FindThingNear`, a
      new `Core.Undo.MoveThingCommand`, `MapData.MoveThing`/
      `GetDirtyThings`/`ClearDirty(Thing)` mirroring `Sector.NeedsRebuild`'s
      dirty-flag idiom, named `Thing.NeedsUpdate` since a moved Thing only
      needs a position resync, not a mesh rebuild - `MapView` extracts a
      shared `ResolveThingWorldZ` helper used both at creation and by a
      new per-frame dirty-things sync). Picking uses each Thing's own real
      on-screen radius (via the same per-type lookup `DrawThings` already
      does) rather than a small fixed pick radius - truer "click what you
      see" given how much Thing footprints vary (a Spider Mastermind vs.
      a key). Movement snaps to grid through the same `EffectiveSnap`
      helper every other mode already shares.
      **Real bug found and fixed in the process**: this project's
      existing Vertices/Linedefs/Sectors keybinds (1/2/3) never actually
      matched UDB's real defaults at all - confirmed via UDB's own
      bundled `Assets/Common/UDBuilder.default.cfg`
      (`buildermodes_verticesmode/linedefsmode/sectorsmode/thingsmode =
      86/76/83/84`, the raw key codes for **V**/**L**/**S**/**T** - letter
      keys, not numbers). An uncorrected divergence from before this
      session, only caught while looking up a real default key for the
      new Things mode. Corrected all four to V/L/S/T rather than bolting
      "4" onto an already-wrong scheme.
