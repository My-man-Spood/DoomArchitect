using System.Text;
using DoomArchitect.Core.ZDoom; // for doc-comment <see cref="ZDTextParser"/>/<see cref="ZScriptParser"/> references only

namespace DoomArchitect.Core.ZDoom.Bcs;

/// <summary>
/// A recursive-descent BCS parser over <see cref="BcsTokenizer"/>, built
/// specifically for this project's own LSP use case rather than inheriting
/// <see cref="ZDTextParser"/> the way <c>DecorateParser</c>/<c>ZScriptParser</c>
/// do. That base class is purpose-built for this map editor's resource-
/// discovery posture (a <c>Stream</c>/<c>BinaryReader</c>/<c>SourceName</c>
/// parse-target model with include-file swap/restore machinery, and a
/// single <c>HasError</c>/<c>ErrorDescription</c> field - enough for "stop,
/// this input is broken, contribute nothing" when discovering actors for
/// the Thing browser). An LSP needs a *list* of diagnostics instead, so
/// one typo doesn't suppress every other one in the file - structurally
/// incompatible with that single-error model - and its input is always
/// just "the open buffer's current text," not a resource with an include
/// closure to swap in and out of. So this is its own class, built
/// directly on <see cref="BcsTokenizer"/>.
///
/// It does borrow one proven *technique* from <c>ZScriptParser.SkipBlock</c>
/// (brace-depth counting via raw tokens until depth returns to 0) -
/// reimplemented here, not reused via inheritance, since a depth-counting
/// failure here needs to append a diagnostic and attempt recovery rather
/// than set one field and halt.
///
/// The AST this produces is deliberately minimal - just enough node
/// shapes, each carrying its own source position, to answer "does this
/// parse, and where exactly did it stop making sense." Resolving a
/// script's flag words into typed booleans, validating a `special`
/// declaration's argument counts, or anything else semantic is explicitly
/// out of scope for this pass (see TODO/TODO.md).
///
/// Confirmed from `zt-bcc`'s own `src/parse/library.c` (not
/// `src/parse/stmt.c`, the original guess before this was pinned down):
/// `#library`/`#import`/`#libdefine`/`#linklibrary`/`#encryptstrings`/
/// `#nocompact`/`#wadauthor`/`#nowadauthor` are *always* `#`-prefixed in
/// the real grammar - `read_module_item` dispatches purely on whether the
/// current token is `#`, so there is no bare-keyword form for any of
/// these at module scope at all. `#libdefine` shares `#define`'s exact
/// grammar (both go through the same `read_define`); the rest take
/// either no argument at all (`#encryptstrings`/`#nocompact`/
/// `#wadauthor`/`#nowadauthor`) or one string literal (`#library`,
/// optional - a bare `#library` with no name is valid and just means
/// "use the default name"; `#linklibrary`, required). None of their
/// semantic effects (string encryption, compaction format, author
/// detection, link dependencies) are modeled here - this pass only needs
/// to recognize and correctly consume them so a real file using one
/// doesn't get a bogus "unknown directive" diagnostic. `strict`, by
/// contrast, genuinely is valid bare (no `#`) - it's a namespace
/// qualifier (`strict namespace Foo { ... }`, confirmed from that same
/// source's `is_namespace`), not a pragma; namespaces aren't modeled by
/// this pass at all, so it's tolerated and skipped rather than flagged.
/// </summary>
/// <summary>
/// Which kind of declaration-shaped token run <see cref="BcsParser.DeclarationScanner"/>
/// is watching for - configures both the trigger-token set and whether
/// the real indexed-declarator form (<c>int 0:myvar</c>) is grammatically
/// possible at all (only in <see cref="VariableDeclaration"/> - confirmed
/// impossible in the other two from <c>zt-bcc</c>'s own
/// <c>src/parse/dec.c</c>).
/// </summary>
internal enum DeclarationScanMode { VariableDeclaration, Parameter, EnumMember }

public sealed partial class BcsParser
{
    /// <summary>
    /// Recognizes declaration-shaped token patterns while a caller walks
    /// tokens for an entirely different reason (brace/paren-depth
    /// bookkeeping it already has to do) - additive to those existing
    /// loops, not a parser of its own, and deliberately backward-looking
    /// only (<see cref="BcsTokenizer"/> never un-reads, so no forward
    /// lookahead is available at any of this parser's call sites).
    ///
    /// Four rules, all expressed purely in terms of a 3-token trailing
    /// window of types plus the current token and its depth:
    /// <list type="bullet">
    /// <item>A (direct): an <see cref="BcsTokenType.Identifier"/>
    /// immediately after a trigger token (a type keyword, or <c>{</c> in
    /// <see cref="DeclarationScanMode.EnumMember"/> mode) - starts a new
    /// declarator list at the current depth.</item>
    /// <item>B (comma-continuation): an identifier immediately after
    /// <c>,</c>, while a list is active at the *same* depth it started
    /// at - the depth-equality guard is what stops `Foo(a, b)` (a call,
    /// not a declaration) from matching: entering `Foo(` never started a
    /// list in the first place, since no trigger token preceded `Foo`.</item>
    /// <item>C (indexed-direct): the real `int 0:myvar` shape - an
    /// identifier after `:` after a decimal literal after a trigger
    /// token. Confirmed real grammar via `dec.c`'s `read_instance`/
    /// `read_storage_index` (`read_instance_list`, dec.c:1121-1172) -
    /// matches this parser's own existing `int 0:myvar;` test fixture.</item>
    /// <item>D (indexed-continuation): same as C but the trigger is a
    /// prior `,` instead (`global int 0:a, 1:b;`), same depth guard as B.</item>
    /// </list>
    /// A list's context ends on a top-level `;` at the depth it started
    /// at, or if depth ever drops below that (defensive - e.g. mid-
    /// recovery, having exited the enclosing scope without one).
    /// </summary>
    private sealed class DeclarationScanner
    {
        private static readonly HashSet<BcsTokenType> TypeKeywords = new()
        {
            BcsTokenType.Int, BcsTokenType.Str, BcsTokenType.Bool, BcsTokenType.Void,
            BcsTokenType.Raw, BcsTokenType.Fixed, BcsTokenType.Auto, BcsTokenType.Char,
        };

