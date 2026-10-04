# BCS tokenizer + parser + minimal LSP server

**Status:** Done (first slice)  
**Area:** Scripting / LSP

The first concrete step toward real BCS (ACS-family scripting language)
tooling in DoomArchitect: a hand-rolled lexer + parser, built directly
against the real `zt-bcc` compiler's own source
(`github.com/zeta-group/zt-bcc`, MIT, tag `dev0.8.1` - cloned locally at
`/home/spood/Projects/zt-bcc-source` as a reference checkout, the same
role `/home/spood/Projects/ultimate-doom-builder-source` plays for map
editing), plus a minimal standalone Language Server Protocol process
that reports real syntax errors as LSP diagnostics for `.bcs` files.

## What was built

- `src/DoomArchitect.Core/ZDoom/Bcs/BcsTokenizer.cs` - `BcsTokenType`
  (every member traceable to a real `TK_*` entry), `BcsToken`,
  `BcsTokenizer`. Lives in its own `ZDoom/Bcs/` subfolder/namespace
  (`DoomArchitect.Core.ZDoom.Bcs`) rather than flat alongside
  `ZScriptTokenizer`/`DecorateParser`/`MapinfoParser`/`ZDTextParser` -
  everything else in `ZDoom/` shares one purpose (discovering actor
  definitions for this map editor's own Thing browser), which BCS has
  nothing to do with, and this is expected to keep growing (semantic
  model, symbol table) - a deliberate, user-requested deviation from the
  "new ZDoom-family format = flat sibling file" pattern those others
  follow, not an oversight. Same reflection-based named-token lookup
  pattern as `ZScriptTokenizer`, same pull-based `BinaryReader` lexer
  shape, though. A handful of real, confirmed-
  from-source behaviors that diverge from both plain C and this
  project's own `ZScriptTokenizer`: identifiers are case-folded to
  lowercase (BCS is case-insensitive); a bare leading zero is decimal,
  never octal (`"010"` is 10, not 8 - real octal needs an explicit
  `0o`/`0O` prefix); six distinct numeric literal kinds (decimal/octal/
  hex/binary/fixed/radix) each accept a `'` digit separator; an empty
  hex/fixed-fraction/radix literal only warns and defaults to 0, while an
  empty binary/octal/decimal literal is fatal; string literals keep
  escapes verbatim but char literals really interpret them; a `TK_TYPENAME`
  suffix rule (`...T`/`..._T`/lone `"T"`) exists and is checked before
  keyword lookup.
- `src/DoomArchitect.Core/ZDoom/Bcs/BcsDiagnostic.cs` - `BcsDiagnostic`/
  `BcsDiagnosticSeverity` (Error/Warning), shared by the tokenizer and
  parser as one collection rather than two separate channels.
- `src/DoomArchitect.Core/ZDoom/Bcs/BcsAst.cs` + `BcsParser.cs` - a
  deliberately minimal recursive-descent parser, **not** a `ZDTextParser`
  subclass (unlike `DecorateParser`/`ZScriptParser`) - that base class's
  `Stream`/`BinaryReader`/`SourceName` resource-discovery model and
  single `HasError`/`ErrorDescription` field fit this map editor's
  "find actor definitions for the Thing browser" posture, not an LSP's
  need for a *list* of diagnostics so one typo doesn't suppress every
  other one in the file. Does borrow `ZScriptParser.SkipBlock`'s brace-
  counting *technique*, reimplemented directly here since a failure
  needs to append a diagnostic and recover, not halt.
- `src/DoomArchitect.LanguageServer/` (new project) - a minimal stdio
  LSP server on `OmniSharp.Extensions.LanguageServer` 0.19.9 (the
  standard, if no-longer-actively-released, C# LSP implementation -
  confirmed it still restores/builds cleanly against `net10.0`).
  `BcsTextDocumentHandler` re-parses the whole buffer on
  open/change (full-document sync - BCS scripts are small enough that
  this is simpler and cheap enough not to need incremental sync) and
  publishes the parser's diagnostics via `textDocument/publishDiagnostics`.
  Verified with a real framed-JSON-RPC smoke test over the actual
  process's stdio (`initialize` → `didOpen` → `publishDiagnostics`),
  not just unit tests. **Not** referenced from the root
  `DoomArchitect.csproj` - a standalone process, not linked into the
  Godot game.
- `src/DoomArchitect.Core.Tests/ZDoom/Bcs/BcsTokenizerTests.cs` +
  `BcsParserTests.cs` - 48 tests, each quirk-pinning test citing the
  specific real compiler behavior it protects (not generic coverage).
- `BcsToken.Length` (new) - the token's real source column span, set
  generically by `BcsTokenizer.ReadToken` from how far the cursor
  actually moved, not inferred from `Value.Length` by a caller. A real
  gap, not a style choice: `Value` is lowercased (doesn't change length,
  so harmless) but also has numeric-literal prefixes (`"0x"`) and digit
  separators (`'`) stripped out, which *does* change length - the
  highlighter below was the first consumer that needed an accurate span
  and surfaced this.
- `Scripts/View/Bcs/BcsSyntaxHighlighter.cs` + `Scripts/View/ScriptDocument.cs`
  changes (new) - real in-app BCS support, wired in-process (no LSP/
  JSON-RPC needed for the app's own editor - it's all one C# process).
  `ScriptDocument.LoadFile` checks the extension: a `.bcs` *or `.acs`*
  file gets a `BcsSyntaxHighlighter` (a real `SyntaxHighlighter` resource driven
  directly by `BcsTokenizer`'s token stream - not Godot's built-in regex/
  keyword-list `CodeHighlighter`, which would only get BCS's case-
  insensitive keywords/radix literals right by accident) assigned to its
  `CodeEdit`, plus diagnostic lines tinted via `CodeEdit.SetLineBackgroundColor`
  (red for `Error`, yellow for `Warning`) - both re-run from `BcsParser.Parse`
  on every `CodeEdit.TextChanged`, same "small file, cheap to redo from
  scratch, no debouncing" approach `DoomArchitect.LanguageServer` already
  uses. Every other file extension still opens as plain text, unchanged.
  No automated test coverage for either file - this project's test
  boundary is `DoomArchitect.Core` only; Godot-side `Scripts/View/*`
  scripts have never had automated tests, matching existing precedent
  (`MapView.cs`/`ScriptDocument.cs` itself, etc.) - needs the user's own
  visual confirmation in the running app.
- **Diagnostic hover + keyword completion**, both in-app and LSP (new).
  `BcsTokenizer.ReservedWordTexts` (new) exposes the same 53-entry
  reserved-word list as literal text, built in the same reflection pass
  as `ReservedWordTypes` so the two can't drift apart - the one shared
  source both completion providers below read from.
  - **In-app**: `ScriptDocument` now retains the latest `BcsParser.Parse`
    diagnostics as a field (`_diagnostics`, previously a throwaway
    local) and wires `CodeEdit.SetTooltipRequestFunc` to show a
    diagnostic's message on hover, plus `CodeEdit.CodeCompletionRequested`
    to offer the 53 keywords via `AddCodeCompletionOption`. Confirmed
    from Godot's own `text_edit.cpp`: the tooltip callback only fires
    when the mouse is over a recognized "word" and hands back that
    word's *text*, not a position - the callback ignores that argument
    entirely and instead derives the real line via
    `CodeEdit.GetLineColumnAtPos(GetLocalMousePosition())`. Known,
    accepted limitation from that same source: hovering a blank column
    on a diagnostic line (trailing whitespace, a bare `}`) shows nothing,
    since the callback never fires there at all - not worth a custom
    popup/mouse-motion workaround for what this needs today.
  - **LSP**: new `BcsHoverHandler`/`BcsCompletionHandler`, registered in
    `Program.cs` alongside `BcsTextDocumentHandler`. Hover/completion
    requests carry only a URI + position, never the document's text, so
    a new `BcsDocumentStore` (DI singleton, written by
    `BcsTextDocumentHandler`'s open/change/close handling, read by the
    other two) had to be added to track it - real new scope this pass
    needed, not a pre-existing gap. Verified end-to-end with a real
    framed-JSON-RPC exchange against the running process (hover on a
    line with a diagnostic → its message; hover on a clean line →
    `null`; completion → all 53 keywords, LSP kind 14).
  - Completion was keyword-only at first - now superseded, see below.
    Hover is still diagnostic-only - no hover-on-identifier ("what is
    this token"), which would need real symbol *resolution* (which
    declaration a given use refers to), not just the flat symbol
    *collection* completion now has.
- **Symbol-aware identifier completion**, both in-app and LSP (new) -
  real declared names (functions, script/function parameters, globals,
  locals declared inside script/function bodies, enum types and their
  members), not a word-based "any identifier seen anywhere" approach
  (explicitly chosen over the simpler alternative). The parser used to
  treat every script/function body as 100% opaque (`SkipBracedBlock`
  just brace-counted and discarded every token) - this is the first
  pass that looks inside one at all.
  - New `BcsParser.DeclarationScanner` (private): a small pattern-
    matcher fed token-by-token from the existing brace/paren-depth-
    counting loops (`SkipBracedBlock`, `SkipBalancedParens`,
    `ParseVariableDeclaration`'s declarator loop, `ParseFunction`'s
    header loop) - additive to them, not a restructuring. Recognizes an
    identifier immediately after a type keyword (or after a comma
    continuing the same list, guarded by depth-equality so a function
    call's arguments, e.g. `Foo(a, b)`, are never mistaken for a
    declaration) as a declared name - including the real indexed form
    (`global int 0:a, 1:b;`, confirmed from `zt-bcc`'s own
    `src/parse/dec.c`, `read_instance_list`/`read_storage_index`). No
    real expression/statement grammar was added - only this one
    declaration-shaped pattern is recognized; everything else inside a
    body is still skipped exactly as before.
  - `BcsSpecialDeclaration` gets `Names` (plural - one `special`
    statement can declare several, comma-separated, confirmed from
    `dec.c`'s `p_read_special_list`) - deliberately **not** a
    `Parameters` field to match `BcsFunctionDeclaration`: confirmed from
    that same source (`read_special_param`) that special parameters are
    declared by type only and are never named at all.
  - New `BcsSymbol`/`BcsSymbolKind` and `BcsCompilationUnit.CollectSymbols()`
    flatten every declared name in the file into one deduplicated list.
    Deliberately **not** scope-aware - a local from one script can show
    up while editing a different part of the file, the same "flat, not
    scope-aware" fidelity diagnostic hover already has.
  - **In-app**: `ScriptDocument` retains the parsed `BcsCompilationUnit`
    (`_bcsUnit`, alongside the existing `_diagnostics` field) and offers
    `CollectSymbols()` through completion alongside keywords, mapped to
    Godot's real `CodeCompletionKind` (`Function`/`Variable`/`Enum`/
    `Constant` - Godot has no dedicated "enum member" kind, `Constant`
    is the closest honest fit).
  - **LSP**: `BcsCompletionHandler` now takes a `BcsDocumentStore`
    dependency (it didn't need one before) and merges the same
    `CollectSymbols()` list, mapped to LSP's own `CompletionItemKind`
    (which does have an exact `EnumMember`). Verified end-to-end against
    the real running process: a function, its parameters, a body local,
    a script's own body local, an enum type, and its members all showed
    up with the correct kind; a plain *use* of a declared name (e.g.
    `add(alpha, beta)`'s call arguments) did not get offered twice or
    confused with a declaration.

## Real findings worth remembering (verified from zt-bcc's source, not general ACS knowledge)

- Only 53 of the real `enum tk`'s ~90 keyword-shaped entries are actual
  lexer-level reserved words (confirmed via `src/parse/token/user.c`'s
  exact binary-searched table). The rest - `open`/`respawn`/`death`/
  script-flag words, `print`/`log`/`event`/`kill`, `library`/`wadauthor`/
  `encryptstrings`, `#`-directive names, `import` - are **never**
  tokenizer-level tokens in the real compiler; they stay plain
  identifiers, matched by literal text only at specific grammar
  positions. `BcsTokenType` models both tiers for 1:1 traceability, but
  `BcsTokenizer` only ever produces the first tier (plus `Identifier`).
- `TK_NL` (newline) is a real, significant token the real grammar is
  newline-sensitive around in places - never silently discarded.
- `#` only becomes a directive-introducer at the real compiler's own
  line-beginning check; this pass treats any top-level `#` as one
  unconditionally, a documented simplification.
- **BCS is an extension of ACS, not a separate language** - confirmed
  from `zt-bcc`'s own README ("BCS is an extension of ACS. BCS is mostly
  compatible with ACS") and its wiki's dedicated `IncompatibilitiesWithAcs`
  page, which names exactly two: `&&`/`||` now short-circuit (a behavior
  difference, invisible to a parser - not a syntax error either way),
  and a handful of previously-legal identifiers are now reserved words
  (so an old ACS script using one of those as a variable/function name
  would fail to parse as BCS). This is why `ScriptDocument` applies the
  same `BcsTokenizer`/`BcsSyntaxHighlighter`/`BcsParser` treatment to
  `.acs` files too, not just `.bcs` - it's genuinely the right grammar
  for both in all but that narrow edge case, not a shortcut.

## Completion refinements: original casing, `#define` names (new)

- **`BcsToken.RawValue`** (new) - the real source spelling of an
  `Identifier`/`TypeName` token, before case-folding. `Value` stays
  lowercased everywhere (grammar/lookup still needs that), but every
  symbol-name extraction point in `BcsParser` (function/special/enum
  names, script names, declarator names, body locals, macro names) now
  uses `RawValue` instead - a real, confirmed bug before this: no matter
  what case a user actually declared `MyVar` in, completion always
  offered `myvar` back, since every name ultimately came from the
  already-case-folded `Value`.
- **Godot's own completion filter is unconditionally case-insensitive**
  at the engine level - confirmed directly from `code_edit.cpp`
  (`_filter_code_completion_candidates_impl`, which lowercases both the
  typed prefix and every candidate's display text before fuzzy-
  matching, with no opt-out short of overriding the whole
  `_filter_code_completion_candidates` virtual). This is not a bug to
  work around - typing `ADD` matching a declared `Add` is actually
  *correct* for BCS, which is itself a case-insensitive language
  (confirmed from `zt-bcc`'s own `user.c`) - so filtering case-
  insensitively is the right behavior, not a shortcoming. Only the
  *displayed/inserted* casing was actually wrong, and that's what
  `RawValue` fixes.
- **`#define NAME ...`** now contributes `NAME` (original casing) as a
  `BcsSymbolKind.Macro` completion candidate via a new
  `BcsDefineDirective` AST node - still genuinely not macro-expansion
  support (the value/parameter list is still skipped, unmodeled, exactly
  as before); this only recognizes that a name was declared so it can
  be offered.

## Preprocessor-directive highlighting + a latent tab-focus regression (new)

- **`#directive` highlighting**: `BcsSyntaxHighlighter` now colors any
  `#` plus the identifier immediately following it (tracked via a new
  `previousSignificant` field threaded through `Rebuild`'s token loop) -
  deliberately generalized to *any* directive name rather than a
  hardcoded list, since a directive's name is just a plain `Identifier`
  at the tokenizer level (none of `define`/`include`/`region`/etc. are
  real reserved words - see `BcsParser`'s own remarks), so there's no
  dedicated token type to switch on.
- **Tab no longer toggled 2D/3D in the map view** - a latent bug from the
  earlier tabs feature (not caused by anything in this file), only
  surfacing once the user was actively clicking between tabs during BCS
  testing. Root cause: Godot's `TabBar` defaults `focus_mode` to
  `FOCUS_MODE_ALL` (confirmed via Godot's own docs), overriding
  `Control`'s own default of `FOCUS_NONE` - clicking a tab left keyboard
  focus on the `TabBar`, so a later `Tab` keypress was consumed by
  Godot's built-in focus-navigation instead of reaching `MapView`'s own
  `_UnhandledInput`-based toggle. Fixed with `focus_mode = 0` on the
  `TabBar` node in `Scenes/Main.tscn`.

## Scope-aware completion + go-to-definition (new)

Both items the previous "Deferred" section flagged as needing the same
underlying work - done together, since both need to answer "given a
position, which declaration does this refer to, and where exactly is
it."

- **`BcsSymbol` now carries its own declaration position** (`Line`/
  `Column`, not just `Name`/`Kind`). `BcsParser.DeclarationScanner`
  builds fully-formed `BcsSymbol`s directly (position was already
  available in every `BcsToken` it was handed - it just wasn't being
  kept). Every field that used to be `List<string>` of declared names
  (`BcsFunctionDeclaration.ParameterNames`, `BcsScriptDeclaration.ParameterNames`,
  `BcsVariableDeclaration.DeclaratorNames`, `BcsEnumDeclaration.MemberNames`,
  `BcsSpecialDeclaration.Names`) is now `List<BcsSymbol>` for the same
  reason. A function/script's own name gets `NameLine`/`NameColumn` (or
  `NumberLine`/`NumberColumn`) alongside it. Consequence: `BcsSymbol`
  equality now includes position, so two distinct declarations that
  happen to share a name (the same local redeclared across two different
  scripts) no longer collapse for free in a `HashSet` the way they used
  to - completion providers dedupe explicitly by `(Name, Kind)` instead.
- **Body span tracking** - `BcsScriptDeclaration` already had `BodyLine`/
  `BodyColumn` for the opening `{`; it now also has `BodyEndLine`/
  `BodyEndColumn` for the matching closing `}`. `BcsFunctionDeclaration`
  gets all four for the first time (it had none at all before).
  `SkipBracedBlock` captures both ends directly rather than discarding
  the closing brace's position the way it used to.
- **`BcsCompilationUnit.CollectSymbolsVisibleAt(int line)`** - the
  scope-aware replacement for completion: always includes file-scope
  names (functions/scripts/special names, enum types+members, globals,
  macros), plus a function/script's own parameters+locals only when
  `line` falls inside that specific body's span. `CollectSymbols()` (the
  old flat method) is unchanged and still used by a few tests.
- **`BcsCompilationUnit.FindDeclaration(string name, int line)`** - the
  go-to-definition resolver. Case-insensitive (BCS itself is
  case-insensitive). Checks the enclosing body's own locals/parameters
  first (correct shadowing - a local wins over a same-named global from
  inside that local's own scope), falls back to file scope otherwise.
- **In-app**: wired through Godot's own built-in Ctrl/Cmd+Click
  mechanism rather than any custom hit-testing - confirmed from
  `scene/gui/code_edit.cpp` directly: `symbol_validate(symbol)` fires on
  Ctrl/Cmd-held mouse motion over a word (no position - same
  mouse-position derivation the existing tooltip callback already
  needs); the app answers via `SetSymbolLookupWordAsValid`;
  `symbol_lookup(symbol, line, column)` then fires only on an actual
  Ctrl/Cmd+click on a word already marked valid, handing over the
  click's own 0-based position directly. `OnBcsCodeCompletionRequested`
  now reads the caret's current line and calls `CollectSymbolsVisibleAt`
  instead of the old flat `CollectSymbols()`.
- **LSP**: new `BcsDefinitionHandler` (`textDocument/definition`),
  registered in `Program.cs`. Unlike Godot's `CodeEdit`, LSP only hands
  over a cursor position, never the word under it - a small `WordAt`
  helper (scan both directions over word characters from the requested
  column) finds it first. `BcsCompletionHandler` updated the same way as
  the in-app side, using `request.Position.Line`.
- Verified end-to-end against the real running LSP process (same
  framed-JSON-RPC smoke-test approach as every other feature here): two
  scripts each declaring a same-named local - `textDocument/definition`
  on a use inside each resolves to *that script's own* local, not the
  other's; a shared global resolves correctly from inside either;
  `textDocument/completion` triggered inside one script's body excludes
  the other script's locals while still offering the shared global.
  In-app Ctrl+Click navigation and scope-filtered completion need the
  user's own manual confirmation in the running app (no automated
  coverage exists for `Scripts/View/*`, same existing precedent).

## Hover-on-identifier (new)

A diagnostic message on the hovered line still always wins (unchanged);
otherwise hovering a declared name now shows what it is, e.g.
`function Add`, `variable sum`, `enum member Red`, `macro MAX_HEALTH` -
driven entirely by the `FindDeclaration` resolver already built for
go-to-definition.

- New `BcsSymbol.Describe()` (in `BcsAst.cs`) - the one place this wording
  lives, shared by both consumers so hovering a name describes it
  identically in-app and over LSP.
- **In-app**: `ScriptDocument.GetBcsTooltip` finally uses its own `word`
  parameter (previously ignored - the tooltip callback was diagnostic-
  only before this) to call `FindDeclaration` when there's no diagnostic
  on the line.
- **LSP**: `BcsHoverHandler` gets the same fallback, using a new shared
  `BcsWordScanner.WordAt` (factored out of `BcsDefinitionHandler`, which
  already needed the identical "find the word under this position"
  logic LSP requests always need but Godot's `CodeEdit` never does -
  it hands the word over directly via its own `select_word`).
- Verified end-to-end against the real running LSP process: hovering a
  function's own name, a local, a parameter, a macro name, an enum type
  name, and an enum member all resolved to the right description;
  hovering a position with no diagnostic and no resolvable word still
  returns nothing.

## `#library`/wadauthor-family pragma grammar, confirmed (new)

Previously an open item ("exactly where `#library`/`wadauthor`-family
pragmas attach grammatically wasn't pinned down"), accepted both a
`#`-prefixed and bare-keyword spelling defensively. Confirmed from
`zt-bcc`'s own `src/parse/library.c` (not `src/parse/stmt.c`, the
original guess) - the real finding is cleaner than expected: **every one
of these is always `#`-prefixed**, with no bare-keyword form at module
scope at all (`read_module_item` dispatches purely on whether the
current token is `#`). This uncovered two real bugs, now fixed:

- `#libdefine` was being dispatched to the same parsing as `#library`
  (expects an optional string) - wrong; it actually shares `#define`'s
  exact grammar (both go through the real compiler's own `read_define`).
  A real `#libdefine NAME value` would previously have produced a bogus
  diagnostic. Moved to share `#define`'s case instead - it's now also
  tracked as a completable macro name, same as `#define`.
- `#library` with no name at all was treated as a missing-argument
  error - wrong; a bare `#library` is valid real BCS and just means "use
  the default name." The diagnostic is gone; `BcsLibraryDirective.Name`
  can legitimately be empty now.

Also added proper (tolerant, not-modeled-as-a-node) recognition for
`#linklibrary "name"` (required string argument), `#encryptstrings`,
`#nocompact`, `#wadauthor`, `#nowadauthor` (no arguments at all) - these
previously fell through to "unknown directive," so a real file using any
of them got a bogus diagnostic.

Removed the bare (non-`#`) acceptance of `library`/`wadauthor`/
`nowadauthor`/`nocompact`/`encryptstrings` entirely, since none of them
have a real bare form - a bare occurrence is now correctly flagged as an
unexpected token. `strict` alone is *not* part of this cleanup - it's
genuinely valid bare (a namespace qualifier, confirmed from that same
source's `is_namespace`), not a pragma; namespaces still aren't modeled
by this pass at all, so it's still tolerated and skipped, just no longer
miscategorized in the code as one of the pragma-family words.

## Hover signatures/types (new)

Hovering a declared name now shows its real type (`int x`) or, for a
function/named script, its full signature (`function int Add(int a, int b)`,
`script main(int a, int b)`) - not just a bare kind label as before.

- **`DeclarationScanner`** now tracks each matched trigger token's own
  *text*, not just its type, and threads it through as each `BcsSymbol`'s
  new `Type` field - correctly reusing the list's starting type for a
  comma-continuation (`int a, b, c;` - one shared type, confirmed real
  grammar) while letting rule A re-fire independently per parameter
  (`int a, int b` - BCS requires every parameter to restate its own
  type, confirmed from `dec.c`'s `read_param`), which happens for free
  since each parameter's own type keyword immediately precedes it.
- New `BcsFunctionDeclaration.ReturnType` - confirmed from `dec.c`'s
  `read_object`: a function's return type is always the header's very
  first token, immediately after `function`.
- `BcsSymbol.Signature` (new, only ever set for a `Function`-kind
  symbol) - the full parenthesized signature text, built once in
  `FileScopeSymbols()` while the owning `BcsFunctionDeclaration`/
  `BcsScriptDeclaration` node (and its now-typed parameter list) is
  still at hand; `Describe()` prefers it over the plain kind label.
- Verified end-to-end against the real running LSP process: hovering a
  function's own name shows its full signature, hovering a parameter
  use inside its body shows that parameter's own type, hovering a
  global shows its type.
- Known limitation, not fixed here (pre-existing, not introduced by this
  pass): an array/reference parameter (`int& a`, confirmed real grammar
  via `dec.c`'s `read_param_ref`) isn't picked up at all, since its name
  isn't immediately adjacent to its type keyword the way
  `DeclarationScanner`'s rule A requires - same limitation that already
  existed for plain name collection, now also missing from signatures.
  `special` declarations still show just a bare name - their real
  parameters are type-only, never named (confirmed earlier), so there's
  nothing meaningful to add to their signature anyway.

## Colored hover text (new)

A flat, single-color signature like `function int Add(int a, int b)` was
hard to read - hover is now colored the same way the live editor buffer
already is.

- **`Scripts/View/Bcs/BcsColors.cs`** (new) - the color palette and the
  `ColorFor(type, previousSignificant)` lookup, extracted out of
  `BcsSyntaxHighlighter` so it's one shared source of truth rather than
  two copies that could drift apart.
- **`Scripts/View/Bcs/BcsBbcodeFormatter.cs`** (new) - two deliberately
  separate paths, since they need different handling and can't be told
  apart from the string alone: `ColorizeCode` re-tokenizes a
  `BcsSymbol.Describe()` result (real, well-formed BCS-ish syntax) and
  wraps each colorable token in a `[color=#rrggbb]` tag using
  `BcsColors`; `EscapePlainText` just escapes `[`/`]` for a diagnostic's
  own English message, *without* tokenizing it - confirmed live that
  tokenizing prose is actually unsafe: a diagnostic quoting punctuation
  (`"expected ']', got '{'"`) re-tokenizes its quoted `']'` down to a bare
  `]`, silently dropping the surrounding quotes, since a token's `Value`
  is a literal's *decoded content*, not its original source text.
  `ColorizeCode` also had to use `RawValue` (not `Value`) for an
  Identifier/TypeName token - confirmed live too: naively using `Value`
  silently re-lowercased a declared name's casing right back (`Add` →
  `add`) on every hover, the exact bug `RawValue` already exists to
  prevent elsewhere in this codebase.
- **`Scripts/View/Bcs/BcsCodeEdit.cs`** (new) - a `CodeEdit` subclass
  overriding `_MakeCustomTooltip` (confirmed from Godot's own
  `scene/main/viewport.cpp`: `_gui_show_tooltip_at` calls `get_tooltip`
  for the text, then `make_custom_tooltip(text)` to let a subclass
  supply its own `Control` instead of the default plain-text `Label` -
  Godot wraps whatever is returned in its own themed tooltip panel
  regardless) to show a BBCode-enabled `RichTextLabel` instead. Attached
  to `Scenes/UI/ScriptDocument.tscn`'s `CodeEdit` node in place of a bare
  `CodeEdit`. Deliberately dumb - `ScriptDocument.GetBcsTooltip` is the
  one place that already knows which of the two formatter paths applies
  (a diagnostic vs. a resolved declaration), so it decides and hands over
  already-BBCode-ready text; this control only displays it.
- **LSP**: `BcsHoverHandler`'s declaration-description branch upgraded
  from `MarkupKind.PlainText` to `MarkupKind.Markdown` with a fenced code
  block, so a real editor applies its own syntax highlighting to the
  signature. The diagnostic-message branch is unchanged (still
  `PlainText`) - plain English prose doesn't belong in a code fence.
- Verified end-to-end: a standalone script confirmed `ColorizeCode`'s
  exact BBCode output (correct casing, correct coloring, no quote loss)
  before wiring it in; the real running LSP process confirmed the
  Markdown-fenced hover output. In-app visual rendering (does the
  `RichTextLabel` tooltip actually look right, sized right, positioned
  right) needs the user's own confirmation in the running app - no
  automated coverage exists for `Scripts/View/*`, same existing
  precedent.
- **Follow-up fix, user-reported**: the tooltip first rendered one
  letter per line, stacked tall. Confirmed from Godot's own
  `rich_text_label.cpp` (`get_minimum_size`): with `AutowrapMode` at its
  default (anything other than `Off`) and no explicit max width set,
  `FitContent`'s own computed width is discarded entirely in favor of a
  hardcoded 1px - `FitContent` alone was never enough. Fixed by also
  setting `AutowrapMode = TextServer.AutowrapMode.Off` on the
  `RichTextLabel`, which is what actually lets it size to the text's own
  natural (unwrapped) width.

## Leading doc comments in hover (new)

User-requested: a comment block written directly above a declaration -
either one `/* ... */`, or a run of `//` lines with no blank line between
them - now shows up in that declaration's hover, above its
signature/type. Deliberately no special markup (no `/// <summary>`-style
convention) - just whatever comment text is actually there.

- **`BcsTokenizer.NextSignificantToken`** got a new overload
  (`out List<BcsToken> skippedComments`) alongside the existing one
  (unchanged, still calls the new one and discards the list) - comments
  were previously discarded with no trace while skipping to the next
  real token; now a caller can see exactly what was skipped to reach it.
- **`BcsParser`** tracks `_currentLeadingComments` (whatever the most
  recent `Advance()` call skipped) and has a new `ExtractDocComment`
  helper: walks backward from the comment closest to a declaration,
  keeping a contiguous, gap-free run by comparing real line numbers (a
  multi-line block comment's own last line is computed by counting its
  embedded newlines) - no need to track newline tokens separately at
  all. A blank line anywhere breaks the chain. This is also what
  correctly tells an unrelated *trailing* comment on a previous
  declaration's own last line apart from a genuine leading comment on
  the next one, as long as a blank line separates them (a known,
  accepted ambiguity if it doesn't - the same one every other "leading
  doc comment" convention has).
- Wired into every top-level declaration kind this parser already
  tracks a hover-worthy name for: function, script, special, enum,
  variable, `#define`/`#libdefine`. **Not** wired into body locals,
  parameters, or enum members - a doc comment documents the *statement*
  (and, for `special`/a multi-declarator variable statement, is shared
  across every name that statement declares), not each individual part
  of it; extending this to body-locals would need the same tracking
  threaded into `SkipBracedBlock`'s loop too, not requested here.
- New `BcsNode.DocComment` (the raw text, empty if none) and
  `BcsSymbol.DocComment` (threaded through from the owning node, or
  shared via a `with` expression for `special`/multi-declarator
  variables). Deliberately kept **separate** from `BcsSymbol.Describe()`'s
  own signature text, not folded into it - a doc comment is plain
  English prose, and `Describe()`'s result gets re-tokenized by
  `BcsBbcodeFormatter.ColorizeCode` for in-app coloring / wrapped in a
  Markdown code fence over LSP; mixing prose into that string would
  revive the exact "tokenizing prose can silently lose quote characters"
  bug already fixed once this session. Callers (`ScriptDocument.GetBcsTooltip`,
  `BcsHoverHandler`) combine the two themselves: the doc comment through
  `BcsBbcodeFormatter.EscapePlainText`/plain Markdown prose, the
  signature through its own existing path, joined with a blank line.
- Verified end-to-end against the real running LSP process: a two-line
  leading `//` doc comment on a function showed up correctly, joined,
  above its signature; a comment separated from the next declaration by
  a blank line correctly did not attach.

## Multi-file `#include`/`#import` resolution (new)

Completion/hover/go-to-definition now see across the whole include
graph, not just the one open buffer - a function/global/macro declared
in a file pulled in via `#include`/`#import` shows up everywhere the
same names declared locally already did, and go-to-definition can jump
into a different file when that's where the real declaration lives.

- Confirmed real resolution algorithm directly from `zt-bcc`'s own
  `src/task.c` (`identify_file_relative`), not guessed: a relative path
  resolves against the *including* file's own directory first; an
  absolute path is used as-is. (Not modeled: that function's further
  fallbacks - compiler `-i` include directories and a bundled default
  lib dir - this project has no equivalent configuration surface for
  either; an unresolvable `#include "zcommon.acs"` with no local copy
  simply contributes nothing, same failure a real project missing that
  file would have without `-i` configured.)
- New `BcsParser.ParseProgram(source, sourcePath, readFile)` → new
  `BcsProgram` (main unit + every transitively-included unit, each
  stamped with its own resolved path). Mirrors `DecorateParser`/
  `ZScriptParser`'s own existing `OnInclude` caller-injected-resolver
  pattern rather than reinventing one - `BcsParser` itself stays
  filesystem-agnostic, trivially testable against real temp files
  (`BcsProgramTests.cs`, real disk I/O, no mocking). Resolved paths are
  deduped (case-insensitive, normalized) so a diamond-shaped or
  circular include graph - including one that cycles back to the file
  being edited - is parsed at most once per file, never infinitely;
  this is also what makes a true self-`#import` safe without needing
  the real compiler's own dedicated diagnostic for it.
- New `BcsSymbol.SourcePath` (empty = "this file," every pre-existing
  call site) - only a `BcsProgram` ever sets it, when folding in an
  included file's own *file-scope* symbols only (never its locals/
  parameters - those are positions within its own body, meaningless
  relative to a different file's lines).
- **In-app**: `ScriptDocument` parses via `ParseProgram` now (reading
  included files via `Godot.FileAccess`); a cross-file go-to-definition
  jump can't be handled by the tab itself (no project/sibling-tab
  awareness), so it raises a new `NavigateToFileRequested` event instead
  - `AppShell` (refactored to share one `OpenScriptTab` helper between
  the file-dialog path and this one) focuses that file's tab if it's
  already open, otherwise opens it, then jumps to the position either
  way.
- **LSP**: `BcsDocumentStore.GetProgram(uri)` resolves the document's
  own real filesystem path via `DocumentUri.GetFileSystemPath()` (confirmed
  exact API via reflection against the installed package) and reads
  included files via plain `System.IO.File`, always fresh from real
  disk - never from this store, even if that file happens to also be
  open as its own document, matching the in-app side's same choice (an
  unsaved tab's in-memory edits to an included file are deliberately
  not reflected until saved - the same thing a real compiler would do,
  and consistent on both sides rather than solved two different ways).
  `BcsDefinitionHandler`'s returned `Location` now points at the
  resolved file when the match isn't local - a real editor already
  knows how to open a `Location` in a different file.
- **Deliberately out of scope**: an included file's own syntax errors
  never surface into the including file's diagnostics - `BcsDiagnostic`
  has no file field, and this pass only ever needed *symbols* out of an
  included file, not a second opinion on whether it's well-formed.
- **An unresolvable `#include`/`#import` is now a real diagnostic**
  (user-reported gap, fixed same day) - a `Warning`, at the directive's
  own position, distinguishing a relative path with nowhere to resolve
  against ("save this file first") from a path that resolved but
  couldn't be read ("included file not found"). Deliberately only for a
  directive written directly in the file being edited, never one found
  deep inside an already-included file - same reasoning as the
  "included file's own syntax errors" scope limit just above: that
  line number belongs to a different file, and surfacing it here would
  mislead, not help. `BcsTextDocumentHandler` (the LSP handler that
  actually publishes diagnostics) had to switch from plain
  `BcsParser.Parse` to `BcsDocumentStore.GetProgram` for this to reach
  `textDocument/publishDiagnostics` at all - previously it never
  resolved includes, so this diagnostic class would have existed but
  never actually been sent to a real editor. In-app picks it up for
  free - `ScriptDocument`'s existing diagnostic-line-tinting loop
  already consumes whatever's in `_bcsProgram.Diagnostics` generically.
- Verified end-to-end against the real running LSP process with two
  real temp files (one `#include`-ing the other): completion and hover
  inside the including file surfaced the included file's function
  (doc comment included) and global; `textDocument/definition` on a use
  of that function returned a `Location` whose `uri` pointed at the
  included file, not the requesting one. In-app cross-file tab-opening/
  focusing needs the user's own manual confirmation (no automated
  coverage exists for `Scripts/View/*`, same existing precedent).

## Real expression grammar for declaration initializers (new)

`int x = ;` (and similar) is now a real diagnostic - confirmed live via
the LSP smoke test, not assumed. Applies to both top-level and
body-local variable declaration initializers/array sizes; does **not**
add real statement/control-flow grammar (`if`/`while`/`for`/standalone
assignment or call statements) - a script/function body's
non-declaration statements remain exactly as opaque to this pass as
they always were. Confirmed the entire motivation for this is
diagnostics, nothing else: completion/hover/go-to-definition are built
entirely on `DeclarationScanner`'s declaration-shaped pattern matching
and `BcsWordScanner`'s word-under-cursor matching, neither of which
needed this at all.

- New `BcsParser.Expressions.cs` (a `partial class BcsParser`) - a real
  precedence-climbing grammar confirmed against `zt-bcc`'s own
  `src/parse/expr.c` line by line (the real chain: assignment → ternary
  `?:`, with a confirmed-real optional middle operand (`a ?: b`,
  "Elvis" form) → `||` → `&&` → `|` → `^` → `&` → `==`/`!=` →
  relational → shift → additive → multiplicative → prefix unary →
  postfix (`[]`/`.`/call/`++`/`--`) → primary). Validating-only, no
  expression-AST node types at all - nothing downstream reads an
  expression's structure, only "does it parse," so every method just
  consumes tokens and reports a diagnostic on a real problem.
  Deliberately excluded, documented not hidden (each narrow/rare, each
  one skipped avoids chasing most of `expr.c`'s remaining ~1000 lines
  for very little real payoff): `lengthof`/`strcpy`/`memcpy` builtins;
  `::`-qualified names, `upmost`, `namespace` as primaries; compound/
  func literals; postfix `!` ("sure" operator, distinct from prefix
  logical-not, which is supported); the parenthesized 3-argument
  array-field form of the `a:` format cast.
- **A real ambiguity that had to be handled, not just excluded:** a
  call argument can start `identifier ':'` - confirmed from `expr.c`'s
  own `peek_format_cast`, used by every `print`/`log`/`hudmessage`-style
  call (`print(s:"text", d:value)`). `BcsTokenizer` has no forward
  lookahead at all (confirmed elsewhere in this parser), so this is
  handled as a post-hoc reinterpretation instead of a peek: the tag is
  parsed as an ordinary expression first (harmless for a bare
  identifier), and a leftover `:` right after means "that was actually
  a tag" rather than a real ambiguity to resolve.
- `ParseVariableDeclaration` rewritten (replacing its blind brace/paren-
  depth-counting loop) around a new shared `ParseDeclarator()` - also
  used by a new local-declaration hand-off inside `SkipBracedBlock`,
  gated behind a real `atStatementStart` tracking flag. That gate is
  the fix for a real risk, not a theoretical one: a type-conversion
  *expression* mid-statement (`x = int(y);` - confirmed real grammar
  from `expr.c`'s own `read_conversion`) must never be misdetected as a
  *new* local declaration just because a type keyword appears - it
  only ever arrives with the flag already false (preceded by `=`, never
  a statement boundary). Covered by a dedicated regression test.
  `DeclarationScanner` itself is unchanged and still serves `Parameter`/
  `EnumMember` modes exactly as before - this only replaces its
  heuristic lookback for the one shape (`VariableDeclaration` mode) that
  now gets real structured parsing instead.
- Verified end-to-end against the real running LSP process: a clean
  function body, a clean call-expression initializer, and a malformed
  initializer in the same file - exactly one diagnostic published, at
  the malformed one's own position, nothing else flagged.

## Real macro expansion - Phase 1 (new)

The user explicitly chose a full faithful port over a bounded version,
after seeing the real scope confirmed from `zt-bcc`'s own source:
`src/parse/token/stream.c` (expansion mechanics) + `dirc.c` (directive
reading) + what they depend on in `user.c`/`source.c` total ~4,100
lines - a genuinely separate two-level lexer architecture (raw tokens →
preprocessor tokens → macro-expanded tokens → main tokens), not an
extension of the single-pass tokenizer this project had. Too large for
one pass, so this is staged - **Phase 1** (done): real object-like and
function-like `#define`/`#undef` with parameter substitution and
recursive rescanning, within a single file.

- New `BcsPreprocessor` (Core) - a wrapping layer sitting between the
  unchanged `BcsTokenizer` and `BcsParser`, mirroring the real
  compiler's own confirmed layering. Mirrors `BcsTokenizer`'s own
  pull-based `NextSignificantToken` shape exactly, so `BcsParser`'s 4
  call sites needed minimal change. Intercepts only `#define`/
  `#libdefine`/`#undef` fully - every other directive passes straight
  through to `BcsParser.ParseHashDirective`, untouched.
- Confirmed real, whitespace-sensitive grammar from `dirc.c`: `#define FOO(x)`
  (no space) is function-like; `#define FOO (x)` (a space) is
  object-like with a body that happens to start with a parenthesized
  expression - `BcsTokenizer.NextSignificantToken` always skips
  whitespace, so this one check goes around it via `ReadToken()`
  directly. Variadic (`...`/`__VA_ARGS__`) parameters are supported.
- Expansion is a pure function over token lists
  (`BcsPreprocessor.Expand`/`ExpandTokenList`), deliberately not
  entangled with the live pull loop - each argument is recursively
  pre-expanded before substitution, and the substituted result is
  rescanned for further macro references, both confirmed real
  semantics for the common (non-`#`/`##`) case. A macro's own name
  appearing inside its own expansion (directly or through another
  macro - confirmed via a dedicated mutual-reference test) is never
  re-expanded, confirmed real behavior (`dirc.c`'s own `TK_MACRONAME`
  marking) - critically, this guard only works because tokens already
  fully expanded and enqueued are never re-checked by the live loop;
  re-checking them would silently defeat it.
- The existing completion/hover/go-to-def-for-macro-names feature
  (`BcsDefineDirective`) is preserved unchanged, just re-sourced:
  `BcsParser.Parse` now builds one `BcsDefineDirective` per entry in
  `BcsPreprocessor.Macros` after its main loop finishes, since
  `#define`/`#libdefine` never reach `ParseHashDirective`'s own
  dispatch anymore.
- Verified end-to-end: the entire pre-existing test suite (959 tests,
  the highest-risk change made this session - it rewrites `BcsParser`'s
  fundamental token-pull path) passed with zero regressions; new tests
  confirm real substitution happened (not just "didn't crash" - our
  expression grammar never checks whether a name is declared, so a
  bare unexpanded macro name is otherwise already tolerated on its
  own), self-reference/mutual-reference termination, nested macro
  calls as arguments, `#undef`, and wrong-argument-count warnings.
  Confirmed against the real running LSP process too (a control case
  with no macros defined shows the expected diagnostics; the real
  `#define`d case is clean; hover on a macro's own name still shows its
  doc comment).

**Explicitly deferred to later phases, not silently dropped (at the
time):** `##` token-pasting and `#` stringizing - **done in Phase 3,
below**; `#if`/`#elif` (needs its own constant-expression evaluator,
comparable in scope to the expression-grammar work on its own), still
deferred; `#ifdef`/`#ifndef`/`#else`/`#endif` conditional compilation -
**done in Phase 2, above**; cross-file macro visibility (today, a
`#define` in an `#include`d file is **not** visible to the including
file - true C-preprocessor semantics need `#include` to be a textual
splice happening *during* tokenization, a fundamentally different
model from the post-hoc symbol-merging `BcsProgram` already does for
completion/hover/go-to-def; reconciling the two is real, separate work,
still deferred).

## Real macro expansion - Phase 3: `#` stringizing and `##` token-pasting (new)

The last of the originally-deferred `#define` mechanics, completing
real object-like/function-like macro expansion as a faithful port.

- Confirmed real grammar from `dirc.c`'s own `read_body`/`read_body_item`:
  `##` at the very beginning or end of a macro body, and a `#` not
  immediately followed by one of the macro's OWN parameters inside a
  FUNCTION-like macro, are all real diagnostics reported at *define*
  time, not at a later call site - `BcsPreprocessor.ValidateMacroBody`
  (new) runs right after a macro's body is read. A lone `#` inside an
  OBJECT-like macro's body is deliberately never checked at all -
  confirmed real (`TK_PROCESSEDHASH`): it's just a literal `#` there,
  no stringize meaning, nothing to validate.
- **`BcsTokenType.Placemarker`** (new, genuinely real - `TK_PLACEMARKER`
  in the real `enum tk`) - the real compiler's own sentinel for "this
  parameter's argument was empty, but it's still adjacent to a `##`, so
  don't let the next unrelated body token get mistaken for its other
  operand." `BcsTokenizer` itself never produces one; only
  `BcsPreprocessor.Expand`'s own in-memory bookkeeping ever does. The
  class-level doc comment on `BcsTokenType` (listing what's
  deliberately *not* modeled) is updated to explain why this one
  earned a real member while 7 siblings still don't.
- **`BcsPreprocessor.Expand`** rewritten as two confirmed-real passes
  (`stream.c`'s own `expand_macro`, which also walks its body twice,
  not once, for the same reason): pass 1 substitutes every parameter
  (pre-expanded through `ExpandTokenList` in the common case, but with
  the RAW, unexpanded argument when adjacent to a `##` on either side -
  confirmed real, `expand_id`'s own adjacency check decides this before
  ever considering nested expansion) and resolves every `#`-stringize
  against the raw argument (`Stringize`, new); pass 2
  (`BcsPreprocessor`'s own `##`-resolution loop) mutates a working list
  in place rather than walking forward pairwise - confirmed real
  structure (`concat()` rewrites its left operand into the paste result
  without advancing past it) - which is what correctly folds a CHAIN of
  `##`s (`a ## b ## c`, where the shared middle operand merges with its
  left neighbor first and the result is then immediately re-checked
  against the next `##`, rather than two independent non-overlapping
  pastes being attempted).
- **`Stringize`** (new) - confirmed real (`stream.c`'s own `stringize()`):
  never macro-expands its argument first, unlike normal substitution.
  One real, narrow, documented divergence: this tokenizer never keeps
  whitespace as its own token at all (unlike the real one), so a single
  space is reinserted between two argument tokens only when their real
  source columns had an actual gap - an approximation of, not a perfect
  reproduction of, the real compiler's own literal-whitespace
  preservation.
- **`Paste`** (new) - confirmed real semantics (`stream.c`'s own
  `concat`/`concat_tangible`): joins two tokens' own source text into
  one. Deliberately does NOT hand-port the real compiler's own
  ~150-line hand-built `concat_result` compatibility table - instead
  re-lexes the combined text through a fresh, throwaway `BcsTokenizer`
  and accepts the result only if it reads back as exactly one valid
  token, a pragmatic, equivalent-in-effect substitute. Combined text
  that doesn't is the same "produces an invalid token" real diagnostic,
  just detected differently; this project's own choice to report and
  recover (drop the paste, keep going) rather than abort compilation
  entirely can visibly cascade into a second, honest diagnostic right
  after it (e.g. an initializer left with nothing in it) - not a bug,
  the same "one real problem can cascade" pattern already accepted
  elsewhere in this codebase.
- Verified with new tests in `BcsPreprocessorTests.cs`: stringizing a
  multi-token raw argument into one string literal (decisive shape
  proof, same reasoning as Phase 1's own OPEN/CLOSE tests); pasting two
  identifiers, and an identifier with a digit, into one new identifier;
  a 3-way CHAINED paste (regression coverage for the shared-middle-
  operand case specifically); an empty argument on one side of a `##`
  leaving the other side standing alone; empty arguments on BOTH sides
  producing nothing at all, with nothing leaking into the surrounding
  expression; an invalid combination reporting a real diagnostic; `##`
  at the beginning/end of a macro body; `#` not followed by a real
  parameter; a lone `#` inside an object-like macro never validated.
  Full suite (993 tests) passes with zero regressions. Confirmed
  end-to-end against the real running LSP process for stringizing,
  simple and chained pasting, an invalid paste's cascading diagnostics,
  and the `##`-at-end-of-body diagnostic.
- **Not independently tested, though confirmed correct from source and
  documented in `Stringize`'s own remarks**: that stringizing truly
  uses the raw argument rather than a pre-expanded one. This project's
  parse-shape test strategy (prove a *diagnostic* difference between
  right and wrong behavior) can't distinguish the two here - `Stringize`
  always collapses whatever tokens it's given into exactly one string
  literal regardless of their content, so there's no shape difference
  to assert on either way.

## Real macro expansion - Phase 4: a real `#if`/`#elif` constant-expression evaluator (new)

Replaces Phase 2's leniency (a bare `#if` tolerated as always-true, the
first `#elif` reached while searching always taken unevaluated) with a
real evaluator, confirmed from `token/expr.c`'s own `p_eval_prep_expr` -
a small, SEPARATE grammar from the real statement-expression grammar
(`BcsParser.Expressions.cs`): no assignment, no postfix (`[]`/`.`/
calls/`++`/`--`), no format-cast tags, and it actually computes an
`int` value (via plain recursive-descent precedence climbing, not the
real compiler's own goto-chain) rather than just validating shape.

- Confirmed real precedence (low to high, `eval_binary`'s own flat
  goto-chain, read bottom-up): ternary `?:` (optional middle operand,
  the same "Elvis" form the real statement grammar has) → `||` → `&&`
  → `|` → `^` → `&` → `==`/`!=` → relational → shift → additive →
  multiplicative → prefix (`+`/`-`/`!`/`~`) → primary.
- Confirmed real primary set (`eval_primary`'s own switch) - a char
  literal, `defined`, a decimal/octal/hex literal, or a parenthesized
  sub-expression, and genuinely nothing else: a fixed-point/binary/
  radix literal (real token kinds this tokenizer already produces) is
  just as much a real "invalid expression" error here as any other
  unrecognized token, confirmed - NOT silently treated as 0 the way an
  unresolved plain identifier is.
- Every token is read through the normal auto-expanding main pull loop
  (confirmed real, `p_read_expanpreptk` - a macro referenced in a
  condition really does get expanded, e.g. `#if VERSION >= 2`), with
  one confirmed exception: the name tested by `defined`/`defined(...)`
  is read RAW (`EvalDefined`, via `PullOneRaw`, never macro-expanded) -
  confirmed real (`eval_defined`'s own non-expanding reads): `defined`
  needs to know whether the name ITSELF is currently a macro, not what
  it would expand to.
- A plain identifier that isn't a macro evaluates to 0, confirmed real
  (`eval_id`) - the same convention the standard C preprocessor uses.
  Division/mod by zero is a real diagnostic (confirmed,
  "division by zero"); the real compiler aborts compilation entirely
  on it, while this pass recovers by resolving that expression to 0 and
  letting the directive's own `SkipToEndOfLine` clean up the rest of
  the line - the same "diagnose and recover, don't abort" posture this
  whole LSP effort already takes everywhere else.
- **`ReadIfdef`** now branches on the directive: `#ifdef`/`#ifndef`
  still read their name RAW exactly as Phase 2 left them (confirmed
  correct, same reasoning as `defined`'s own name); a real `#if` now
  calls the evaluator instead of unconditionally setting its branch
  active. **`SkipInactiveRegion`** now evaluates an `#elif`'s own
  condition when reached while searching an inactive region, instead of
  unconditionally activating the first one found - a FALSE `#elif` is
  itself skipped too, same as any other sibling, and the search
  continues looking for the next `#elif`/`#else`/`#endif` at that
  level. This is what actually fixes Phase 2's known, documented
  divergence for a chain with more than one real `#elif`.
- **A real bug caught live while writing this phase's own tests, not
  hypothetical:** the evaluator's own trailing lookahead read (needed,
  after reading a primary, to decide whether a binary operator follows
  it) is always one token PAST the expression itself - and whenever
  that lookahead token happened to BE the line's own terminating
  newline, it vanished before the directive's own `SkipToEndOfLine`
  call ever got to see it, so that call would then keep consuming
  looking for a newline that had already gone by - silently swallowing
  the ENTIRE NEXT LINE as if it were trailing garbage on the `#if`'s own
  line. `EvaluateDirectiveCondition` now pushes back whatever token it
  ends up holding before returning, every time, so `SkipToEndOfLine`
  always sees a fresh, correct view of what (if anything) is actually
  left.
- `SkipToEndOfLine` and `SkipInactiveRegion`'s own token reads were
  switched from calling the underlying `BcsTokenizer` directly to going
  through the shared `PullOneRaw` helper (the same `_pushback`-bypass
  bug class already found twice in earlier phases) - proactive, since
  the evaluator's own recovery paths (e.g. `EvalDefined`'s "expected
  ')'" case) now genuinely rely on pushback being honored immediately
  afterward.
- Verified with 19 new tests in `BcsPreprocessorTests.cs`: real operator
  precedence (`1 + 2 * 3 == 7`, decisive against naive left-to-right
  evaluation), the ternary's optional middle operand, `defined`/
  `defined(...)` both ways (true and false), confirmation that
  `defined` never expands the name it's testing, a macro reference
  inside a condition actually expanding, an unresolved identifier
  evaluating to 0, a multi-`#elif` chain picking the correct branch
  (specifically regression coverage for more than one `#elif`, the
  exact case Phase 2 got wrong), a chain correctly falling through to
  `#else` when every `#elif` is false, division-by-zero and invalid-
  and missing-expression diagnostics, and dedicated regression coverage
  for the newline-swallowing pushback bug itself. Full suite
  (1008 tests) passes with zero regressions. Confirmed end-to-end
  against the real running LSP process for real precedence, a
  multi-`#elif` chain, both `defined()` outcomes, the newline-swallowing
  fix, and the division-by-zero diagnostic.

All four macro-expansion phases (object-like/function-like `#define`/
`#undef`; `#ifdef`/`#ifndef`/`#else`/`#endif`; `#`/`##`; a real
`#if`/`#elif` evaluator) are now done. Still deferred: cross-file macro
visibility (listed above).

## Real macro expansion - Phase 2: `#ifdef`/`#ifndef`/`#else`/`#endif` (new)

Conditional compilation, within the same staged port - built directly
on Phase 1's `BcsPreprocessor` layer, no new architecture needed.

- Confirmed real structure from `dirc.c`'s own `push_ifdirc`/
  `pop_ifdirc`/`find_elif`/`find_endif`/`read_search_dirc`: a stack of
  currently-open `#if`-family blocks (`BcsPreprocessor._conditionalBranchTaken`),
  one `bool` per level - true once *some* branch in that chain (the
  original `#if`/`#ifdef`/`#ifndef`, an `#elif`, or the `#else`) has
  actually been entered, which is what makes a later `#elif`/`#else` in
  the same chain correctly skip even when reached. An inactive region is
  skipped by `SkipInactiveRegion`, tracking nested `#if`-family depth so
  a nested block's own `#endif` is never mistaken for the enclosing
  level's. `#else`/`#endif`/`#elif` with no open block, and an unclosed
  block still open at end-of-file, both report a real diagnostic
  (confirmed from `dirc.c`'s own `p_confirm_ifdircs_closed` for the
  latter).
- A bare `#if` is tolerated, not rejected, but its condition is
  deliberately **not evaluated** - the real `p_eval_prep_expr` constant-
  expression evaluator doesn't exist yet (same deferred item as
  `#elif`'s own condition). Its tokens are discarded and the branch is
  simply taken unconditionally, a documented divergence from the real
  compiler until that evaluator exists in a later phase - not a bug.
  Same leniency for the first `#elif` reached while searching an
  inactive region: treated as *the* branch to take, correct only when a
  chain has at most one `#elif` (an accepted, narrow divergence for the
  same reason).
- **A real bug found and fixed while building this phase's own tests**
  (not present in anything Phase 1 shipped as "done," but latent in it):
  `ReadMacroBody` called the underlying `BcsTokenizer` directly instead
  of through the shared `PullOneRaw` helper, so it never consulted the
  `_pushback` buffer `ReadDefine` relies on - for an object-like macro
  with no value at all (`#define FEATURE`, needed as a building block
  for `#ifdef FEATURE`-style tests), the pushed-back terminating
  newline was silently dropped and body-reading incorrectly continued
  into the *next* source line. Fixed by routing it through `PullOneRaw`
  like everything else.
- **A second real bug found the same way:** the main dispatch loop (and
  `SkipInactiveRegion`'s own nested-directive scan) gated directive-name
  recognition on `BcsTokenType.Identifier` alone - correct for
  `ifdef`/`ifndef`/`elif` (none are reserved words anywhere else in the
  language, confirmed from `user.c`'s own keyword table), but `if` and
  `else` collide with real BCS statement keywords and tokenize as their
  own dedicated types (`BcsTokenType.If`/`Else`), never `Identifier`. A
  bare `#if`/`#else` fell straight through to "not ours," reaching
  `BcsParser.ParseHashDirective` instead, which rejected it as "expected
  a directive name after '#'." Fixed by widening both gates to also
  accept `If`/`Else`.
- Verified with new tests in `BcsPreprocessorTests.cs`: `#ifdef`/`#ifndef`
  with a defined and an undefined macro (both directions); `#else`
  taken when the governing condition is false, skipped when true; a
  nested `#ifdef`/`#endif` pair inside a skipped outer region doesn't
  stop the outer scan at the nested `#endif`; `#else`/`#endif` with no
  open block; an unclosed `#ifdef` reports at end-of-file; a bare `#if`
  is tolerated as always-true. Full suite (982 tests) passes with zero
  regressions. Confirmed end-to-end against the real running LSP
  process for all of the above.
