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
/// One open item, not resolved here: exactly where `#library`/`#import`/
/// `#libdefine`/`wadauthor`/`nowadauthor`/`nocompact`/`encryptstrings`
/// attach grammatically wasn't pinned down while planning this (they're
/// absent from the real compiler's confirmed `#`-directive table, which
/// only covers `#define`/`#include`/`#if...`/`#region`/`#endregion`) -
/// this parser accepts both a `#`-prefixed and a bare-keyword spelling of
/// `library`/`libdefine` defensively, and silently skips the pragma-like
/// words to their terminating `;` without modeling them as their own node
/// yet. Read `zt-bcc`'s own `src/parse/stmt.c` before treating either
/// choice as authoritative.
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

public sealed class BcsParser
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

        private readonly DeclarationScanMode _mode;
        private readonly List<BcsSymbol> _names = new();
        private BcsTokenType? _prev1, _prev2, _prev3;
        private bool _listActive;
        private int _listDepth;

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

                if (IsTrigger(_prev1)) // rule A
                {
                    _names.Add(new BcsSymbol(token.RawValue, kind, token.Line, token.Column));
                    _listActive = true;
                    _listDepth = depth;
                }
                else if (_prev1 == BcsTokenType.Comma && _listActive && depth == _listDepth) // rule B
                {
                    _names.Add(new BcsSymbol(token.RawValue, kind, token.Line, token.Column));
                }
                else if (allowIndexed && _prev1 == BcsTokenType.Colon && _prev2 == BcsTokenType.LitDecimal && IsTrigger(_prev3)) // rule C
                {
                    _names.Add(new BcsSymbol(token.RawValue, kind, token.Line, token.Column));
                    _listActive = true;
                    _listDepth = depth;
                }
                else if (allowIndexed && _prev1 == BcsTokenType.Colon && _prev2 == BcsTokenType.LitDecimal && _prev3 == BcsTokenType.Comma && _listActive && depth == _listDepth) // rule D
                {
                    _names.Add(new BcsSymbol(token.RawValue, kind, token.Line, token.Column));
                }
            }
            else if (token.Type == BcsTokenType.Semicolon && depth == _listDepth)
            {
                _listActive = false;
            }

            _prev3 = _prev2;
            _prev2 = _prev1;
            _prev1 = token.Type;
        }
    }

    private readonly BcsTokenizer _tokenizer;
    private readonly List<BcsDiagnostic> _diagnostics;
    private BcsToken _current;

    public BcsParser(BcsTokenizer tokenizer, List<BcsDiagnostic> diagnostics)
    {
        _tokenizer = tokenizer;
        _diagnostics = diagnostics;
        _current = _tokenizer.NextSignificantToken();
    }

    /// <summary>Convenience one-shot entry point - wraps <paramref name="source"/> as a <see cref="MemoryStream"/> (never real file I/O), used by both the language server (re-parsing the open buffer on every change) and tests.</summary>
    public static (BcsCompilationUnit Unit, List<BcsDiagnostic> Diagnostics) Parse(string source)
    {
        var diagnostics = new List<BcsDiagnostic>();
        var tokenizer = new BcsTokenizer(new BinaryReader(new MemoryStream(Encoding.UTF8.GetBytes(source))), diagnostics);
        var unit = new BcsParser(tokenizer, diagnostics).Parse();
        return (unit, diagnostics);
    }

    private void Advance() => _current = _tokenizer.NextSignificantToken();

    private void AddDiagnostic(string message, BcsToken token) =>
        _diagnostics.Add(new BcsDiagnostic(message, token.Line, token.Column));

    private static bool IsContextualKeyword(BcsToken token, string text) =>
        token.Type == BcsTokenType.Identifier && token.Value == text;

    public BcsCompilationUnit Parse()
    {
        var unit = new BcsCompilationUnit();
        while (_current.Type != BcsTokenType.EndOfInput)
        {
            var member = ParseTopLevelMember();
            if (member != null) unit.Members.Add(member);
        }
        return unit;
    }

    private BcsNode? ParseTopLevelMember()
    {
        var start = _current;

        if (_current.Type == BcsTokenType.Hash) return ParseHashDirective();
        if (IsContextualKeyword(_current, "library")) return ParseBareLibrary();
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

        // Pragma-like top-level words this pass tolerates without modeling as their own node - see this class's own remarks on the unresolved #library/wadauthor-family grammar question.
        if (_current.Type == BcsTokenType.Strict ||
            IsContextualKeyword(_current, "wadauthor") || IsContextualKeyword(_current, "nowadauthor") ||
            IsContextualKeyword(_current, "nocompact") || IsContextualKeyword(_current, "encryptstrings"))
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
            case "library":
            case "libdefine": return ParseLibraryDirective(start);
            case "define":
            {
                // Only the name is extracted (so it's offered by completion) -
                // the value/parameter list still isn't modeled at all, same
                // deliberate scope limit as "region"/"endregion" below: this
                // is not macro-expansion support, just enough to know a name
                // was declared here.
                string? macroName = _current.Type == BcsTokenType.Identifier ? _current.RawValue : null;
                while (_tokenizer.NextSignificantToken(includeNewlines: true) is { Type: not (BcsTokenType.Newline or BcsTokenType.EndOfInput) }) { }
                Advance();
                return macroName != null ? new BcsDefineDirective { Name = macroName, Line = start.Line, Column = start.Column } : null;
            }
            case "region":
            case "endregion":
                // Not modeled as their own node at all - skip to end of line, which NextSignificantToken's default (not including newlines) would otherwise swallow, so ask for it explicitly here.
                while (_tokenizer.NextSignificantToken(includeNewlines: true) is { Type: not (BcsTokenType.Newline or BcsTokenType.EndOfInput) }) { }
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

    private BcsNode ParseLibraryDirective(BcsToken start)
    {
        var name = string.Empty;
        if (_current.Type == BcsTokenType.LitString) { name = _current.Value; Advance(); }
        else AddDiagnostic("expected a string literal after the library directive", _current);

        return new BcsLibraryDirective { Name = name, Line = start.Line, Column = start.Column };
    }

    private BcsNode ParseBareLibrary()
    {
        var start = _current;
        Advance(); // 'library'
        var node = ParseLibraryDirective(start);
        if (_current.Type == BcsTokenType.Semicolon) Advance();
        return node;
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

        var node = new BcsSpecialDeclaration { Line = start.Line, Column = start.Column };
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
        Advance(); // 'function'

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
        Advance(); // 'enum'

        string? name = null;
        if (_current.Type == BcsTokenType.Identifier) { name = _current.RawValue; Advance(); }

        var (members, _, _, _, _) = SkipBracedBlock(DeclarationScanMode.EnumMember);
        if (_current.Type == BcsTokenType.Semicolon) Advance();

        var node = new BcsEnumDeclaration { Name = name, Line = start.Line, Column = start.Column };
        node.MemberNames.AddRange(members);
        return node;
    }

    /// <summary>
    /// A type keyword (possibly preceded by `global`/`world`/`static`/
    /// `const`, whichever token actually started this declaration - see
    /// <see cref="ParseTopLevelMember"/>'s dispatch) plus one or more
    /// comma-separated declarators, each optionally indexed
    /// (`global int 0:a, 1:b;`). <see cref="DeclarationScanner"/> is fed
    /// the real starting token too (observed once, before it's consumed
    /// by the initial <see cref="Advance"/> below) so rule C still fires
    /// correctly for the no-modifier form (`int 0:myvar;`, this parser's
    /// own existing test fixture) where the type keyword itself is never
    /// seen again inside the declarator loop that follows.
    /// </summary>
    private BcsNode ParseVariableDeclaration()
    {
        var start = _current;
        var typeKeyword = _current.Value;

        var scanner = new DeclarationScanner(DeclarationScanMode.VariableDeclaration);
        scanner.Observe(start, 0);
        Advance();

        var declarators = new List<string>();
        var depth = 0;
        while (_current.Type != BcsTokenType.EndOfInput && !(depth == 0 && _current.Type == BcsTokenType.Semicolon))
        {
            if (_current.Type is BcsTokenType.OpenCurly or BcsTokenType.OpenParen or BcsTokenType.OpenSquare) depth++;
            else if (_current.Type is BcsTokenType.CloseCurly or BcsTokenType.CloseParen or BcsTokenType.CloseSquare) depth--;

            if (depth >= 0) scanner.Observe(_current, depth);
            declarators.Add(_current.Value);
            Advance();
        }

        if (_current.Type == BcsTokenType.Semicolon) Advance();
        else AddDiagnostic("expected ';'", _current);

        var node = new BcsVariableDeclaration { TypeKeyword = typeKeyword, Line = start.Line, Column = start.Column };
        node.DeclaratorTokens.AddRange(declarators);
        node.DeclaratorNames.AddRange(scanner.Names);
        return node;
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

        scanner.Observe(_current, 0); // the opening '{' itself - EnumMember mode's trigger, so the very first member (right after it) matches rule A
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

            if (_current.Type is BcsTokenType.OpenCurly or BcsTokenType.OpenParen or BcsTokenType.OpenSquare) depth++;
            else if (_current.Type is BcsTokenType.CloseCurly or BcsTokenType.CloseParen or BcsTokenType.CloseSquare) depth--;

            if (depth > 0) scanner.Observe(_current, depth);
            else { endLine = _current.Line; endColumn = _current.Column; }

            Advance();
        }

        return (scanner.Names.ToList(), startLine, startColumn, endLine, endColumn);
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