        /// <summary>A nested type's own private members aren't visible to its containing type in C# (only the reverse) - this is the one accessor <see cref="BcsParser.SkipBracedBlock"/> needs to recognize where a local declaration statement may start.</summary>
        public static bool IsTypeKeyword(BcsTokenType type) => TypeKeywords.Contains(type);

        private readonly DeclarationScanMode _mode;
        private readonly List<BcsSymbol> _names = new();
        private BcsTokenType? _prev1, _prev2, _prev3;
        // The actual text of the token behind _prev1/_prev3 respectively -
        // needed because the trigger token IS the declared name's own type
        // (confirmed from zt-bcc's dec.c: a variable/parameter's type
        // keyword always sits immediately before its name, or before the
        // ':' index in the indexed form), but _prev1/_prev3 only remember
        // its *kind*, not its text.
        private string? _prevText1, _prevText2, _prevText3;
        private bool _listActive;
        private int _listDepth;
        // Remembered so a comma-continuation (rule B/D) can reuse the type
        // the list started with - confirmed real grammar for a plain
        // variable declarator list (`int a, b, c;` - one shared type), but
        // NOT for parameters, where BCS requires each one to restate its
        // own type (`int a, int b`, confirmed from dec.c's read_param) -
        // rule A simply re-fires for each parameter in that case instead,
        // so this is only ever actually read back by rule B/D.
        private string _listType = string.Empty;

        public DeclarationScanner(DeclarationScanMode mode) => _mode = mode;

        public IReadOnlyList<BcsSymbol> Names => _names;

        /// <summary><see cref="DeclarationScanMode.EnumMember"/> names are enum members; both other modes (plain variable declarators and function/script parameters) are indistinguishable as completable things, so both map to <see cref="BcsSymbolKind.Variable"/>.</summary>
        private static BcsSymbolKind KindFor(DeclarationScanMode mode) =>
            mode == DeclarationScanMode.EnumMember ? BcsSymbolKind.EnumMember : BcsSymbolKind.Variable;

        private bool IsTrigger(BcsTokenType? type) =>
            _mode == DeclarationScanMode.EnumMember ? type == BcsTokenType.OpenCurly : type.HasValue && TypeKeywords.Contains(type.Value);

        public void Observe(BcsToken token, int depth)
        {
            if (_listActive && depth < _listDepth) _listActive = false;

            if (token.Type == BcsTokenType.Identifier)
            {
                var allowIndexed = _mode == DeclarationScanMode.VariableDeclaration;
                var kind = KindFor(_mode);
                // EnumMember mode's "trigger" is punctuation ('{'/','), not
                // a real type keyword - there is no type to surface there.
                var trackType = _mode != DeclarationScanMode.EnumMember;

                if (IsTrigger(_prev1)) // rule A
                {
                    _listType = trackType ? _prevText1 ?? string.Empty : string.Empty;
                    _names.Add(new BcsSymbol(token.RawValue, kind, token.Line, token.Column, Type: _listType));
                    _listActive = true;
                    _listDepth = depth;
                }
                else if (_prev1 == BcsTokenType.Comma && _listActive && depth == _listDepth) // rule B
                {
                    _names.Add(new BcsSymbol(token.RawValue, kind, token.Line, token.Column, Type: _listType));
                }
                else if (allowIndexed && _prev1 == BcsTokenType.Colon && _prev2 == BcsTokenType.LitDecimal && IsTrigger(_prev3)) // rule C
                {
                    _listType = trackType ? _prevText3 ?? string.Empty : string.Empty;
                    _names.Add(new BcsSymbol(token.RawValue, kind, token.Line, token.Column, Type: _listType));
                    _listActive = true;
                    _listDepth = depth;
                }
                else if (allowIndexed && _prev1 == BcsTokenType.Colon && _prev2 == BcsTokenType.LitDecimal && _prev3 == BcsTokenType.Comma && _listActive && depth == _listDepth) // rule D
                {
                    _names.Add(new BcsSymbol(token.RawValue, kind, token.Line, token.Column, Type: _listType));
                }
            }
            else if (token.Type == BcsTokenType.Semicolon && depth == _listDepth)
            {
                _listActive = false;
            }

            _prev3 = _prev2;
            _prev2 = _prev1;
            _prev1 = token.Type;
            _prevText3 = _prevText2;
            _prevText2 = _prevText1;
            _prevText1 = token.Value;
        }
    }

    private readonly BcsPreprocessor _preprocessor;
    private readonly List<BcsDiagnostic> _diagnostics;
    private BcsToken _current;
    /// <summary>Every comment token skipped by the most recent <see cref="Advance"/> call to reach <see cref="_current"/> - see <see cref="ExtractDocComment"/>.</summary>
    private List<BcsToken> _currentLeadingComments = new();

    public BcsParser(BcsTokenizer tokenizer, List<BcsDiagnostic> diagnostics)
    {
        _preprocessor = new BcsPreprocessor(tokenizer, diagnostics);
        _diagnostics = diagnostics;
        _current = _preprocessor.NextSignificantToken(includeNewlines: false, out _currentLeadingComments);
    }

