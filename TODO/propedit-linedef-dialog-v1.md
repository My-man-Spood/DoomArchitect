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
      etc.); a Generalized (Boom) specials tab, same reasoning as
      Sector's own deferred Generalized Effects tab; sidedef
      Custom-fields button; sidedef/sector reassignment or creation;
      Comment/Custom tabs (placeholders, matching Sector's own pattern).

## Update: ACS arg0-as-string, the one deferred gap that turned out to be a real bug

What shipped above as "deliberately out of scope" (ACS arg0-as-string)
was really a live bug waiting to surface: the real UDB config for the
whole ACS_Execute family (80/81/82/83/84/85/226, all confirmed at
their real numbers in this project's own bundled
`Hexen_linedefs.cfg`/`ZDoom_linedefs.cfg`) marks arg0 `str = true` -
that slot can legitimately hold a script *name* (a UDMF string)
instead of a number, and `str`/`titlestr` were parsed into the raw
config tree but never read anywhere in `GameConfigurationLoader.cs`.
A mapper who names rather than numbers a script (common, and exactly
what a UDB-experienced mapper reaches for) got `arg0` read back via
the generic `GetInteger("arg0", 0)`, which silently defaults to `0` on
a failed cast - looking exactly like "the argument is missing," even
though the map ran correctly in GZDoom the whole time (the real stored
string was never touched; only the editor's own read path was blind
to it).

Fixed with the real UDB behavior, not a minimal string-textbox patch:
`ArgumentInfo` gained `Str`/`TitleStr`, and arg0 of a `Str`-capable
special now shows a real dropdown of every script actually declared in
the map's own `SCRIPTS` lump (numbered sorted by index, then named
alphabetically - UDB's own real `ScriptItem.SortByIndex`/`SortByName`
two-group ordering), reusing the exact same numeric/dropdown toggle
this dialog's own argument rows already had for enum-backed arguments
- a third purpose for the identical mechanism rather than a fourth
control. Picking an entry with declared parameters relabels arg1-arg4
too, porting UDB's own real `ScriptItem.GetArgumentsDescriptions`
per-special remap table verbatim.

New `Core.ZDoom.Bcs.BcsScriptCatalog.Build(BcsCompilationUnit)` walks
every real `BcsScriptDeclaration` (numbered and named alike - unlike
`CollectSymbols`, which deliberately skips numbered scripts as "not a
completable name"). `OpenMapMenu.BuildScriptCatalog()` locates the
current map's own `SCRIPTS` lump (`WadFile.FindMarkerIndex`/
`FindScriptsLumpIndex`, the same lookup `CompileScriptsIfPresent`
already uses for save-time compilation) and parses it with the same
resource-path-aware `BcsParser.ParseProgram` call
`ScriptCompilerRunner.Compile`'s own discovery pass already makes, so
`#include`d files contribute their own scripts too - reparsed fresh
every time a dialog opens rather than cached, since parsing one lump's
text is cheap (the same parser already runs live on every keystroke in
an open Script tab).

`ActionArgumentsEditor` (shared verbatim with the Thing dialog -
`ActionInfo`'s own doc comment already says this table isn't Linedef-
specific) threads the catalog through as a new `Setup` parameter.
`ElementSnapshot` gained its own `Arg0String` alongside the existing
`Args: long[]`, since that's the one slot that can legitimately be
either type - read via `UniFields`'s raw dictionary entry directly
(`raw.Value is string`), not the type-coercing `GetInteger`/`GetString`
wrappers, specifically to tell "stored as a string" apart from "stored
as 0."

**A real near-miss caught during implementation, not after**: the
first pass of the write-back logic (`BuildArg0Command`) reused the
same "blank text means no change" rule every other numeric slot
already relies on (`NumericFieldExpression.ResolveInteger`'s own
blank-returns-null semantics) - but that rule is only safe when the
snapshot's own numeric fallback is trustworthy, and for this one slot
it isn't: a named-script original shows the numeric view blank (no
number to pre-fill) with `Args[0]` sitting at the meaningless 0 the
failed cast left behind. Toggling to that view without typing
anything and clicking OK would have silently overwritten a genuine
string reference with `0`. Fixed by treating unresolvable/blank arg0
text as unconditionally "no change," rather than falling through to a
comparison against a snapshot value that can't be trusted for this
slot - caught by manually tracing the read/write paths against this
exact scenario before calling the feature done, not by a test (none
exist for this pure-Godot-UI layer, consistent with every other
dialog in this project).

No automated coverage for `ActionArgumentsEditor`'s own UI wiring
(same established convention as everywhere else in this codebase);
`ArgumentInfo.Str`/`TitleStr` parsing and `BcsScriptCatalog.Build`
(numbered/named/mixed sorting, declared parameters, namespace-nested
scripts) are both covered - 7 new tests, 1180 total passing.

## Update: a "Go to Script" button to jump to the selected script

Follow-up power feature, requested alongside the arg0 dropdown above:
jump to a Script tab for wherever the script currently selected in
arg0's own dropdown is actually declared, scrolled to its own
declaration line.

