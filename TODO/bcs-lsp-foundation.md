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

## Deferred, tracked, not cut

- **Hover, completion, go-to-definition, any semantic analysis beyond
  raw syntax diagnostics** - named explicitly out of scope for this
  pass in every relevant doc comment. In-app `ScriptDocument` now shows
  diagnostics as a line tint only, with no way to actually read a
  diagnostic's message text in the app itself yet (no hover/tooltip, no
  problems panel) - the message is there (`BcsDiagnostic.Message`),
  just not surfaced visually. The standalone LSP server is the only way
  to see diagnostic text today, via a real LSP client.
- **Multi-file `#include`/`#import` resolution** - this pass parses only
  the one open buffer; a real project's shared headers aren't resolved.
- **Expression-level grammar inside variable declarations/script
  bodies** - `BcsParser` deliberately just captures raw declarator
  tokens up to the terminating `;`/brace-balanced body rather than
  really parsing expressions, so a malformed initializer (e.g.
  `int x = ;`) is *not* caught by this pass - confirmed directly via the
  LSP smoke test, not assumed. Real syntax errors it *does* catch today:
  an unclosed block, an unexpected top-level token, a missing directive
  argument.
- **Exact grammar position for `#library`/`#import`/`#libdefine`/
  `wadauthor`-family pragmas** - absent from the real compiler's
  confirmed `#`-directive table; this pass accepts both a `#`-prefixed
  and bare-keyword spelling defensively. Read `zt-bcc`'s own
  `src/parse/stmt.c` before treating either as authoritative.
- **Macro/preprocessor support** (`#define`, `##`, `TK_TYPENAME`-adjacent
  macro-only pseudo-tokens) - not modeled at all; `#define`/`#region`/
  `#endregion` are silently skipped to end of line.