    /// <summary>Convenience one-shot entry point - wraps <paramref name="source"/> as a <see cref="MemoryStream"/> (never real file I/O), used by both the language server (re-parsing the open buffer on every change) and tests.</summary>
    public static (BcsCompilationUnit Unit, List<BcsDiagnostic> Diagnostics) Parse(string source)
    {
        var diagnostics = new List<BcsDiagnostic>();
        var tokenizer = new BcsTokenizer(new BinaryReader(new MemoryStream(Encoding.UTF8.GetBytes(source))), diagnostics);
        var unit = new BcsParser(tokenizer, diagnostics).Parse();
        return (unit, diagnostics);
    }

    /// <summary>
    /// Like <see cref="Parse"/>, but also resolves and recursively parses
    /// every <c>#include</c>/<c>#import</c> this file references (and
    /// transitively, theirs too) into a <see cref="BcsProgram"/>. Stays
    /// filesystem-agnostic itself - <paramref name="readFile"/> is the
    /// caller's own way to turn a resolved path into text (or
    /// <c>null</c> if it can't), the same caller-injected-resolver shape
    /// <c>DecorateParser</c>/<c>ZScriptParser</c>'s own <c>OnInclude</c>
    /// already uses for their (lump-path, not real-filesystem) includes -
    /// so this stays trivially testable against real temp files without
    /// needing Godot's <c>FileAccess</c> or any particular I/O API baked
    /// in. <paramref name="sourcePath"/> is this file's own resolved
    /// path (null for an unsaved buffer with nowhere to resolve a
    /// relative include against - those are just skipped, not an error).
    ///
    /// Path resolution confirmed from the real compiler's own
    /// <c>src/task.c</c> (<c>identify_file_relative</c>): an absolute
    /// path is used as-is; a relative one resolves against the
    /// *including* file's own directory. (Not modeled: that same
    /// function's further fallbacks - compiler <c>-i</c> include
    /// directories and a bundled default lib dir - this project has no
    /// equivalent configuration surface for either yet.) Resolved paths
    /// are deduped (case-insensitively, normalized via
    /// <see cref="Path.GetFullPath"/>) so a diamond-shaped or circular
    /// include graph - including one that eventually cycles back to
    /// this very file - is parsed at most once per file, never
    /// infinitely; this is also what makes true self-<c>#import</c> safe
    /// without needing the real compiler's own dedicated diagnostic for
    /// it.
    ///
    /// An include/import that can't be resolved or read reports a
    /// warning - but only when it's written directly in <paramref name="source"/>
    /// itself, never for one found deep inside an already-included
    /// file: that second case isn't actionable from here (its line
    /// number belongs to a different file entirely, and
    /// <see cref="BcsDiagnostic"/> has no file field to say which one),
    /// and surfacing it would be actively misleading, not helpful.
    /// </summary>
    public static BcsProgram ParseProgram(string source, string? sourcePath, Func<string, string?> readFile)
    {
        var (mainUnit, diagnostics) = Parse(source);

        var included = new List<(string Path, BcsCompilationUnit Unit)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (sourcePath != null) visited.Add(Path.GetFullPath(sourcePath));

        void ResolveIncludes(BcsCompilationUnit unit, string? baseDir, bool reportDiagnostics)
        {
            foreach (var member in unit.Members)
            {
                var rawPath = member switch
                {
                    BcsIncludeDirective include => include.Path,
                    BcsImportDirective import => import.Path,
                    _ => null,
                };
                if (string.IsNullOrEmpty(rawPath)) continue;

                var resolved = ResolveIncludePath(rawPath, baseDir);
                if (resolved == null)
                {
                    if (reportDiagnostics)
                        diagnostics.Add(new BcsDiagnostic($"cannot resolve relative path '{rawPath}' - save this file first", member.Line, member.Column, BcsDiagnosticSeverity.Warning));
                    continue;
                }

                var normalized = Path.GetFullPath(resolved);
                if (!visited.Add(normalized)) continue;

                var text = readFile(resolved);
                if (text == null)
                {
                    if (reportDiagnostics)
                        diagnostics.Add(new BcsDiagnostic($"included file not found: '{rawPath}'", member.Line, member.Column, BcsDiagnosticSeverity.Warning));
                    continue;
                }

                var (includedUnit, _) = Parse(text); // the included file's own diagnostics are intentionally discarded - see BcsProgram's own remarks
                included.Add((resolved, includedUnit));
                ResolveIncludes(includedUnit, Path.GetDirectoryName(resolved), reportDiagnostics: false);
            }
        }

        ResolveIncludes(mainUnit, sourcePath != null ? Path.GetDirectoryName(sourcePath) : null, reportDiagnostics: true);
        return new BcsProgram(mainUnit, included, diagnostics);
    }

    private static string? ResolveIncludePath(string rawPath, string? baseDir)
    {
        if (Path.IsPathRooted(rawPath)) return rawPath;
        return baseDir == null ? null : Path.Combine(baseDir, rawPath);
    }

    private void Advance() => _current = _preprocessor.NextSignificantToken(includeNewlines: false, out _currentLeadingComments);

    private void AddDiagnostic(string message, BcsToken token) =>
        _diagnostics.Add(new BcsDiagnostic(message, token.Line, token.Column));

