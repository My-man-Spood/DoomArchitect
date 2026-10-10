# UDMF text format parser/serializer

**Status:** Done  
**Area:** Map I/O

- [x] `Core.IO`: UDMF text format parser/serializer - a close port of
      UDB's own `UniversalParser`/`UniversalStreamReader`/
      `UniversalStreamWriter` (~2200 lines read across two research
      passes into the actual source, not just its behavior guessed at),
      since a from-scratch reimplementation risked missing real
      edge-case behavior UDB already gets right:
      - `UdmfTreeParser`: the tokenizer, same grammar/number-classification
        (hex, the exact non-general "contains '.' or 'e-'" float
        heuristic, int-escalating-to-long) /string-escape/keyword rules
        as UDB's `InputStructure`, reorganized into named methods around
        a small cursor instead of one ~500-line switch loop. One
        deliberate fix (discussed with the user): UDB's own `\DDD` string
        escape has a real bug (only advances 1 of 3 digits, so the
        trailing 2 leak into the string) - fixed here rather than
        reproduced, since nothing depends on the bug
      - `UdmfReader`/`UdmfWriter`: exact field defaults (sector
        `lightlevel` = 160 not 255, linedef `sidefront`/`sideback` = -1
        sentinel, etc.), exact "log a warning and drop" recovery for
        malformed references (dangling vertex, zero-length linedef,
        out-of-range sidedef index, sidedef-with-invalid-sector) rather
        than aborting the whole load, and UDB's own asymmetric
        field-omission rules on write (sector always writes its five
        core fields even at defaults; sidedef omits offsets-when-zero
        and textures-when-"-"; linedef always writes sidefront/sideback,
        `-1` when absent)
      - Went further than pure UDMF-format porting: `Vertex`/`Sector`/
        `Linedef`/`Sidedef` each gained a `CustomFields` bag (boxed
        `object`, no dependency from `Core.Map` back onto `Core.IO`)
        holding any UDMF field recognized by the format but not yet a
        typed property here (linedef `special`/`arg0..arg4`, sector
        `id`/slopes, sidedef flags, vertex `zceiling`/`zfloor`, and any
        genuinely arbitrary third-party field). Combined with whole-block
        preservation for block types we don't recognize at all (`thing`
        included), a load-then-save round-trip loses almost nothing, even
        though most of it isn't editable yet
      - 96 Core tests total (51 new for this pass) covering the
        tokenizer, the reader's defaults/drop-rules/custom-field capture,
        the writer's formatting/omission rules, and full round-trips

## Update: a real, never-written UDMF field - invisible two-sided walls in GZDoom

Reported by the user as walls completely invisible in GZDoom (floor
visible, no wall at all) on two deliberately minimal test sectors (a
tiny raised pillar inside a room, one with normal front/back sidedef
orientation, one reversed) - confirmed 100% reproducible, and
confirmed *not* caused by this project's recent node-building work:
rebuilding `ZNODES` with `zdbsp`, and even stripping `ZNODES` entirely
(forcing GZDoom to build its own fresh nodes at load time), both
failed identically. That ruled out node-building and pointed back at
the saved `TEXTMAP` itself.

Root cause: `twosided` is a real, independent UDMF linedef field -
confirmed against the base spec (a bool, "line has two sides,"
defaulting `false`) - distinct from `sideback` (which only says *which*
sidedef index is the back one). `UdmfWriter` never wrote it at all,
for any linedef, ever. A first look at Ultimate Doom Builder's own
source for comparison found nothing (a plain `.cs`-source grep for
"twosided" came back empty) - but that was checking the wrong layer:
UDB is heavily config-driven, and `ZDoom_misc.cfg` names the real
field via `doublesidedflag = "twosided";` ("Doublesided" in the UI,
confirmed exactly matching what the user described after fixing it
manually in-editor). UDB's own `Linedef.ApplySidedFlags()` sets this
explicitly - `front != null && back != null` - at every single site
that attaches or detaches a sidedef (sector creation, line splitting,
sector joining, 3D floor creation, and more - a real, scattered,
stored-and-synced piece of state, not something derived once).

