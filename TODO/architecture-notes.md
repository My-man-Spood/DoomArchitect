# Architecture notes

**Status:** Reference (not a feature - standing design rationale)  
**Area:** Architecture

- **One parser per format, not a shared "universal" one.** UDB's own
  `UniversalParser` is a genuinely reusable curly-brace/assignment text
  tokenizer, used for UDMF *and* UDB's own game-config files and other
  internal formats - "universal" in a real sense. `Core.IO.UdmfTreeParser`
  is structurally similar under the hood (it doesn't know what a
  "vertex" or "sector" is either, just blocks and assignments), but it's
  deliberately scoped and named as UDMF-only rather than exposed as a
  shared engine other readers could plug into.
  Why: the next format on the roadmap - the classic binary Doom map
  format (`THINGS`/`LINEDEFS`/`SIDEDEFS`/etc.) - is fixed-size binary
  records, not text at all, so it wouldn't share a single line with
  `UdmfTreeParser` regardless of how generic that was made. With only
  one real consumer of a curly-brace text grammar today, pulling out a
  shared "universal" parser now would be an abstraction built for a
  future format that doesn't exist yet, and might not even look like
  UDMF's grammar if it did. If a second genuinely curly-brace-shaped
  text format shows up later (e.g. a game-config file, matching one of
  UDB's own other uses of `UniversalParser`), that's the point to
  reconsider factoring `UdmfTreeParser`'s already-generic tokenizer out
  into something shared - not before

  **Update, Game configuration system:** the second consumer arrived - a
  real `.cfg`-format parser (`Core.Configuration.CfgParser`) for
  DoomArchitect's own bundled `Doom.cfg`/`Doom2.cfg`, and eventually a
  real UDB `.cfg` file a user might supply. Reconsidering at that point,
  as promised, landed on *not* sharing a tokenizer with `UdmfTreeParser`
  after all - confirmed via reading UDB's own real `Configuration.cs`
  source that UDB itself keeps `UniversalParser` (UDMF) and
  `Configuration` (`.cfg`) as two separate classes despite both being
  curly-brace/assignment grammars, because the two genuinely differ (a
  `.cfg` key can be a bare integer or contain arbitrary characters up to
  the next delimiter, UDMF's cannot; `.cfg` has `include()` and
  case-sensitive keys, UDMF has neither). Following UDB's own precedent
  here rather than guessing.

- **`Scripts/Controls/` - a real library of reusable UI building blocks,
  not another per-feature folder.** Everything under `Scripts/View/` is
  Godot script attached to one specific scene/dialog (global namespace,
  by this project's own established convention, matching how Godot's
  own scene-script attachment expects it); `Scripts/Controls/` is for
  genuinely generic, feature-agnostic UI mechanisms with no scene of
  their own - given a real namespace (`DoomArchitect.Controls`), same
  as the existing precedent for plain non-Node helper classes
  (`DoomArchitect.Rendering`, `Scripts/Rendering/`).
  First (and so far only) resident: `VirtualizedGrid<TItem, TCell>` -
  extracted out of `TextureBrowserDialog`'s own gallery once a real
  performance regression made clear that "one real Godot node per item
  in a Container" doesn't scale, and the fix (a small, recycled pool of
  cells covering only the visible viewport) wasn't going to be a one-off
  need. Deliberately a plain C# class, not itself a `Control`/`Node`:
  Godot's node/property/signal system doesn't support open generic
  types, so making the *generic controller* a plain class that drives an
  existing `ScrollContainer`/`Control` pair (rather than trying to make
  it a real, attachable scene node) is what lets it be generic at all.
  A consumer's own cell type implements the small `IVirtualizedGridCell<TItem>`
  interface; nothing else is required to reuse it for a different
  large-collection-by-thumbnail (or by anything else uniform-sized)
  picker later.