    /// <summary>
    /// Builds a declaration's leading doc comment text out of
    /// <paramref name="leadingComments"/> (whatever <see cref="Advance"/>
    /// most recently skipped to reach this declaration's own first
    /// token) - walks backward from the one closest to
    /// <paramref name="declarationStartLine"/>, keeping a contiguous,
    /// gap-free run (no blank line, confirmed by comparing real line
    /// numbers rather than tracking newline tokens separately - a
    /// skipped comment's own <see cref="BcsToken.Line"/>, plus however
    /// many embedded newlines a multi-line block comment's <see cref="BcsToken.Value"/>
    /// contains, gives its real last line directly). A blank line (or
    /// anything else) breaks the chain - the same heuristic every other
    /// "leading doc comment" convention uses, and the same one that
    /// correctly excludes an unrelated *trailing* comment left dangling
    /// on a previous declaration's own last line, as long as there's a
    /// blank line separating it from this one (a known, accepted
    /// ambiguity if there isn't).
    /// </summary>
    /// <summary><c>internal</c>, not <c>private</c> - <see cref="BcsPreprocessor"/> reuses this exact algorithm for a `#define`'s own leading doc comment, since it now fully owns reading `#define`/`#libdefine` itself (see its own remarks).</summary>
    internal static string ExtractDocComment(IReadOnlyList<BcsToken> leadingComments, int declarationStartLine)
    {
        if (leadingComments.Count == 0) return string.Empty;

        var kept = new List<BcsToken>();
        var expectedEndLine = declarationStartLine - 1;

        for (var i = leadingComments.Count - 1; i >= 0; i--)
        {
            var comment = leadingComments[i];
            var endLine = comment.Line + comment.Value.Count(c => c == '\n');
            if (endLine != expectedEndLine) break;

            kept.Insert(0, comment);
            expectedEndLine = comment.Line - 1;
        }

        return kept.Count == 0 ? string.Empty : string.Join("\n", kept.Select(c => c.Value.Trim())).Trim();
    }

    public BcsCompilationUnit Parse()
    {
        var unit = new BcsCompilationUnit();
        while (_current.Type != BcsTokenType.EndOfInput)
        {
            var member = ParseTopLevelMember();
            if (member != null) unit.Members.Add(member);
        }

        // #define/#libdefine are now fully consumed by _preprocessor
        // before this method's own loop ever sees them (see its own
        // remarks) - this is what keeps the existing completion/hover/
        // go-to-def-for-macro-names feature working unchanged, just
        // sourced from the preprocessor's own bookkeeping instead of a
        // case inside ParseHashDirective.
        foreach (var macro in _preprocessor.Macros)
        {
            unit.Members.Add(new BcsDefineDirective { Name = macro.Name, DocComment = macro.DocComment, Line = macro.Line, Column = macro.Column });
        }

        return unit;
    }

    private BcsNode? ParseTopLevelMember()
    {
        var start = _current;

        if (_current.Type == BcsTokenType.Hash) return ParseHashDirective();
        if (_current.Type == BcsTokenType.Script) return ParseScript();
        if (_current.Type == BcsTokenType.Special) return ParseSpecial();
        if (_current.Type == BcsTokenType.Function) return ParseFunction();
        if (_current.Type == BcsTokenType.Enum) return ParseEnum();

        if (_current.Type is BcsTokenType.Global or BcsTokenType.World or BcsTokenType.Static or BcsTokenType.Const
            or BcsTokenType.Int or BcsTokenType.Str or BcsTokenType.Bool or BcsTokenType.Void
            or BcsTokenType.Raw or BcsTokenType.Fixed)
        {
            return ParseVariableDeclaration();
        }

        // 'strict' alone (bare, no '#') is real grammar - a namespace
        // qualifier, not a pragma (see this class's own remarks) -
        // tolerated and skipped since namespaces aren't modeled here.
        if (_current.Type == BcsTokenType.Strict)
        {
            Advance();
            SkipToSemicolon();
            return null;
        }

        AddDiagnostic($"unexpected token '{start.Value}'", start);
        return Recover();
    }

    private BcsNode? ParseHashDirective()
    {
        var start = _current;
        Advance(); // '#'

        if (_current.Type != BcsTokenType.Identifier)
        {
            AddDiagnostic("expected a directive name after '#'", _current);
            return Recover();
        }

        var directive = _current.Value;
        Advance();

        switch (directive)
        {
            case "include": return ParseStringArgDirective(start, isImport: false);
            case "import": return ParseStringArgDirective(start, isImport: true);
            case "library": return ParseLibraryDirective(start);
            case "linklibrary":
                // #linklibrary "name" - a build-time link dependency,
                // confirmed from library.c's read_linklibrary; not
                // modeled as its own node (this pass doesn't track link
                // dependencies), just consumed correctly so it isn't
                // mistaken for an unknown directive.
                if (_current.Type == BcsTokenType.LitString) Advance();
                else AddDiagnostic("expected a string literal after '#linklibrary'", _current);
                return null;
            // "define"/"libdefine" never reach here at all anymore -
            // BcsPreprocessor fully intercepts both before this method
            // ever sees the '#' (see its own remarks); BcsParser.Parse
            // builds a BcsDefineDirective per BcsPreprocessor.Macros
            // entry after the main parse loop finishes instead.
            case "encryptstrings":
            case "nocompact":
            case "wadauthor":
            case "nowadauthor":
                // No arguments at all in the real grammar (library.c) -
                // the directive word itself (already consumed above) is
                // the whole directive. Not modeled as their own node -
                // this pass doesn't track string-encryption/compaction-
                // format/author-detection semantics.
                return null;
            case "region":
            case "endregion":
                // Not modeled as their own node at all - skip to end of line, which NextSignificantToken's default (not including newlines) would otherwise swallow, so ask for it explicitly here.
                while (_preprocessor.NextSignificantToken(includeNewlines: true) is { Type: not (BcsTokenType.Newline or BcsTokenType.EndOfInput) }) { }
                Advance();
                return null;
            default:
                AddDiagnostic($"unknown directive '#{directive}'", start);
                return Recover();
        }
    }

    private BcsNode? ParseStringArgDirective(BcsToken start, bool isImport)
    {
        if (_current.Type != BcsTokenType.LitString)
        {
            AddDiagnostic($"expected a string literal after '#{(isImport ? "import" : "include")}'", _current);
            return Recover();
        }

        var path = _current.Value;
        Advance();
        return isImport
            ? new BcsImportDirective { Path = path, Line = start.Line, Column = start.Column }
            : new BcsIncludeDirective { Path = path, Line = start.Line, Column = start.Column };
    }