Fixed more narrowly than UDB's own approach: rather than adding a
stored, separately-synced flag to this project's own `Linedef` (which
would need the same discipline UDB's own many call sites show, and
has no in-memory consumer that ever needed it - every call site here
already asks "is Back null" directly), `UdmfWriter` now computes
`twosided = true` fresh, directly from `linedef.Back != null`, at the
one and only place that actually needs this as a named field: the
moment of writing it out. Can never drift out of sync, because
there's nothing stored to drift. `UdmfReader` gained a matching
`KnownLinedefFields` entry so a loaded file's own existing value
doesn't linger in the generic custom-fields bag and get written back
out as a stale duplicate alongside the freshly computed one.

Verified end to end against the user's own minimal reproduction WAD:
the freshly-written `TEXTMAP` now has `twosided = true;` on every
real two-sided line and omits it entirely for one-sided ones, with no
duplicate/stale field; rebuilt through the real `zdbsp` pipeline too
(a real `ZNODES` lump, same as an actual Save would produce).

### Update: the "compute it instead of storing it" call was still an unreviewed departure from UDB

Pushed back on directly: "compute it instead of storing it" was a
decision made independently rather than checked against UDB first,
which is exactly backwards for this project's own standing rule (UDB
as the north star, specifically *because* its own implementation
choices are already battle-tested). Fair - fixed by actually applying
that rule here instead of re-justifying the shortcut.

Added `Linedef.TwoSided` as a real, named, public property, computed
fresh from `Front`/`Back` rather than stored - a deliberate departure
from UDB's own stored-and-synced-at-~25-call-sites flag, reasoned as
"safer" since a computed value can never drift out of sync.

### Update: still wrong - this field is user-editable in UDB, so computing it is structurally incorrect, not just stylistically different

Pushed back on a second time, more sharply: computing it was reasoned
through as "safer," but that reasoning missed two things the user
raised directly. First, UDB's stored-and-synced approach has survived
this long for a reason, even when that reason isn't immediately
obvious from the outside. Second, and decisively: this flag is a
real, user-facing checkbox in UDB's own Linedef Edit dialog
("Doublesided," confirmed against `ZDoom_misc.cfg`'s
`linedefflags_udmf` list, sitting right alongside "Impassable"/"Block
monsters"/etc.) - a mapper can set it independently of the real
Front/Back state. A computed property can *never* represent that
override; it isn't a safer reimplementation of the same behavior,
it's a different, narrower behavior that happens to coincide with
UDB's most of the time. The ~25 call sites and the two dedicated
map-error checks (`ResultLineNotDoubleSided`/`ResultLineNotSingleSided`)
aren't incidental complexity UDB could've avoided - the checks exist
*because* the stored flag can legitimately disagree with reality, and
that disagreement is exactly the real, reviewable map issue they're
built to surface to the mapper, not a bug class to engineer away.

`Linedef.TwoSided` is now a real stored property (`{ get; internal
set; }`), kept in sync via a private `MapData.ApplySidedFlags` helper
- the same rule UDB's own `ApplySidedFlags()` applies
(`front != null && back != null`) - called at every site that attaches
or detaches a sidedef: `CreateLinedef`, `SplitLinedef` (the new half
only; the original keeps its already-correct sides), the internal
`JoinChangeSidedef`, and `AttachOrRetargetSidedef`. Far fewer call
sites than UDB's own two dozen, since this project's `Linedef`/
`MapData` model is more consolidated, but the same discipline: a
mapper (or a loaded file) can still end up with a genuine mismatch,
and that's preserved rather than silently normalized.

`UdmfReader` now reads an explicit `twosided` value straight from the
file when present - even when it disagrees with `Front`/`Back` -
overriding the default `CreateLinedef` already computed; when the
field is absent entirely (this project's own backlog of previously-
saved files, from before `UdmfWriter`'s bug was fixed, which never
wrote it), it falls back to the Front/Back-computed default instead
of the bare spec's "absent means false," since the alternative would
mean this project's own prior bug never self-heals on a plain
load-then-save. `UdmfWriter` needed no changes - it already read
`linedef.TwoSided` directly rather than re-deriving the condition
inline.

Five tests added/adjusted for this: `MapDataTests`'s existing
construction/`AttachOrRetargetSidedef` tests still pass as-is (stored-
and-synced produces the same observable result as computed, for every
*normal* mutation path), plus new coverage for `SplitLinedef`
preserving two-sidedness onto the new half, `JoinLinedefs` copying
sidedefs to a previously one-sided `keep` setting it two-sided, and
two new `UdmfReaderTests` covering the override (explicit `true` on a
one-sided line wins) and fallback (`twosided` absent on a real two-
sided line still ends up `true`) cases directly - 1160 total passing.
