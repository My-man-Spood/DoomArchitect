# Compile SCRIPTS into BEHAVIOR on map save

**Status:** Done
**Area:** Scripting / LSP

Until now, `BEHAVIOR` was only ever preserved byte-for-byte on save -
this project never invoked a real ACS/BCS compiler. Requested directly
as the natural next step once "Add Script" (see `resources-browser-panel.md`)
started creating real `SCRIPTS` lumps with nothing that actually
compiled them.

## UDB's real mechanism

Confirmed from source (`Source/Core/Compilers/`,
`MapManager.SaveMap`/`CompileScriptLumps`): on every real save (not
autosave), if the scripts window is open it's implicitly saved first,
then `SCRIPTS` is recompiled into `BEHAVIOR` by shelling out to an
external compiler executable (`Process.Start`, synchronous wait). A
failed compile shows an error but the map/geometry still saves,
keeping whatever `BEHAVIOR` bytes were already there. "No compiler
configured for this lump" is a non-blocking *log* entry in UDB
(`General.ErrorLogger.Add`), not a popup - only an actual failed
*compile attempt* pops a blocking message box. This project mirrors
that same distinction (see below).

## Compiler choice: `zt-bcc`, bundled rather than bring-your-own

`zt-bcc` is already this project's own reference compiler for the BCS
language-server work (`bcs-lsp-foundation.md`, source cloned at
`~/Projects/zt-bcc-source`). Its errors print to **stderr** as
`file:line:col: message` (confirmed directly from `zt-bcc`'s own
`src/task.c:print_diag` and `src/main.c`'s usage text), exit code
nonzero on failure - simpler than ACC's `acs.err`-file convention
UDB's base `AccCompiler` uses. UDB's own `ZtBccCompiler.OnCheckError`
confirms the identical stderr format and a 4-way colon split.

Discussed directly whether to bundle a prebuilt binary (like UDB does
- confirmed by inspecting its own repo: a real Windows `acc.exe` and a
statically-linked Linux `acc` ELF binary, both checked straight into
`Assets/<OS>/Compilers/...`) versus a Test-Engine-style user-supplied
path, and whether reimplementing the compiler in C# was viable:

- `zt-bcc` is MIT-licensed and tiny (~725KB for UDB's own Windows
  build) - cheap to commit directly, no Git LFS needed.
- A full C# reimplementation was rejected: `zt-bcc`'s own source is
  ~11.5k/10.6k/9.5k lines across parse/semantic/codegen. This
  project's existing BCS tokenizer/parser already covers the shape of
  the first number, but semantic analysis and real ACS bytecode/object-
  file generation (~20k lines combined) are completely unbuilt and are
  the highest-stakes part to get wrong - a bad opcode doesn't show up
  as a diagnostic, it silently misbehaves at runtime in GZDoom.
- Settled on: bundle a committed binary per OS
  (`Compilers/zt-bcc/<platform>/`), resolved via `BundledScriptCompiler`
  (Godot's own `OS.GetName()`), with an advanced override in
  Preferences > General for anyone who wants a different build.

Built the Linux binary during this same session
(`~/Projects/zt-bcc-source`'s own `cmake`/`make`, dynamically linked -
confirmed working end-to-end, including a direct manual invocation
outside the editor that produced a real `ACS\0`-magic object file).
**Windows/macOS builds are a real, open follow-up** - this sandbox
can't cross-compile them, so only `Compilers/zt-bcc/linux/zt-bcc`
exists today; the Preferences override is the escape hatch until those
exist (or until someone builds and drops one in).

**Surviving a real Godot export is a separate, unverified concern.**
This project currently has only one working export preset
(`export_presets.cfg`: Linux, `embed_pck=false`) and no confirmed
multi-platform export pipeline. Resolving the bundled binary via
`res://`/`ProjectSettings.GlobalizePath` works correctly in the
editor/dev-run case (the only case verified so far - `res://` is real
files on disk pre-export), but whether it survives being packed into
an actual exported build as a directly `Process.Start`-able loose file
(the same convention GDExtension native libraries already rely on - an
export-preset include filter keeping it out of the `.pck`) has not
been tested. Revisit once a real export is attempted.

## What was built

- **Core** (`src/DoomArchitect.Core/Compilers/`, new, mirrors UDB's
  own `Compilers/` naming): `ScriptCompileError` (1-based `Line`,
  matching `BcsDiagnostic.Line`'s own convention), `ZtBccErrorParser`
  (the stderr parse, with the same "nothing matched, treat the whole
  stream as one error" fallback UDB's own `AccCompiler` comment
  describes), `ZtBccArguments` (pure arg-list builder, confirmed order
  from `zt-bcc`'s usage text and UDB's own bundled `zt-bcc.cfg`).
- **`WadFile.cs`**: `FindScriptsLumpIndex`, `WithSetBehaviorLump`
  (replace-in-place or insert, same positioning `WithAddedScriptsLump`
  already uses for `SCRIPTS`), and a shared private
  `FindMapGroupBounds` helper factored out of all three methods.
- **`BundledScriptCompiler`** (Godot layer): per-OS binary resolution.
- **`ScriptCompilerRunner`** (Godot layer): this project's first
  synchronous, output-capturing process launch (`TestMapLauncher`'s
  own `OS.CreateProcess` is fire-and-forget by design, wrong shape
  here) - plain `System.Diagnostics.Process`, exactly what UDB's own
  compiler classes use. Writes the script source + resolves include
  dirs (the compiling WAD's own directory, plus any configured
  resource that's a real on-disk folder) into a reused temp directory,
  invokes the resolved executable, reads back the object file or
  parses stderr.
- **`OpenMapMenu.WriteMapToFile`**: split into `BuildLumpsForSave` +
  a new `CompileScriptsIfPresent` step + `WadWriter.Write`, so a
  compile can slot in between building the lump list and serializing
  it. No `SCRIPTS` lump in the map's group -> completely unchanged,
  zero behavior change for the overwhelmingly common case. Two new
  events, `ScriptsLumpSaving`/`ScriptsCompiled`, let `AppShell` (which
  owns the tab list - `OpenMapMenu` itself has no visibility into
  other tabs) flush an open, dirty script tab before compiling
  (mirrors UDB's own implicit-save-before-compiling) and tint the
  matching tab's lines on failure.
- **`ScriptDocument.ShowCompileErrors`**: reuses the exact
  `SetLineBackgroundColor`/`_diagnosticLines` mechanism the live
  BCS-parser diagnostics already use - cleared the same way, by the
  next edit's own refresh.
- **Preferences > General**: one new row, "Script Compiler (advanced -
  leave blank to use the bundled zt-bcc)", mirroring
  `TestEnginesEditor`'s own path+Browse+FileDialog composition.

## Update: `#include "zcommon.acs"` now resolves against loaded resources

Traced UDB's real mechanism to fix this properly rather than guess:
`DataManager.GetTextResourceData` (`Source/Core/Data/DataManager.cs:3132`)
is a generic "search every loaded resource container, by name" lookup
- used for whatever a mapper's own resources happen to contain
(**not** `gzdoom.pk3` specifically - see the correction further down,
a wrong claim of mine that lived here on the first pass). The real
external compiler binary never does this search itself - UDB's own
`AccCompiler.Run()` runs its own lightweight ACS preprocessor
(`AcsParserSE`) purely to *discover* `#include`/`#import` names,
resolves each via `GetTextResourceData`, and physically copies the
resolved text into the compiler's temp directory before invoking the
real external binary, which then finds them exactly like normal
sibling files via its own `-i` search.

This project's own `BcsParser` (built for the LSP, not a compiler, but
the same shape of tool, already tracking `BcsProgram.IncludedPaths`)
plays the same discovery role now:

- **`ResourceSet.FindIncludeText`** (Core, `IO/ResourceSet.cs`): the
  real equivalent of `GetTextResourceData` for one include - strips to
  a bare title and reuses the already-existing `FindLump`, which
  already searches WAD/PK3/folder containers alike.
- **A real pre-existing bug, found while tracing this**: for a
  lump-backed script (the normal "Add Script" case), `BcsPreprocessor`
  gave every relative include a `null` base directory, which
  `ResolveIncludePath` turned into an outright failure - *"cannot
  resolve relative path - save this file first"* - without ever
  calling `readFile` at all. No real UDB/zt-bcc counterpart exists for
  this (the real compiler always runs from a real file on disk; this
  "no base directory" case is unique to this project's in-memory-lump-
  editing feature), so it's this project's own original design
  decision, not a port-fidelity question. Fixed: a bare name with no
  base directory now passes straight through to `readFile` instead of
  failing early, letting a resource-set search (or, for an
  unresolvable one, the ordinary "included file not found" path) take
  over.
- **`ScriptDocument.SetIncludeResourcePaths`**: wires the live BCS
  diagnostics' own `readFile` to search the active map's resources
  too, after a real on-disk sibling file - so go-to-definition/hover
  now also see symbols from an included `zcommon.acs`, fixing both of
  the user's asks (compiling and editor-side resolution) from the same
  underlying piece. `AppShell` supplies the currently active map tab's
  own resource paths whenever a script tab opens.
- **`ScriptCompilerRunner.ExtractResourceIncludes`**: before invoking
  `zt-bcc`, runs the same discovery pass and physically writes any
  resource-resolved (not already a real file) include into the compile
  temp directory under its own referenced name - mirroring
  `AccCompiler.Run()`'s own copy-into-tempdir step exactly. Verified
  directly (outside the editor): a script `#include`-ing a hand-written
  `zcommon.acs` dropped into the compiler's own working directory
  compiles successfully and produces a real `ACS\0` object file; the
  same script fails with "failed to load file" without it - confirming
  the extraction step is what actually makes the difference, not just
  the include dirs already being there.

## Update: two more real bugs found testing the above live

**An ordering bug** - `AppShell`'s three script-tab-opening methods
called `SetIncludeResourcePaths` *after* `LoadFile`/`LoadLump`/
`LoadPk3Entry`, but those methods trigger the first diagnostics parse
synchronously as part of loading (`InitializeBcsLanguageSupport`'s own
`RefreshBcsHighlightingAndDiagnostics()` call). So the very first parse
of any newly-opened script always ran with `_includeResources` still
null, producing a stale "included file not found" that nothing ever
re-triggered a refresh to clear (only the next actual edit does, via
`TextChanged`). Fixed by swapping the order - resources are set before
loading in all three places.

**A wrong claim of mine, corrected** - the "UDB's real mechanism"
section above claimed `zcommon.acs` comes from `gzdoom.pk3`'s own
bundled copy, inferred from seeing it declared a `requiredarchive`
without actually checking. Checked a real `gzdoom.pk3` directly after
the user still hit the same error post-fix: **it has no such entries
at all**. These compatibility headers (`zcommon.acs`/`zcommon.bcs`/
`zdefs.acs`/`zspecial.acs`, confirmed from `zcommon.bcs`'s own header
comment: "based on the declarations found in zdefs.acs and
zspecial.acs, both shipped with the acc compiler") are historically
distributed *with the ACS compiler itself*, not the game engine - a
real UDB user only gets `#include "zcommon.acs"` to resolve if they
themselves have added a copy to their own resources. There's no "it
just works" magic via any required archive at all.

Given that, **this project's own `zt-bcc` checkout ships the real,
authoritative copy of these same files** at its own `lib/` folder
(`~/Projects/zt-bcc-source/lib/` - `zcommon.acs` is a 5-line shim that
`#import`s the real 1886-line `zcommon.bcs`). Copied verbatim (MIT,
same repo as the bundled binary, ~88KB total) into
`Compilers/zt-bcc/lib/` - resolved by a new
`BundledScriptCompiler.ResolveLibDirectory()`, added to
`ScriptCompilerRunner`'s own `-i` list (last, so a mapper's own
same-named resource still wins a collision) and to `ScriptDocument.ReadBcsFile`'s
fallback chain (disk, then resources, then this). Verified directly
with the real binary: the earlier "Add Script" boilerplate script now
compiles to a real object file using *only* `-i Compilers/zt-bcc/lib`,
no map resources needed at all.

## Update: resolving zcommon.acs made opening *any* script hang

Reported live right after the above: opening a script tab no longer
errored, it just hung (no exception, no crash dialog - had to be
killed manually). Reproduced directly, outside Godot, against the
user's own real configured resources (`DOOM2.WAD` + `gzdoom.pk3` + the
`ProjectReaper` mod folder + a 79MB `OTEX_1.1.pk3`): the parse itself
finished in under 100ms - not an infinite loop or a stack overflow -
but produced **1102 diagnostics**. This project's own BCS
parser/preprocessor doesn't yet fully understand real-world
`zcommon.bcs` syntax (`#library`, `strict namespace`, and whatever else
a real 1886-line external library actually uses that was never
exercised by this project's own test fixtures) - resolving the file at
all (the whole point of the fix above) is what let the parser actually
try to ingest all of it for the first time, surfacing this.

That alone wouldn't necessarily hang anything, but `ScriptDocument.RefreshBcsHighlightingAndDiagnostics`
had a real, independent, pre-existing bug: it painted *every*
diagnostic the parse produced onto the open tab's own lines via
`CodeEdit.SetLineBackgroundColor`, with zero filtering by
`BcsDiagnostic.SourcePath` - including every diagnostic raised while
reading an `#include`d file, clamped onto whatever few lines the open
tab happens to have. 1102 individual Godot `CodeEdit` calls for a
4-line boilerplate script is almost certainly what actually hung the
UI thread. Fixed by filtering to `SourcePath.Length == 0` (`BcsDiagnostic`'s
own "the main file" convention) before looping - cuts the count from
1102 down to 2 for this exact script, confirmed by rerunning the same
reproduction with the filter applied.

**Not yet fully resolved**: those remaining 2 diagnostics are
themselves evidence of a deeper, separate issue - they report on lines
5-6 of a 4-line script, meaning the broken parse of `zcommon.bcs`
(unbalanced `#if`/`#endif` tracking, most likely) leaks stale state
back into the *including* file once the preprocessor pops back to it.

## Update: the diagnostic-flood fix above did NOT actually fix the hang

My own claim just above - "today's fix guarantees opening a script
never hangs" - was wrong, and said with more confidence than the
evidence supported: the diagnostic flood (1102 `CodeEdit` calls) was a
real, independent bug worth fixing on its own, but it was never
actually confirmed as *the* hang's cause, just assumed to be. The user
hit the exact same hang again opening the real `MAP01` `SCRIPTS` lump
after that fix shipped.

Traced properly this time, with hard evidence at every step rather
than inference: reproduced directly against the user's real resources
and the real `MAP01.wad` (`/home/spood/Doom/CustomMaps/ProjectReaper/maps/MAP01.wad`),
bisected the real `SCRIPTS` content down to a 2-line minimal repro
(`#include "zcommon.acs"` followed by a `script` whose body contains
an `if(1) { }`), then instrumented the actual parser temporarily
(`Console.Error.WriteLine` inside the suspect loops, reverted after)
to watch exactly where it stopped making progress - confirmed within
seconds: `BcsParser.Parse()`'s own top-level loop, spinning forever on
the exact same `CloseCurly '}'` token (2+ million iterations in 3
seconds, never advancing).

**Real root cause**: this project's parser doesn't model
`strict namespace NAME { ... }` (a real, core BCS feature - confirmed
`zt-bcc` itself lists "Namespaces" as a basic feature) as its own
block construct - `strict` is only handled as a bare qualifier
("tolerated and skipped" - `ParseTopLevelMember`'s own comment),
skipping to the next semicolon rather than the namespace body's own
matching `}`. Every declaration *inside* the real `zcommon.bcs`'s own
namespace block then gets mis-parsed as if it were back at the file's
top level - until the namespace's own closing `}` is reached, which
nothing is expecting, and nothing at the top level exists to consume.

`BcsParser.Recover()` intentionally leaves a depth-0 `}` unconsumed -
its own doc comment says so explicitly - "left for an enclosing
`SkipBracedBlock` to see". That's correct and necessary *inside* a
real block. But `ParseTopLevelMember`'s own "unexpected token" fallback
calls the exact same `Recover()` with no enclosing block to hand
anything to - so `_current` never moved, and `Parse()`'s own
`while (_current.Type != EndOfInput)` loop called `ParseTopLevelMember()`
on the identical token forever.

**The actual fix**: `Parse()`'s own loop now guarantees forward
progress itself, regardless of what any nested call does - if
`_current` is the exact same token object before and after
`ParseTopLevelMember()`, it force-advances past it. Added the same
guard to `SkipBracedBlock`'s own "expected ';'" recovery path (the one
other call site with the identical "report, don't advance, loop"
shape), proactively, even though it wasn't confirmed as a second active
bug. New regression test,
`BcsParserTests.Parse_StrayClosingBraceAtTopLevel_TerminatesInsteadOfLoopingForever`
(a minimal, `#include`-free repro - a bare stray `}` at the top level,
not needing the real `zcommon.bcs` chain at all) - deliberately
written to *fail by timing out*, not by a wrong assertion, if this
ever regresses.

Re-verified against the exact original reproduction (the real
`MAP01.wad`'s own `SCRIPTS` lump, the user's real resources): parse now
finishes in 32ms instead of hanging. The underlying "doesn't understand
real `strict namespace` content" gap was real too, and - per direct
pushback, rightly - got built for real instead of staying a permanent
workaround: see `bcs-lsp-foundation.md`'s own "Real
`[private|internal] [strict] namespace [name] { ... }` support"
section. That single change took the real `MAP01.wad` `SCRIPTS` lump
from 1107 diagnostics down to 1 (a genuine, unrelated,
subfolder-resolution issue - see the update right below), confirming
the "unreliable, nonsensical errors" the user was seeing were this
same cascade, not a second, separate bug. The forward-progress safety
net from this update stays regardless - real, independent robustness,
not made redundant by actually modeling namespaces.

## Update: that one remaining diagnostic was a bug in `FindIncludeText` itself, not a missing feature

`#include "acs/souls.acs"` (a real file, genuinely present in
`ProjectReaper`'s own `acs/` subfolder) still reported "not found".
Traced it to `ResourceSet.FindIncludeText`'s own implementation,
introduced alongside the original `#include`-resolution feature: it
stripped the path down to a bare title (`"souls"`) and called
`FindLump`, which only ever checks a container's own root-level
entries plus a fixed set of texture namespaces - it never had any way
to see a real subfolder at all, even though `DirectoryResource`
already indexes every file recursively, by its full relative path,
from construction. The right primitive already existed in this
codebase - `IResourceContainer.FindByPath`, which every container
(`DirectoryResource`/`Pk3File`/`WadFile`) already implements as "try
the exact relative path first, fall back to a bare-title match" - this
method just wasn't using it. Fixed by rewriting `FindIncludeText` to
try each container's own `FindByPath` in priority order instead of
going straight to `FindLump`; every existing test still passes
unchanged (each container's own `FindByPath` already degrades to the
exact same bare-title behavior when there's no real path match), plus
a new one pinning the actual subfolder case. Re-verified against the
real `MAP01.wad` `SCRIPTS` lump: **0 main-file diagnostics** now,
down from the 1107 this whole investigation started at.

## Explicitly deferred

- `zcommon.bcs` *on its own* (not just as an include) still produces
  real diagnostics - now contained to ~80, well past its namespace
  body, around its own lines 1848-1881 - a `special`-declaration-list
  shape its real multi-hundred-line, comma-separated numbered-entry
  form doesn't fully match what this parser's existing `ParseSpecial`
  expects. Confirmed by inspection, not yet root-caused or fixed - a
  separate, smaller, already-contained gap (it no longer leaks into
  whatever script actually `#include`s the file, which is what made
  this look worse than it is).
- Test Map integration - UDB recompiles before testing too (same
  `SaveMap` call, a different `SavePurpose`); this project's
  `TestMapLauncher` uses an entirely separate `BuildCurrentMapBytes`
  path that doesn't go through this hook.
- `#library`/multi-lump linking - "Add Library" is still a stub.
- Per-game-configuration compiler profiles (`TestEngine`-style list) -
  a single global path instead, since mappers don't typically swap ACS
  compilers per game config the way they swap source ports.
- Warnings on an otherwise-successful compile - UDB's own `zt-bcc`
  integration only reads stderr when the exit code is nonzero, mirrored
  exactly rather than inventing warning support UDB's own zt-bcc path
  doesn't have either.
- Windows/macOS bundled binaries (see above).
- Verifying the bundled binary survives a real Godot export (see
  above).