    /// <summary>
    /// <c>#library ["name"]</c> - confirmed from <c>library.c</c>'s
    /// <c>read_library</c>: the string literal is optional, not required
    /// - a bare <c>#library</c> with no name at all is valid real BCS and
    /// just means "use the default name" (previously, incorrectly,
    /// treated as a missing-argument error here).
    /// </summary>
    private BcsNode ParseLibraryDirective(BcsToken start)
    {
        var name = string.Empty;
        if (_current.Type == BcsTokenType.LitString) { name = _current.Value; Advance(); }

        return new BcsLibraryDirective { Name = name, Line = start.Line, Column = start.Column };
    }

    /// <summary>
    /// <c>script &lt;number-or-name&gt; [(...)] [flag...] { ... }</c> - the
    /// optional parenthesized group after the number is captured as raw
    /// text, not interpreted, since real BCS uses that position for both
    /// a script-type keyword (<c>(open)</c>) and an argument list
    /// (<c>(int a, int b)</c>), and distinguishing them needs the
    /// grammar-position knowledge this pass deliberately doesn't build -
    /// <see cref="SkipBalancedParens"/>'s own declaration scan sidesteps
    /// that ambiguity rather than resolving it (see its own remarks).
    /// </summary>
    private BcsNode ParseScript()
    {
        var start = _current;
        var docComment = ExtractDocComment(_currentLeadingComments, start.Line);
        Advance(); // 'script'

        var number = _current.Type == BcsTokenType.Identifier ? _current.RawValue : _current.Value;
        var numberLine = _current.Line;
        var numberColumn = _current.Column;
        var isNamedScript = _current.Type is BcsTokenType.LitString or BcsTokenType.Identifier;
        if (_current.Type is BcsTokenType.LitString or BcsTokenType.LitDecimal or BcsTokenType.Identifier) Advance();
        else AddDiagnostic("expected a script number or name", _current);

        string? parenGroup = null;
        var parameterNames = Array.Empty<BcsSymbol>() as IReadOnlyList<BcsSymbol>;
        if (_current.Type == BcsTokenType.OpenParen) (parenGroup, parameterNames) = SkipBalancedParens();

        var flags = new List<string>();
        while (_current.Type == BcsTokenType.Identifier)
        {
            flags.Add(_current.Value);
            Advance();
        }

        var (bodyLocals, bodyLine, bodyColumn, bodyEndLine, bodyEndColumn) = SkipBracedBlock(DeclarationScanMode.VariableDeclaration);

        var node = new BcsScriptDeclaration
        {
            Number = number,
            IsNamedScript = isNamedScript,
            NumberLine = numberLine,
            NumberColumn = numberColumn,
            TypeKeyword = parenGroup,
            BodyLine = bodyLine,
            BodyColumn = bodyColumn,
            BodyEndLine = bodyEndLine,
            BodyEndColumn = bodyEndColumn,
            DocComment = docComment,
            Line = start.Line,
            Column = start.Column,
        };
        node.FlagTokens.AddRange(flags);
        node.ParameterNames.AddRange(parameterNames);
        node.BodyLocals.AddRange(bodyLocals);
        return node;
    }

    /// <summary>
    /// <c>special [-]decimal ':' identifier '(' ... ')' ... (',' ...)*;</c> -
    /// one statement can declare several, comma-separated entries
    /// (confirmed from the real compiler's own `src/parse/dec.c`,
    /// `p_read_special_list`, dec.c:2437-2451). Each entry's own name is
    /// the identifier right after its `decimal ':'` pair, at the
    /// statement's own top level (depth 0, i.e. not inside that entry's
    /// own `(...)` paramspec list) - a small dedicated scan rather than
    /// <see cref="DeclarationScanner"/>, which has no "first vs.
    /// continuation" concept to fit this shape (there's no preceding
    /// type-keyword trigger here at all).
    /// </summary>
    private BcsNode ParseSpecial()
    {
        var start = _current;
        var docComment = ExtractDocComment(_currentLeadingComments, start.Line);
        Advance(); // 'special'

        var headerTokens = new List<string>();
        var names = new List<BcsSymbol>();
        BcsTokenType? prev1 = null, prev2 = null;
        var parenDepth = 0;

        while (_current.Type is not (BcsTokenType.Semicolon or BcsTokenType.EndOfInput))
        {
            if (_current.Type == BcsTokenType.OpenParen) parenDepth++;
            else if (_current.Type == BcsTokenType.CloseParen) parenDepth--;
            else if (parenDepth == 0 && _current.Type == BcsTokenType.Identifier && prev1 == BcsTokenType.Colon && prev2 == BcsTokenType.LitDecimal)
            {
                names.Add(new BcsSymbol(_current.RawValue, BcsSymbolKind.Function, _current.Line, _current.Column));
            }

            headerTokens.Add(_current.Value);
            prev2 = prev1;
            prev1 = _current.Type;
            Advance();
        }

        if (_current.Type == BcsTokenType.Semicolon) Advance();
        else AddDiagnostic("expected ';'", _current);

        var node = new BcsSpecialDeclaration { DocComment = docComment, Line = start.Line, Column = start.Column };
        node.HeaderTokens.AddRange(headerTokens);
        node.Names.AddRange(names);
        return node;
    }

