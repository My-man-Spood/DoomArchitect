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
public sealed class BcsParser
{
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
            case "region":
            case "endregion":
                // Not modeled as their own node yet (no macro-expansion support) - skip to end of line, which NextSignificantToken's default (not including newlines) would otherwise swallow, so ask for it explicitly here.
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
    /// grammar-position knowledge this pass deliberately doesn't build.
    /// </summary>
    private BcsNode ParseScript()
    {
        var start = _current;
        Advance(); // 'script'

        var number = _current.Value;
        if (_current.Type is BcsTokenType.LitString or BcsTokenType.LitDecimal or BcsTokenType.Identifier) Advance();
        else AddDiagnostic("expected a script number or name", _current);

        string? parenGroup = null;
        if (_current.Type == BcsTokenType.OpenParen) parenGroup = SkipBalancedParens();

        var flags = new List<string>();
        while (_current.Type == BcsTokenType.Identifier)
        {
            flags.Add(_current.Value);
            Advance();
        }

        var bodyToken = _current;
        SkipBracedBlock();

        var node = new BcsScriptDeclaration
        {
            Number = number,
            TypeKeyword = parenGroup,
            BodyLine = bodyToken.Line,
            BodyColumn = bodyToken.Column,
            Line = start.Line,
            Column = start.Column,
        };
        node.FlagTokens.AddRange(flags);
        return node;
    }

    private BcsNode ParseSpecial()
    {
        var start = _current;
        Advance(); // 'special'

        var headerTokens = new List<string>();
        while (_current.Type is not (BcsTokenType.Semicolon or BcsTokenType.EndOfInput))
        {
            headerTokens.Add(_current.Value);
            Advance();
        }

        if (_current.Type == BcsTokenType.Semicolon) Advance();
        else AddDiagnostic("expected ';'", _current);

        var node = new BcsSpecialDeclaration { Line = start.Line, Column = start.Column };
        node.HeaderTokens.AddRange(headerTokens);
        return node;
    }

    private BcsNode ParseFunction()
    {
        var start = _current;
        Advance(); // 'function'

        var headerTokens = new List<string>();
        while (_current.Type is not (BcsTokenType.OpenCurly or BcsTokenType.Semicolon or BcsTokenType.EndOfInput))
        {
            headerTokens.Add(_current.Value);
            Advance();
        }

        if (_current.Type == BcsTokenType.OpenCurly) SkipBracedBlock();
        else if (_current.Type == BcsTokenType.Semicolon) Advance(); // a forward declaration
        else AddDiagnostic("expected '{' or ';'", _current);

        var node = new BcsFunctionDeclaration { Line = start.Line, Column = start.Column };
        node.HeaderTokens.AddRange(headerTokens);
        return node;
    }

    private BcsNode ParseEnum()
    {
        var start = _current;
        Advance(); // 'enum'

        string? name = null;
        if (_current.Type == BcsTokenType.Identifier) { name = _current.Value; Advance(); }

        SkipBracedBlock();
        if (_current.Type == BcsTokenType.Semicolon) Advance();

        return new BcsEnumDeclaration { Name = name, Line = start.Line, Column = start.Column };
    }

    private BcsNode ParseVariableDeclaration()
    {
        var start = _current;
        var typeKeyword = _current.Value;
        Advance();

        var declarators = new List<string>();
        var depth = 0;
        while (_current.Type != BcsTokenType.EndOfInput && !(depth == 0 && _current.Type == BcsTokenType.Semicolon))
        {
            if (_current.Type is BcsTokenType.OpenCurly or BcsTokenType.OpenParen or BcsTokenType.OpenSquare) depth++;
            else if (_current.Type is BcsTokenType.CloseCurly or BcsTokenType.CloseParen or BcsTokenType.CloseSquare) depth--;
            declarators.Add(_current.Value);
            Advance();
        }

        if (_current.Type == BcsTokenType.Semicolon) Advance();
        else AddDiagnostic("expected ';'", _current);

        var node = new BcsVariableDeclaration { TypeKeyword = typeKeyword, Line = start.Line, Column = start.Column };
        node.DeclaratorTokens.AddRange(declarators);
        return node;
    }

    /// <summary>Consumes a balanced "{ ... }" block, counting nested braces - the technique <c>ZScriptParser.SkipBlock</c> uses, reimplemented directly against <see cref="BcsTokenizer"/> (see this class's own remarks on why that's not inherited).</summary>
    private void SkipBracedBlock()
    {
        if (_current.Type != BcsTokenType.OpenCurly)
        {
            AddDiagnostic($"expected '{{', got '{_current.Value}'", _current);
            return;
        }

        Advance();
        var depth = 1;
        while (depth > 0)
        {
            if (_current.Type == BcsTokenType.EndOfInput)
            {
                AddDiagnostic("unexpected end of file inside a block", _current);
                return;
            }

            if (_current.Type == BcsTokenType.OpenCurly) depth++;
            else if (_current.Type == BcsTokenType.CloseCurly) depth--;
            Advance();
        }
    }

    /// <summary>Consumes a balanced "( ... )" group, returning its raw inner text - used wherever this pass doesn't need to interpret the contents, only skip past them correctly (see <see cref="ParseScript"/>).</summary>
    private string SkipBalancedParens()
    {
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

            sb.Append(_current.Value).Append(' ');
            Advance();
        }

        return sb.ToString().TrimEnd();
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
