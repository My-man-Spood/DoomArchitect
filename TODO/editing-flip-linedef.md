# Flip Linedef (Linedefs mode)

**Status:** Done
**Area:** 2D editing

UDB's `fliplinedefs` action (`ClassicModes/LinedefsMode.cs`) - bound to
`F` by default (`UDBuilder.default.cfg`: `buildermodes_fliplinedefs =
70`, plain `F`, no modifier). The same action name is also bound in
UDB's own `SectorsMode.cs`, where it does something different and
bulkier (flips every wrong-facing line around whichever sector is
selected) - out of scope here, not ported; this is Linedefs mode's own
per-selection version only.

Reverses a linedef's direction: `Start`/`End` swapped together with
`Front`/`Back` in the same step (`Linedef.FlipVertices()` +
`Linedef.FlipSidedefs()` in UDB's own `Core/Map/Linedef.cs`), so which
sector each side still faces doesn't change - only which end is
"first", and which physical sidedef object now sits in the `Front`
slot vs. `Back`. A pure one-sided linedef (`Front` only, no `Back`) is
left untouched, matching UDB's own filter in `FlipLinedefs()` exactly
(`if(l.Back != null || l.Front == null) filtered.Add(l);`) - by
convention a one-sided wall's `Front` already faces the right way, and
there's nothing on the other side to swap into.

This project already had the exact same swap mechanics, just scoped
far narrower: `GeometryStitcher.FlipBackwardLinedefs` only ever flips a
linedef left with *only* a `Back` side (no `Front`) - an internal
normalization step used by `DissolveLinedefsCommand`'s sector-repair
path, `DeleteSectorsCommand`, and `DrawLoopCommand`. Rather than widen
that method's own filter (which would silently change behavior at
those three existing, unrelated call sites - they rely on it being
narrow), the actual swap body was extracted into a private
`SwapVerticesAndSides` helper, and a new, separate
`GeometryStitcher.FlipLinedefs` added with UDB's own broader filter,
used only by the new feature.

New `FlipLinedefsCommand : ICommand` (Undo/Redo, one step regardless of
selection size) and `MapOverlay.FlipSelection()` - Linedefs mode only,
falling back to the hovered line when nothing is selected (matching
`DeleteSelection`/`DissolveSelection`'s own fallback). The one-sided
filter is applied in `FlipSelection()` itself, before constructing the
command, rather than left entirely to `GeometryStitcher.FlipLinedefs`'s
own internal check - so a selection made up only of one-sided lines
pushes no empty no-op step onto the Undo stack.

New `flip_linedef` keybind, `F` by default (free in this project - not
claimed by WASD camera movement or anything else), wired the same way
as `delete_item`/`dissolve_item` (`!_in3D` guarded in `MapView.cs`).

5 new tests: `GeometryStitcher.FlipLinedefs` (two-sided flip, one-sided
no-op, Undo round-trip) and `FlipLinedefsCommand` (Do/Undo) - 1165
total passing.