    /// <summary>
    /// <c>function TYPE NAME ( params ) { ... }</c> - <see cref="BcsFunctionDeclaration.Name"/>
    /// is the identifier immediately before the first top-level `(` in
    /// the header; everything inside that first top-level paren group
    /// (and only that one - a second, later paren group in the header
    /// doesn't re-trigger this) feeds a <see cref="DeclarationScanner"/>
    /// in <see cref="DeclarationScanMode.Parameter"/> mode for
    /// <see cref="BcsFunctionDeclaration.ParameterNames"/>, extracted
    /// while real tokens (not yet flattened to <see cref="BcsFunctionDeclaration.HeaderTokens"/>'s
    /// plain strings) are still available.
    /// </summary>
    private BcsNode ParseFunction()
    {
        var start = _current;
        var docComment = ExtractDocComment(_currentLeadingComments, start.Line);
        Advance(); // 'function'

        // The return type is always the very first token of the header,
        // immediately after 'function' - confirmed from dec.c's
        // read_object: a function's type specifier always comes right
        // after the (optional) function keyword, before its name.
        var returnType = _current.Value;

        var headerTokens = new List<string>();
        string? name = null;
        int nameLine = 0, nameColumn = 0;
        string? lastIdentifier = null;
        int lastIdentifierLine = 0, lastIdentifierColumn = 0;
        DeclarationScanner? paramScanner = null;
        var parenDepth = 0;

        while (_current.Type is not (BcsTokenType.OpenCurly or BcsTokenType.Semicolon or BcsTokenType.EndOfInput))
        {
            if (paramScanner == null && parenDepth == 0 && _current.Type == BcsTokenType.OpenParen)
            {
                name = lastIdentifier;
                nameLine = lastIdentifierLine;
                nameColumn = lastIdentifierColumn;
                paramScanner = new DeclarationScanner(DeclarationScanMode.Parameter);
                parenDepth = 1;
            }
            else if (paramScanner != null && parenDepth > 0)
            {
                if (_current.Type == BcsTokenType.OpenParen) parenDepth++;
                else if (_current.Type == BcsTokenType.CloseParen) parenDepth--;

                if (parenDepth > 0) paramScanner.Observe(_current, parenDepth);
            }
            else if (_current.Type == BcsTokenType.Identifier)
            {
                lastIdentifier = _current.RawValue;
                lastIdentifierLine = _current.Line;
                lastIdentifierColumn = _current.Column;
            }

            headerTokens.Add(_current.Value);
            Advance();
        }

        var bodyLocals = new List<BcsSymbol>();
        int bodyLine = 0, bodyColumn = 0, bodyEndLine = 0, bodyEndColumn = 0;
        if (_current.Type == BcsTokenType.OpenCurly)
            (bodyLocals, bodyLine, bodyColumn, bodyEndLine, bodyEndColumn) = SkipBracedBlock(DeclarationScanMode.VariableDeclaration);
        else if (_current.Type == BcsTokenType.Semicolon) Advance(); // a forward declaration
        else AddDiagnostic("expected '{' or ';'", _current);

        var node = new BcsFunctionDeclaration
        {
            ReturnType = returnType,
            DocComment = docComment,
            Name = name,
            NameLine = nameLine,
            NameColumn = nameColumn,
            BodyLine = bodyLine,
            BodyColumn = bodyColumn,
            BodyEndLine = bodyEndLine,
            BodyEndColumn = bodyEndColumn,
            Line = start.Line,
            Column = start.Column,
        };
        node.HeaderTokens.AddRange(headerTokens);
        if (paramScanner != null) node.ParameterNames.AddRange(paramScanner.Names);
        node.BodyLocals.AddRange(bodyLocals);
        return node;
    }

    private BcsNode ParseEnum()
    {
        var start = _current;
        var docComment = ExtractDocComment(_currentLeadingComments, start.Line);
        Advance(); // 'enum'

        string? name = null;
        if (_current.Type == BcsTokenType.Identifier) { name = _current.RawValue; Advance(); }

        var (members, _, _, _, _) = SkipBracedBlock(DeclarationScanMode.EnumMember);
        if (_current.Type == BcsTokenType.Semicolon) Advance();

        var node = new BcsEnumDeclaration { Name = name, DocComment = docComment, Line = start.Line, Column = start.Column };
        node.MemberNames.AddRange(members);
        return node;
    }

    /// <summary>
    /// A type keyword (possibly preceded by `global`/`world`/`static`/
    /// `const`, whichever token actually started this declaration - see
    /// <see cref="ParseTopLevelMember"/>'s dispatch) plus one or more
    /// comma-separated declarators, each optionally indexed
    /// (`global int 0:a, 1:b;`), each with a real, validated initializer
    /// expression now (<see cref="ParseDeclarator"/>) - this is what
    /// finally catches something like `int x = ;` as a real diagnostic,
    /// which the old purely-heuristic <see cref="DeclarationScanner"/>-
    /// based version of this method never could (it only ever looked
    /// *backward* for declaration-shaped patterns, never forward into
    /// what followed a name at all).
    ///
    /// The leading modifier (`global`/`world`/`static`/`const`), if
    /// present, is skipped *before* capturing <c>typeKeyword</c> -
    /// confirmed real grammar from <c>dec.c</c>'s <c>read_storage</c>/
    /// <c>read_object</c>: at most one such modifier, always
    /// immediately followed by the real type specifier - so
    /// <c>typeKeyword</c> (surfaced as every declarator's own
    /// <see cref="BcsSymbol.Type"/>, e.g. in hover) is always the real
    /// type ("int"), never the modifier ("global").
    /// </summary>
    private BcsNode ParseVariableDeclaration()
    {
        var start = _current;
        var docComment = ExtractDocComment(_currentLeadingComments, start.Line);

        if (_current.Type is BcsTokenType.Global or BcsTokenType.World or BcsTokenType.Static or BcsTokenType.Const) Advance();

        var typeKeyword = _current.Value;
        Advance();

        var declaratorTokens = new List<string>();
        var declaratorNames = new List<BcsSymbol>();

        while (true)
        {
            var declarator = ParseDeclarator(typeKeyword, declaratorTokens);
            if (declarator is { } found) declaratorNames.Add(found);
            else RecoverExpression();

            if (_current.Type == BcsTokenType.Comma) { declaratorTokens.Add(_current.Value); Advance(); continue; }
            break;
        }

        if (_current.Type == BcsTokenType.Semicolon) Advance();
        else AddDiagnostic("expected ';'", _current);

        var node = new BcsVariableDeclaration { TypeKeyword = typeKeyword, DocComment = docComment, Line = start.Line, Column = start.Column };
        node.DeclaratorTokens.AddRange(declaratorTokens);
        node.DeclaratorNames.AddRange(declaratorNames);
        return node;
    }

