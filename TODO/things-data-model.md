# Things data model

**Status:** Done  
**Area:** Things

- [x] Things (map objects) - data model (`Core.Map.Thing`) + parsing
      (classic binary THINGS, UDMF `thing` blocks) + a generic placeholder
      rendering in both views. The obvious worry going in - real Thing
      rendering means showing an actual monster/item sprite sized to its
      real type's radius/height, which needs a DoomEd-number-to-actor
      database (the Game configuration system, right below) - turned out
      not to block anything: UDB's own `DataManager.GetThingInfo` always
      returns something renderable even for a totally unrecognized type
      number (a synthesized fallback, radius 10/height 20, its own bundled
      placeholder sprite), used by both UDB's 2D and 3D thing rendering.
      This pass mirrors that bootstrap path with the user's own hand-made
      icon (`Assets/Icons/icon_thing.svg`) instead of UDB's bundled one.
      Only `x`/`y`/`height`/`angle`/`type` are modeled as typed properties
      (matching UDB's own UDMF required/defaulted thing fields exactly);
      everything else (id/pitch/roll/scale/special/arg0-4, every skill/
      ambush/coop flag) round-trips through `CustomFields` - UDB's exact
      write-omission rules for those fields were researched specifically
      to confirm this needs no per-field replication, since we never
      synthesize a value for an absent field and write back verbatim
      whatever's present. Classic-format flags are preserved as a raw
      `ushort` (`Thing.RawFlags`), not decoded into named booleans - real
      translation-table work with no game-config data to drive it yet.
      3D billboard uses Godot's own `BillboardMode.FixedY` (yaw-only,
      matching UDB's own default Thing billboard behavior) rather than
      porting UDB's per-frame camera-relative rotation matrix - simpler
      for an identical visual result. 2D originally used a single flat
      icon texture, rotated to the thing's real angle and sized in world
      space (scaling with zoom, not a fixed screen-pixel size) via the
      same world-size-to-screen-pixels conversion already used for the
      adaptive grid - since replaced with UDB's own real sprite-based
      approach, see the dedicated entry below.
      **Deliberately not built**: no editing (placing/dragging/deleting,
      no `EditMode.Things`) - data model + rendering only, per the literal
      scope of this item; no per-type sprite/size differentiation (every
      Thing looks identical - arrives with the Game configuration system);
      no viewing-angle-based sprite rotation selection (UDB's real 8-frame-
      per-angle system needs real sprite data this pass doesn't have -
      confirmed via source, not guessed, so this isn't a gap waiting to be
      noticed later, it's a documented, deliberate deferral).