**First attempt was Ctrl+Click on arg0's own label** (the user's own
first-named idea) instead of a separate button - reporting back from
testing on a real Hyprland/Wayland setup, both the click itself and a
parallel hover-highlight cue came up completely dead: holding Ctrl
never did anything, clicking always just ran the plain toggle. Two
different reads of the modifier were tried (polling
`Input.IsKeyPressed(Key.Ctrl)` first, then reading `CtrlPressed`
straight off the raw mouse event via `Control.GuiInput` instead, which
is Godot's own recommended approach over polling) - neither worked,
pointing at something more fundamental than either specific API choice
(a plausible real history of Ctrl-modifier-reporting bugs in Godot's
Wayland backend, though not confirmed further - not worth chasing
blind with no way to run the editor directly). Dropped in favor of the
user's own other original idea: a small dedicated "Go to Script"
button next to arg0's label, with no modifier-key dependency
whatsoever - `Arg0NavigateButton` in `ActionArgumentsEditor.tscn`
(arg0's own row only; the other four never need it), shown only
alongside a navigable entry and hidden otherwise.

`ScriptCatalogEntry` gained `SourcePath`/`Line`/`Column` (from the
owning `BcsScriptDeclaration`'s own `SourcePath`/`NumberLine`/
`NumberColumn`) - empty `SourcePath` means "declared directly in the
map's own `SCRIPTS` lump"; a non-empty one is the literal `#include`
text (e.g. `"acs/souls.acs"`), which turned out to be the actual
common case once tested live (most real maps' own scripts live in an
included file, not the main lump directly) - first shipped as
**deliberately not supported** (`BuildScriptCatalog()`'s own include
resolution goes through `ResourceSet.FindIncludeText`, which returns
only decoded text, no tracking of which resource container or entry it
came from), then actually implemented once that gap turned out to be
the real, immediate blocker rather than a rare edge case.

Resolving an include path back to something re-openable as a tab
needed zero new Core API surface, once looked at properly: every
`IResourceContainer` kind already exposes what's needed for its own
case (`WadFile.Lumps`, public, to find a matched bare-lump-name's own
index; `DirectoryResource.ResolveAbsolutePath`, already resolving a
relative include path to its real on-disk path; a PK3 entry's own
`IResourceContainer.FindByPath` already resolves the full nested path,
so the include text doubles as `Pk3EntryPath` directly). New
`OpenMapMenu.FindIncludeOpenRequest(string includePath)` just replays
`ResourceSet.FindIncludeText`'s own priority-ordered container search
one container at a time (instead of going through `ResourceSet` at
all, since pairing each container with its own real resource path -
needed for `ResourceOpenRequest.SourcePath` - meant walking
`_currentResourcePaths` directly rather than the wrapper), keeping the
winning container's identity instead of discarding it for text, and
builds whichever `ResourceOpenRequest` shape that container kind
needs. One accepted minor edge case: an include written as a bare
title rather than a full path, that only resolves via a PK3's own
title-fallback match, opens as a second tab rather than reusing one
already open via the full path - fixing that would need a new
`Pk3File` accessor just to recover its own canonical entry path,
judged not worth it for how rare that phrasing is in practice.

Three window-focus options were considered for what should happen to
the host dialog, itself a genuine separate OS window
(`embed_subwindows=false`) rather than an overlay: force the main
window forward (`DisplayServer.WindowMoveToForeground`), auto-apply-
and-close the dialog, or do nothing about focus at all. Settled on the
third, directly per report: a tiling WM (Hyprland, in this case) with
floating dialogs doesn't have a reliable "send to background and
refocus the other window" concept the way a conventional desktop WM
does, and auto-closing risked discarding in-progress edits. So
navigation is a pure tab-switch-and-scroll in the main window; the
dialog is left open exactly as it was, and the user brings the main
window forward themselves.

Bubbles the same shape `MapOverlay.EditLinedefsRequested` →
`MainMenuBar.OpenLinedefEditDialogFor` already does, one hop longer
since only `AppShell` (not `MainMenuBar`) can open/switch tabs:
`ActionArgumentsEditor.NavigateToScriptRequested` (a `ScriptCatalogEntry`,
raised by the new button's own click handler) → re-raised verbatim by
`LinedefEditDialog`/`ThingEditDialog` → resolved
by `MainMenuBar` (which already holds the active `OpenMapMenu`) into a
concrete `ResourceOpenRequest` - for the empty-`SourcePath` case, the
map's own `SCRIPTS` lump via new `OpenMapMenu.FindScriptsLumpLocation()`
(factored out of `BuildScriptCatalog`, which now calls it too instead
of duplicating the same marker/lump lookup); for the `#include` case,
`FindIncludeOpenRequest` above - → `AppShell.OnNavigateToScriptRequested`,
now covering all three of `OnResourceOpenRequested`'s own find-or-open
branches (loose file / PK3 entry / WAD lump, minus its `MapName` one -
a script's declaration is never a map), each followed by the same
`NavigateTo(line, column)` jump.

New test coverage: `BcsScriptCatalogTests` covers a plain declaration's
own `SourcePath`/`Line`, and that one reached via `#include` carries
the included file's own `SourcePath` - 1 new test, 1181 total passing.
No coverage for `OpenMapMenu.FindIncludeOpenRequest`'s own container-
kind dispatch, or for the `ActionArgumentsEditor`/dialog/`MainMenuBar`/
`AppShell` event-bubbling - the former touches real on-disk/WAD/PK3
state the same way `ScriptCompilerRunner`'s own integration tests do
rather than a plain unit test, the latter is pure Godot UI wiring,
same established
convention as every other dialog in this project.