    /// <summary>
    /// One declarator: an optional indexed-storage prefix (`0:`,
    /// confirmed real grammar from <c>dec.c</c>'s <c>read_instance</c>/
    /// <c>read_storage_index</c>), the name, an optional array size
    /// (`[expr]` - real expression parsing; a bare `[]` is tolerated
    /// without error, deliberately lenient rather than risk a false
    /// positive on a real-but-unconfirmed "size can be omitted" case),
    /// and an optional initializer (`= expr`). Shared by both
    /// <see cref="ParseVariableDeclaration"/> (top-level) and
    /// <see cref="SkipBracedBlock"/>'s own local-declaration hand-off.
    ///
    /// <paramref name="rawTokens"/>, when supplied, only collects the
    /// declarator's own *name-shaped* tokens (the indexed prefix, the
    /// name, the array brackets) - never an initializer's own tokens,
    /// since those are now really parsed/validated rather than just
    /// recorded; nothing ever consumed <see cref="BcsVariableDeclaration.DeclaratorTokens"/>
    /// for initializer text specifically, so this is a deliberate,
    /// harmless narrowing, not a regression.
    ///
    /// Returns <c>null</c> only when no name could be found at all
    /// (the caller decides how to recover from that).
    /// </summary>
    private BcsSymbol? ParseDeclarator(string typeKeyword, List<string>? rawTokens = null)
    {
        void Consume() { rawTokens?.Add(_current.Value); Advance(); }

        if (_current.Type == BcsTokenType.LitDecimal)
        {
            Consume();
            if (_current.Type == BcsTokenType.Colon) Consume();
            else { AddDiagnostic("expected ':' after an indexed declarator's index", _current); return null; }
        }

        if (_current.Type != BcsTokenType.Identifier)
        {
            AddDiagnostic("expected a declarator name", _current);
            return null;
        }

        var nameToken = _current;
        Consume();
        var symbol = new BcsSymbol(nameToken.RawValue, BcsSymbolKind.Variable, nameToken.Line, nameToken.Column, Type: typeKeyword);

        if (_current.Type == BcsTokenType.OpenSquare)
        {
            Consume();
            if (_current.Type != BcsTokenType.CloseSquare && !ParseExpression()) RecoverExpression();
            if (_current.Type == BcsTokenType.CloseSquare) Consume();
            else AddDiagnostic("expected ']'", _current);
        }

        if (_current.Type == BcsTokenType.OpAssign)
        {
            Advance(); // the initializer's own tokens are real expression content now, not raw-captured - see this method's own remarks
            if (!ParseExpression()) RecoverExpression();
        }

        return symbol;
    }

