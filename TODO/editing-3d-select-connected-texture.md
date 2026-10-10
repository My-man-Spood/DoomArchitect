# Select connected walls/flats sharing the same texture (3D mode)

**Status:** Done
**Area:** 3D editing

UDB's real `BaseVisualGeometrySidedef.SelectNeighbours`/
`VisualFloor.SelectNeighbours`/`VisualCeiling.SelectNeighbours`, fired
from its own `EndSelect` (`visualselect`'s real handler) whenever Shift
or Ctrl is held on a select click. Originally scoped to the texture-
match mode only; height-matching for flats was added shortly after
(see the "Update" below) once a real workflow need for it surfaced.

Two genuinely different real algorithms, confirmed by reading both:

- **Walls** (`BaseVisualGeometrySidedef.SelectNeighbours`) - a
  vertex/linedef-chain flood-fill, walking outward from the clicked
  wall part along shared vertices, resolving which of a two-sided
  linedef's Front/Back actually "continues" from a given vertex in a
  given walk direction (so it stays on one consistent face of a
  connected wall loop, never jumping to the unrelated room on the
  other side of a dividing wall). New `Core.Geometry.ConnectedTextureSelector.FindConnectedWalls`
  ports this almost line-for-line from the exact same traversal
  `TextureAutoAligner` already uses (forward/backward job stack,
  `PushNeighbors`/`PartVisibleAndMatches`) - deliberately *not*
  refactored to share code with it (this project's own "one parser per
  format... reconsider once a second real consumer arrives" precedent
  cuts the other way here: the two algorithms' *payloads* differ enough
  - offset accumulation vs. plain reachability - that forcing a shared
  abstraction now would risk the existing, working, tested auto-align
  path for a modest line-count savings). Scoped the same way
  `TextureAutoAligner` already is: same-part-role only (upper-to-upper
  etc, no UDB-style cross-role chaining via its own
  `VisualSidedefParts` machinery), no 3D-floor participation.
- **Flats** (`VisualFloor`/`VisualCeiling.SelectNeighbours`) - a much
  simpler sector-graph walk instead: every sector bordering the current
  one through a shared sidedef, matching the same Floor (or Ceiling)
  texture and/or height. New `ConnectedTextureSelector.FindConnectedSectors`.
  No 3D-floor/vavoom participation, matching the wall side's own scope
  note.

Adapted to this project's selection granularity the exact same way the
earlier multi-select texture-nudge fix was (see
`TODO/editing-3d-texture-nudge-autoalign.md`): UDB's own 3D selection
tracks individual wall parts separately, so "select the connected
group" is unambiguous there. This project's wall selection
(`_selectedLinedefs3D`) only tracks whole linedefs - reaching *any*
matching part on a linedef selects/deselects that whole linedef (the
flat side already matches UDB's own real per-surface granularity
exactly, via the independent `_selectedFloors3D`/`_selectedCeilings3D`
split added shortly after this feature - see
`TODO/editing-3d-height-edit-panning.md`'s own "Update" section for
why). `MapView.HandleThreeDSelectClick` applies UDB's own real semantics
exactly: the connected group gets set to the clicked target's *new*
post-toggle state (selecting an unselected target selects the whole
group; clicking an already-selected one deselects the whole group) -
not an independent toggle per connected surface.

**Real keybind decision, not UDB's own default**: UDB binds this to
Shift+Click; bound to **Ctrl+Click** here instead
(`select_connected_texture_modifier`). Shift is already this project's
3D fly-down camera control, and the user identified a real, more severe
problem with reusing it as *any* 3D-mode modifier than initially
assumed: holding Shift to modify a click can itself drift the camera
down and change what's actually under the crosshair before the click
is even processed, silently acting on the wrong target. Ctrl collides
with nothing in 3D mode.

**Flagged, not pursued now**: Shift-for-fly-down has now caused three
separate keybind decisions this session alone (3D auto-align's own
axis-swap modifier, texture-nudge's 8-pixel modifier, and this
feature) to avoid Shift entirely in 3D mode. Discussed a real
alternative - double-tap Shift to toggle/arm fly-down instead of
holding it, freeing plain Shift for safe modifier use everywhere (the
same scheme as Minecraft creative-mode flight, which is where the
user's own instinct for the held-Shift control came from in the first
place) - but this needs genuinely new keybinding infrastructure
(`KeyBindingRegistry`'s own model is "one chord, held or pressed," not
a tap-timing gesture at all; the Keybinds menu would need a new way to
represent/edit one) rather than a tweak to any one feature. Deferred as
its own standing follow-up, not bundled into this fix.

## Update: height-matching for flats, UDB's own real second (combinable) criterion

The user asked whether real UDB's connected-select is height-dependent
by default - no: confirmed directly in `BaseVisualMode.EndSelect`,
texture-match (Shift) and height-match (Ctrl) are two fully independent
flags passed straight through to `SelectNeighbours(select, matchtexture,
matchheight, stopatselected)`, each gating its own separate half of a
real AND (`(!matchtexture || textureMatches) && (!matchheight ||
heightMatches)`) - holding *both* modifiers together genuinely requires
a neighbor to satisfy both, not an either/or switch between two modes.
Matched that combinability exactly rather than bolting on a simpler
all-or-nothing toggle.

`ConnectedTextureSelector.FindConnectedSectors` gained `matchTexture`/
`matchHeight` parameters (both independently optional; at least one
required). Height comparison uses UDB's own real epsilon
(`VisualFloor.ArePlanesSame`'s `0.001`, not exact equality) reduced to
just the plain Floor/CeilingHeight scalar - this project models no
sector slopes, so there's no plane normal to also compare the way
UDB's own real check does.

New `select_connected_height_modifier` keybind, bound to **Alt** (not
UDB's real Ctrl, since that's already claimed here by texture-match -
see the keybind decision above). Deliberately not UDB's real meaning
for Alt in this exact gesture either (`stopatselected`, a flood-fill
termination tweak) - that feature isn't ported at all in this project,
so there's no competing meaning on that key to preserve, and reusing it
for something else entirely is safe. Result: Ctrl = texture only, Alt =
height only, Ctrl+Alt = both, matching UDB's real combinability with
different physical keys.

Scoped to flats only, matching what was actually asked - UDB supports
height-matching for walls too (comparing a wall part's own visible
rect height/position rather than a sector plane), but
`ConnectedTextureSelector.FindConnectedWalls` wasn't extended; flagged
as a natural follow-up if wanted later, not silently expanded into.

2 new tests (height-only ignoring a texture difference, both-required
rejecting a neighbor that only matches one) - 1173 total passing.