    /// <summary>
    /// Consumes a balanced "{ ... }" block, counting nested braces - the
    /// technique <c>ZScriptParser.SkipBlock</c> uses, reimplemented
    /// directly against <see cref="BcsTokenizer"/> (see this class's own
    /// remarks on why that's not inherited). Depth here counts every
    /// bracket kind (curly/paren/square combined, matching
    /// <see cref="ParseVariableDeclaration"/>'s own existing technique),
    /// not just curly - needed so a nested call's parenthesized argument
    /// list (`Foo(a, b)`) registers as deeper than the body it's in for
    /// <see cref="DeclarationScanner"/>'s depth-equality guard, even
    /// though only curly braces actually end this particular block.
    /// Returns every declaration-shaped name found along the way
    /// (<paramref name="mode"/> - almost always <see cref="DeclarationScanMode.VariableDeclaration"/>
    /// for a real body, <see cref="DeclarationScanMode.EnumMember"/> for
    /// an enum's body) - this is the one place body-locals/enum-members
    /// get collected at all; nothing else in this parser ever looks
    /// inside a `{ ... }`. Also returns the block's own start (the
    /// opening `{`) and end (the matching closing `}`) positions -
    /// callers thread these into their node's own body-span fields so
    /// <see cref="BcsCompilationUnit.CollectSymbolsVisibleAt"/> can later
    /// tell whether a cursor line falls inside this specific body.
    ///
    /// In <see cref="DeclarationScanMode.VariableDeclaration"/> mode
    /// only, a local declaration statement now gets the same real,
    /// validated parsing a top-level one does
    /// (<see cref="ParseDeclarator"/>) instead of just being passively
    /// observed by <paramref name="mode"/>'s <see cref="DeclarationScanner"/> -
    /// gated behind <c>atStatementStart</c> (true right after the
    /// opening `{`, a nested block's own opening `{`, a `;`, or a
    /// nested block's closing `}`; false after anything else), which is
    /// what stops a type-conversion *expression* mid-statement
    /// (`x = int(y);` - that `int` is a cast, not a new declaration -
    /// confirmed real grammar from <c>expr.c</c>'s own
    /// <c>read_conversion</c>) from ever being misdetected as one: its
    /// `int` always arrives with the flag already false (preceded by
    /// `=`, never a statement boundary). Everything else in a body -
    /// every non-declaration statement (`if`/`while`/assignment/call) -
    /// remains exactly as opaque to this pass as it always has been;
    /// real statement/control-flow grammar is a distinct, much larger
    /// thing this pass deliberately doesn't add.
    /// </summary>
    private (List<BcsSymbol> Locals, int StartLine, int StartColumn, int EndLine, int EndColumn) SkipBracedBlock(DeclarationScanMode mode)
    {
        var scanner = new DeclarationScanner(mode);

        if (_current.Type != BcsTokenType.OpenCurly)
        {
            AddDiagnostic($"expected '{{', got '{_current.Value}'", _current);
            return (new List<BcsSymbol>(), 0, 0, 0, 0);
        }

        var startLine = _current.Line;
        var startColumn = _current.Column;
        var endLine = startLine;
        var endColumn = startColumn;
        var locals = new List<BcsSymbol>();

        scanner.Observe(_current, 0); // the opening '{' itself - EnumMember mode's trigger, so the very first member (right after it) matches rule A
        var atStatementStart = true;
        Advance();
        var depth = 1;
        while (depth > 0)
        {
            if (_current.Type == BcsTokenType.EndOfInput)
            {
                AddDiagnostic("unexpected end of file inside a block", _current);
                endLine = _current.Line;
                endColumn = _current.Column;
                break;
            }

            if (mode == DeclarationScanMode.VariableDeclaration && atStatementStart && DeclarationScanner.IsTypeKeyword(_current.Type))
            {
                var typeKeyword = _current.Value;
                Advance();

                while (true)
                {
                    var declarator = ParseDeclarator(typeKeyword);
                    if (declarator is { } found) locals.Add(found);
                    else RecoverExpression();

                    if (_current.Type == BcsTokenType.Comma) { Advance(); continue; }
                    break;
                }

                if (_current.Type == BcsTokenType.Semicolon) Advance();
                else AddDiagnostic("expected ';'", _current);

                atStatementStart = true;
                continue; // a declaration statement is depth-neutral - any brackets inside its own initializer(s) were already balanced by the expression parser itself, never touching this block's own depth
            }

            if (_current.Type is BcsTokenType.OpenCurly or BcsTokenType.OpenParen or BcsTokenType.OpenSquare) depth++;
            else if (_current.Type is BcsTokenType.CloseCurly or BcsTokenType.CloseParen or BcsTokenType.CloseSquare) depth--;

            if (depth > 0) scanner.Observe(_current, depth);
            else { endLine = _current.Line; endColumn = _current.Column; }

            atStatementStart = _current.Type == BcsTokenType.Semicolon
                || _current.Type == BcsTokenType.OpenCurly
                || (_current.Type == BcsTokenType.CloseCurly && depth > 0);

            Advance();
        }

        return (scanner.Names.Concat(locals).ToList(), startLine, startColumn, endLine, endColumn);
    }

    /// <summary>
    /// Consumes a balanced "( ... )" group, returning its raw inner text
    /// - used wherever this pass doesn't need to interpret the contents,
    /// only skip past them correctly (see <see cref="ParseScript"/>) -
    /// alongside whatever parameter names a <see cref="DeclarationScanner"/>
    /// found in the same pass. This is exactly how <see cref="ParseScript"/>'s
    /// own `(open)` vs `(int a, int b)` ambiguity gets sidestepped rather
    /// than resolved: `open` is preceded by `(`, not a trigger token, so
    /// the scanner's rule A never fires and the names list comes back
    /// empty; `int a, int b` fires it on both, independent of ever
    /// deciding which shape this group actually is.
    /// </summary>
    private (string Text, IReadOnlyList<BcsSymbol> ParameterNames) SkipBalancedParens()
    {
        var scanner = new DeclarationScanner(DeclarationScanMode.Parameter);
        var sb = new StringBuilder();
        Advance(); // '('
        var depth = 1;
        while (depth > 0 && _current.Type != BcsTokenType.EndOfInput)
        {
            if (_current.Type == BcsTokenType.OpenParen) depth++;
            else if (_current.Type == BcsTokenType.CloseParen)
            {
                depth--;
                if (depth == 0) { Advance(); break; }
            }

            scanner.Observe(_current, depth);
            sb.Append(_current.Value).Append(' ');
            Advance();
        }

        return (sb.ToString().TrimEnd(), scanner.Names);
    }

    private void SkipToSemicolon()
    {
        while (_current.Type is not (BcsTokenType.Semicolon or BcsTokenType.EndOfInput)) Advance();
        if (_current.Type == BcsTokenType.Semicolon) Advance();
    }

    /// <summary>
    /// Skips forward to the next un-nested ';' (consumed) or matching '}'
    /// (left for an enclosing <see cref="SkipBracedBlock"/> to see), so one
    /// bad declaration doesn't swallow every later diagnostic in the file -
    /// the deliberate, necessary divergence from <c>ZDTextParser</c>'s
    /// halt-on-first-error house style (see this class's own remarks).
    /// </summary>
    private BcsSyntaxErrorNode Recover()
    {
        var start = _current;
        var skipped = new StringBuilder();
        var depth = 0;

        while (_current.Type != BcsTokenType.EndOfInput)
        {
            if (depth == 0 && _current.Type == BcsTokenType.Semicolon) { skipped.Append(_current.Value); Advance(); break; }
            if (depth == 0 && _current.Type == BcsTokenType.CloseCurly) break;

            if (_current.Type is BcsTokenType.OpenCurly or BcsTokenType.OpenParen or BcsTokenType.OpenSquare) depth++;
            else if (_current.Type is BcsTokenType.CloseCurly or BcsTokenType.CloseParen or BcsTokenType.CloseSquare) depth--;

            skipped.Append(_current.Value).Append(' ');
            Advance();
        }

        return new BcsSyntaxErrorNode { SkippedText = skipped.ToString().TrimEnd(), Line = start.Line, Column = start.Column };
    }
}
