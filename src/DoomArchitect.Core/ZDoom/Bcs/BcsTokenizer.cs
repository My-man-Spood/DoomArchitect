using System.Text;
using DoomArchitect.Core.ZDoom; // for doc-comment <see cref="ZScriptTokenizer"/> references only

namespace DoomArchitect.Core.ZDoom.Bcs;

/// <summary>Marks a <see cref="BcsTokenType"/> value with its literal source text (an operator, punctuation mark, or one of the real compiler's reserved words) so <see cref="BcsTokenizer"/> can build its named-token lookup by reflection - same mechanism as <see cref="ZScriptTokenString"/>/<see cref="ZScriptTokenizer"/>.</summary>
public sealed class BcsTokenString : Attribute
{
    public string Value { get; }

    public BcsTokenString(string value)
    {
        Value = value;
    }
}

/// <summary>
/// Every member here is traceable to a real <c>TK_*</c> entry in zt-bcc's
/// own <c>enum tk</c> (<c>src/parse/phase.h</c>, dev0.8.1) - confirmed
/// against that source directly, not assumed from general ACS/C knowledge.
/// Trailing comments give the real name for exactly that reason.
///
/// Members are split into two tiers, and that split is load-bearing - read
/// before touching either tier:
///
/// <b>Tokenizer-level</b> (everything above <see cref="TypeName"/>,
/// inclusive): every punctuation mark/operator, every literal kind, and
/// the real compiler's exact 53-entry reserved-word table (verified
/// directly from <c>src/parse/token/user.c</c>'s binary-searched
/// <c>table[]</c> - *not* every keyword-shaped <c>TK_*</c> entry belongs
/// here, only these 53 do). <see cref="BcsTokenizer"/> only ever produces
/// these (plus <see cref="Identifier"/>).
///
/// <b>Parser-level only</b> (everything below <see cref="TypeName"/>):
/// exists purely so <see cref="BcsParser"/> can name these with 1:1
/// traceability to their real <c>TK_*</c> identity. <see cref="BcsTokenizer"/>
/// NEVER produces these - confirmed absent from the real reserved-word
/// table, meaning the real compiler itself treats `open`/`print`/`event`/
/// `kill`/`library`/etc. as plain identifiers at the lexer level and only
/// recognizes them by literal text at specific grammar positions (e.g.
/// `open` is also a legal ordinary variable name outside a script-flag
/// position). A tokenizer change that "promotes" one of these to its own
/// recognized token would be a real regression against the actual
/// compiler's grammar, not a fix - don't.
///
/// Deliberately not modeled at all (no enum member): the 8 real
/// preprocessor-pseudo tokens (<c>TK_NONE</c>/<c>TK_STRINGIZE</c>/
/// <c>TK_PLACEMARKER</c>/<c>TK_PROCESSEDHASH</c>/<c>TK_LINKLIBRARY</c>/
/// <c>TK_ENDMACRO</c>/<c>TK_ENDMACROARG</c>/<c>TK_MACRONAME</c>) and
/// <c>TK_RESERVED</c> itself - no macro-expansion support exists yet.
/// <c>TK_SPACE</c>/<c>TK_TAB</c> aren't modeled separately from
/// <see cref="Whitespace"/> either - confirmed (<c>user.c</c>'s own
/// `read_token`) that the real compiler's normal lexing path only ever
/// produces <c>TK_HORZSPACE</c> for a run of spaces/tabs; the other two
/// are internal to macro-stringification, irrelevant here.
/// </summary>
public enum BcsTokenType
{
    // generic
    Identifier,
    Whitespace, // TK_HORZSPACE - a run of spaces/tabs, one token
    LineComment,
    BlockComment,
    Invalid,
    EndOfInput, // TK_END

    LitDecimal, // TK_LIT_DECIMAL
    LitOctal,   // TK_LIT_OCTAL - real explicit "0o"/"0O" prefix only; a bare leading zero is decimal, never octal (confirmed divergence from both C and this project's own ZScriptTokenizer)
    LitHex,     // TK_LIT_HEX
    LitBinary,  // TK_LIT_BINARY
    LitFixed,   // TK_LIT_FIXED
    LitRadix,   // TK_LIT_RADIX - "<base>r<digits>" / "<base>_<digits>", e.g. "16rff" or "16_ff"
    LitString,  // TK_LIT_STRING
    LitChar,    // TK_LIT_CHAR

    [BcsTokenString("\n")] Newline, // TK_NL - a real, significant token; never discarded as whitespace

    // punctuation
    [BcsTokenString("[")] OpenSquare,   // TK_BRACKET_L
    [BcsTokenString("]")] CloseSquare,  // TK_BRACKET_R
    [BcsTokenString("(")] OpenParen,    // TK_PAREN_L
    [BcsTokenString(")")] CloseParen,   // TK_PAREN_R
    [BcsTokenString("{")] OpenCurly,    // TK_BRACE_L
    [BcsTokenString("}")] CloseCurly,   // TK_BRACE_R
    [BcsTokenString(".")] Dot,          // TK_DOT - BCS has no leading-dot float literal (".5" is Dot then an integer, never its own number) and no ".." concat operator, unlike this project's ZScriptTokenizer
    [BcsTokenString("++")] Increment,   // TK_INC
    [BcsTokenString("--")] Decrement,   // TK_DEC
    [BcsTokenString(",")] Comma,        // TK_COMMA
    [BcsTokenString(":")] Colon,        // TK_COLON
    [BcsTokenString(";")] Semicolon,    // TK_SEMICOLON
    [BcsTokenString("?")] Questionmark, // TK_QUESTION_MARK
    [BcsTokenString("::")] DoubleColon, // TK_COLONCOLON
    [BcsTokenString("#")] Hash,         // TK_HASH
    [BcsTokenString("##")] HashHash,    // TK_HASHHASH
    [BcsTokenString("@")] At,           // TK_AT
    [BcsTokenString("...")] Ellipsis,   // TK_ELLIPSIS
    [BcsTokenString("\\")] Backslash,   // TK_BACKSLASH

    // assignment
    [BcsTokenString("=")] OpAssign,             // TK_ASSIGN
    [BcsTokenString("+=")] OpAssignAdd,         // TK_ASSIGN_ADD
    [BcsTokenString("-=")] OpAssignSubtract,    // TK_ASSIGN_SUB
    [BcsTokenString("*=")] OpAssignMultiply,    // TK_ASSIGN_MUL
    [BcsTokenString("/=")] OpAssignDivide,      // TK_ASSIGN_DIV
    [BcsTokenString("%=")] OpAssignMod,         // TK_ASSIGN_MOD
    [BcsTokenString("<<=")] OpAssignLeftShift,  // TK_ASSIGN_SHIFT_L
    [BcsTokenString(">>=")] OpAssignRightShift, // TK_ASSIGN_SHIFT_R
    [BcsTokenString("&=")] OpAssignBitAnd,      // TK_ASSIGN_BIT_AND
    [BcsTokenString("^=")] OpAssignBitXor,      // TK_ASSIGN_BIT_XOR
    [BcsTokenString("|=")] OpAssignBitOr,       // TK_ASSIGN_BIT_OR

    // comparison
    [BcsTokenString("==")] OpEquals,         // TK_EQ
    [BcsTokenString("!=")] OpNotEquals,      // TK_NEQ
    [BcsTokenString("<")] OpLessThan,        // TK_LT
    [BcsTokenString("<=")] OpLessOrEqual,    // TK_LTE
    [BcsTokenString(">")] OpGreaterThan,     // TK_GT
    [BcsTokenString(">=")] OpGreaterOrEqual, // TK_GTE

    // logical / bitwise - kept distinct (unlike this project's ZScriptTokenType, which never needed the logical forms): BCS's real table has TK_LOG_AND/TK_LOG_OR genuinely separate from TK_BIT_AND/TK_BIT_OR
    [BcsTokenString("!")] OpLogicalNot, // TK_LOG_NOT
    [BcsTokenString("&&")] OpLogicalAnd, // TK_LOG_AND
    [BcsTokenString("||")] OpLogicalOr,  // TK_LOG_OR
    [BcsTokenString("&")] OpBitAnd,      // TK_BIT_AND
    [BcsTokenString("|")] OpBitOr,       // TK_BIT_OR
    [BcsTokenString("^")] OpBitXor,      // TK_BIT_XOR
    [BcsTokenString("~")] OpBitNot,      // TK_BIT_NOT - no "~=" compound form exists in the real table, unlike this project's ZScriptTokenType's OpAssignNegate

    // arithmetic
    [BcsTokenString("+")] OpAdd,        // TK_PLUS
    [BcsTokenString("-")] OpSubtract,   // TK_MINUS
    [BcsTokenString("/")] OpDivide,     // TK_SLASH
    [BcsTokenString("*")] OpMultiply,   // TK_STAR
    [BcsTokenString("%")] OpMod,        // TK_MOD
    [BcsTokenString("<<")] OpLeftShift, // TK_SHIFT_L
    [BcsTokenString(">>")] OpRightShift,// TK_SHIFT_R

    // keywords - the real compiler's exact 53-entry reserved-word table (src/parse/token/user.c), alphabetical as in that source
    [BcsTokenString("assert")] Assert,
    [BcsTokenString("auto")] Auto,
    [BcsTokenString("bool")] Bool,
    [BcsTokenString("break")] Break,
    [BcsTokenString("buildmsg")] Buildmsg,
    [BcsTokenString("case")] Case,
    [BcsTokenString("char")] Char,
    [BcsTokenString("const")] Const,
    [BcsTokenString("continue")] Continue,
    [BcsTokenString("createtranslation")] PalTrans, // TK_PALTRANS
    [BcsTokenString("default")] Default,
    [BcsTokenString("do")] Do,
    [BcsTokenString("else")] Else,
    [BcsTokenString("enum")] Enum,
    [BcsTokenString("extern")] Extern,
    [BcsTokenString("false")] False,
    [BcsTokenString("fixed")] Fixed,
    [BcsTokenString("for")] For,
    [BcsTokenString("foreach")] Foreach,
    [BcsTokenString("function")] Function,
    [BcsTokenString("global")] Global,
    [BcsTokenString("goto")] Goto,
    [BcsTokenString("if")] If,
    [BcsTokenString("int")] Int,
    [BcsTokenString("internal")] Internal,
    [BcsTokenString("lengthof")] Lengthof,
    [BcsTokenString("let")] Let,
    [BcsTokenString("memcpy")] Memcpy,
    [BcsTokenString("module")] Module,
    [BcsTokenString("namespace")] Namespace,
    [BcsTokenString("null")] Null,
    [BcsTokenString("private")] Private,
    [BcsTokenString("raw")] Raw,
    [BcsTokenString("restart")] Restart,
    [BcsTokenString("return")] Return,
    [BcsTokenString("script")] Script,
    [BcsTokenString("special")] Special,
    [BcsTokenString("static")] Static,
    [BcsTokenString("str")] Str,
    [BcsTokenString("strcpy")] Strcpy,
    [BcsTokenString("strict")] Strict,
    [BcsTokenString("struct")] Struct,
    [BcsTokenString("suspend")] Suspend,
    [BcsTokenString("switch")] Switch,
    [BcsTokenString("terminate")] Terminate,
    [BcsTokenString("true")] True,
    [BcsTokenString("typedef")] Typedef,
    [BcsTokenString("until")] Until,
    [BcsTokenString("upmost")] Upmost,
    [BcsTokenString("using")] Using,
    [BcsTokenString("void")] Void,
    [BcsTokenString("while")] While,
    [BcsTokenString("world")] World,

    /// <summary>TK_TYPENAME - no literal text/table lookup; recognized by suffix rule alone (see <see cref="BcsTokenizer"/>'s identifier handling), checked before the reserved-word table and skipping it entirely when it matches.</summary>
    TypeName,

    // ===================================================================
    // Parser-level only below this line - BcsTokenizer never returns any
    // of these; see this enum's own class doc comment for why.
    // ===================================================================

    Open, Respawn, Death, Enter, Pickup, BlueReturn, RedReturn, WhiteReturn, Lightning,
    Disconnect, Unloading, Clientside, Net, // script-flag words
    Print, PrintBold, HudMessage, HudMessageBold, StrParam, Log, // print-family statement keywords
    Reopen, Kill, Event, Busy,
    WadAuthor, NoWadAuthor, NoCompact, Library, EncryptStrings,
    Include, Define, LibDefine, Import, Region, EndRegion, // # directive names
    NamespaceName, FunctionName, ScriptName, // __NAMESPACE__/__FUNCTION__/__SCRIPT__
    AcsExecuteWait, AcsNamedExecuteWait,
}

public sealed class BcsToken
{
    public BcsTokenType Type { get; internal set; }

    /// <summary>
    /// The token's text. For an <see cref="BcsTokenType.Identifier"/> or a
    /// reserved word recognized from one, this is already lowercased -
    /// matching the real compiler's own case-folding (confirmed in
    /// <c>user.c</c>) - so it is NOT the original source spelling.
    /// </summary>
    public string Value { get; internal set; } = string.Empty;

    public int IntValue { get; internal set; }
    public double DoubleValue { get; internal set; }
    public bool IsValid { get; internal set; } = true;
    public int Line { get; internal set; }
    public int Column { get; internal set; }

    /// <summary>
    /// The token's real source span in columns - set generically by
    /// <see cref="BcsTokenizer.ReadToken"/> after dispatch, not by
    /// whichever <c>ReadXxx</c> method produced this token. Deliberately
    /// NOT inferred from <see cref="Value"/>'s length by callers: for a
    /// numeric literal with a prefix ("0x1F") or digit separators
    /// ("1'000") stripped out of <see cref="Value"/>, or an identifier
    /// case-folded into it, <c>Value.Length</c> can be shorter than what
    /// actually appeared in the source.
    /// </summary>
    public int Length { get; internal set; }

    public override string ToString() => $"<Token.{Type} ({Value})>";
}

/// <summary>
/// A pull-based BCS lexer, built directly against zt-bcc's own real lexer
/// (<c>src/parse/token/source.c</c>/<c>user.c</c>, dev0.8.1) rather than
/// general ACS/C-family assumptions - every behavior called out below was
/// read from that source during this feature's planning, not guessed.
///
/// Deliberately diverges from this project's own <see cref="ZScriptTokenizer"/>
/// precedent in two ways, both intentional:
/// <list type="bullet">
/// <item>Tracks <see cref="BcsToken.Line"/>/<see cref="BcsToken.Column"/>
/// incrementally while lexing, rather than <c>ZScriptTokenizer</c>'s
/// constructor-time newline-offset prescan + after-the-fact lookup - that
/// approach fits UDB's rare one-shot "something's broken" use; this
/// tokenizer is meant to be rebuilt on every keystroke by an LSP, where
/// upfront incremental tracking is both simpler and cheaper.</item>
/// <item>Case-folds every identifier's <see cref="BcsToken.Value"/> to
/// lowercase before reserved-word lookup - the real compiler does this
/// too (<c>user.c</c>'s own <c>tolower</c> loop), so BCS identifiers,
/// keywords included, are genuinely case-insensitive; <c>ZScriptTokenizer</c>
/// is case-preserving because real ZScript is case-sensitive - not a
/// copy-paste gap.</item>
/// </list>
///
/// Unlike <c>ZScriptTokenizer</c>, there's no <c>ExpectToken(params[])</c>
/// disambiguating entry point - BCS's lexical grammar is fully
/// deterministic per current character (confirmed by reading the real
/// dispatch directly), so a caller that wants a specific kind just calls
/// <see cref="ReadToken"/> and checks <see cref="BcsToken.Type"/> itself.
///
/// Whitespace/comments ARE returned as real tokens (never silently
/// consumed here) and so is <see cref="BcsTokenType.Newline"/> - matching
/// the real compiler's own <c>p_read_source</c>, which always produces
/// them. What the real compiler's higher-level <c>read_token</c> does
/// next - skip horizontal space and (usually) newlines, unless the
/// parser's current grammar position specifically wants to see a newline
/// (its own <c>create_nltk</c> flag) - is <see cref="NextSignificantToken"/>'s
/// job here, not this type's: callers decide per call whether newlines
/// matter, the same contextual decision the real parser makes.
/// </summary>
public sealed class BcsTokenizer
{
    private static readonly Dictionary<string, BcsTokenType> NamedTokenTypes;
    private static readonly List<string> NamedTokenTypesOrder; // longest-text-first, so "<<=" matches before "<<" before "<"
    /// <summary>Every tokenizer-level reserved word (the real compiler's exact 53-entry table) - exposed so e.g. a syntax highlighter can style keywords without duplicating this list.</summary>
    public static readonly HashSet<BcsTokenType> ReservedWordTypes;

    static BcsTokenizer()
    {
        NamedTokenTypes = new Dictionary<string, BcsTokenType>();
        NamedTokenTypesOrder = new List<string>();
        ReservedWordTypes = new HashSet<BcsTokenType>();

        foreach (var tokenType in Enum.GetValues<BcsTokenType>())
        {
            var field = typeof(BcsTokenType).GetField(tokenType.ToString())!;
            var attrs = (BcsTokenString[])field.GetCustomAttributes(typeof(BcsTokenString), false);
            if (attrs.Length == 0) continue;

            NamedTokenTypes.Add(attrs[0].Value, tokenType);
            NamedTokenTypesOrder.Add(attrs[0].Value);
            // A reserved word's literal text is all-lowercase-letters; punctuation never is - this is how ReadIdentifier tells "could this identifier actually be one of the reserved words" apart from "just register this as a candidate for the generic punctuation scan too" without a second, separately-maintained list.
            if (attrs[0].Value.Length > 0 && char.IsLower(attrs[0].Value[0])) ReservedWordTypes.Add(tokenType);
        }

        NamedTokenTypesOrder.Sort((a, b) => b.Length - a.Length);
    }

    private readonly BinaryReader _reader;
    private readonly List<BcsDiagnostic> _diagnostics;
    private readonly StringBuilder _sb = new();
    private int _current;
    private int _line = 1;
    private int _column = 1;

    public BcsTokenizer(BinaryReader reader, List<BcsDiagnostic> diagnostics)
    {
        _reader = reader;
        _diagnostics = diagnostics;
        _current = ReadRaw();
    }

    private int ReadRaw()
    {
        if (_reader.PeekChar() == -1) return -1;
        try { return _reader.ReadChar(); }
        catch (EndOfStreamException) { return -1; }
    }

    /// <summary>The character immediately after <see cref="_current"/> - already sitting in the stream, since <see cref="_current"/> itself was already consumed from it.</summary>
    private int PeekNext() => _reader.PeekChar();

    private int Advance()
    {
        var c = _current;
        if (c == '\n') { _line++; _column = 1; }
        else if (c != -1) { _column++; }
        _current = ReadRaw();
        return c;
    }

    private void AddDiagnostic(string message, int line, int column, BcsDiagnosticSeverity severity = BcsDiagnosticSeverity.Error) =>
        _diagnostics.Add(new BcsDiagnostic(message, line, column, severity));

    /// <summary>Skips <see cref="BcsTokenType.Whitespace"/>/<see cref="BcsTokenType.LineComment"/>/<see cref="BcsTokenType.BlockComment"/> always, and <see cref="BcsTokenType.Newline"/> only when <paramref name="includeNewlines"/> is false - mirrors the real compiler's own <c>read_token</c> skip-loop, where newline significance is a per-call-site grammar decision, not a tokenizer-wide one.</summary>
    public BcsToken NextSignificantToken(bool includeNewlines = false)
    {
        while (true)
        {
            var token = ReadToken();
            if (token.Type is BcsTokenType.Whitespace or BcsTokenType.LineComment or BcsTokenType.BlockComment) continue;
            if (!includeNewlines && token.Type == BcsTokenType.Newline) continue;
            return token;
        }
    }

    /// <summary>
    /// Sets <see cref="BcsToken.Length"/> (in columns) generically here,
    /// rather than inside each <c>ReadXxx</c> method - it's just "how far
    /// did the cursor move", which every token kind shares, and several
    /// kinds (numeric literals with a prefix like "0x" or digit
    /// separators stripped out of <see cref="BcsToken.Value"/>) would
    /// otherwise under-report their real source span if a caller inferred
    /// it from <c>Value.Length</c> instead (a real gap this fixes - see
    /// <c>BcsSyntaxHighlighter</c>, the first consumer that needed it).
    /// Falls back to <c>Value.Length</c> only for the rare token that
    /// spans multiple lines (an unterminated/multi-line block comment),
    /// where "how many columns" isn't a meaningful single number anyway.
    /// </summary>
    public BcsToken ReadToken()
    {
        var line = _line;
        var column = _column;
        var token = ReadTokenCore(line, column);
        token.Length = token.Line == _line ? _column - column : token.Value.Length;
        return token;
    }

    private BcsToken ReadTokenCore(int line, int column)
    {
        if (_current == -1) return new BcsToken { Type = BcsTokenType.EndOfInput, Line = line, Column = column };

        if (_current is ' ' or '\t') return ReadWhitespace(line, column);
        if (_current == '\n') { Advance(); return new BcsToken { Type = BcsTokenType.Newline, Value = "\n", Line = line, Column = column }; }
        if (char.IsLetter((char)_current) || _current == '_') return ReadIdentifier(line, column);
        if (_current is >= '0' and <= '9') return ReadNumber(line, column);
        if (_current == '"') return ReadString(line, column);
        if (_current == '\'') return ReadChar(line, column);
        if (_current == '/') return ReadSlashOrComment(line, column);

        return ReadNamedToken(line, column);
    }

    private BcsToken ReadWhitespace(int line, int column)
    {
        _sb.Length = 0;
        while (_current is ' ' or '\t') { _sb.Append((char)_current); Advance(); }
        return new BcsToken { Type = BcsTokenType.Whitespace, Value = _sb.ToString(), Line = line, Column = column };
    }

    /// <summary>
    /// Collects the identifier, checks the real <see cref="BcsTokenType.TypeName"/>
    /// suffix rule against the ORIGINAL (pre-lowercasing) text - confirmed
    /// exact wording from <c>user.c</c>: the second-to-last character is a
    /// lowercase letter or <c>_</c> and the last is <c>T</c>, or the whole
    /// identifier is just <c>"T"</c> - then lowercases, then (skipping
    /// entirely if it was a type name) checks the real 53-entry reserved-
    /// word table.
    /// </summary>
    private BcsToken ReadIdentifier(int line, int column)
    {
        _sb.Length = 0;
        while (char.IsLetterOrDigit((char)_current) || _current == '_')
        {
            _sb.Append((char)_current);
            Advance();
        }

        var raw = _sb.ToString();
        var isTypeName = raw.Length == 1 && raw[0] == 'T' ||
            raw.Length >= 2 && (char.IsLower(raw[^2]) || raw[^2] == '_') && raw[^1] == 'T';

        var lowered = raw.ToLowerInvariant();
        if (isTypeName) return new BcsToken { Type = BcsTokenType.TypeName, Value = lowered, Line = line, Column = column };

        if (NamedTokenTypes.TryGetValue(lowered, out var keyword) && ReservedWordTypes.Contains(keyword))
            return new BcsToken { Type = keyword, Value = lowered, Line = line, Column = column };

        return new BcsToken { Type = BcsTokenType.Identifier, Value = lowered, Line = line, Column = column };
    }

    /// <summary>
    /// Ported faithfully from the real <c>zero:</c>/<c>decimal:</c>/
    /// <c>fixedpoint:</c>/<c>radix:</c>/<c>binary:</c>/<c>hexadecimal:</c>/
    /// <c>octal:</c> states - six distinct literal kinds, a digit
    /// separator (<c>'</c>) accepted mid-literal in every one of them, and
    /// a deliberately asymmetric diagnostic severity: an empty binary/
    /// octal/decimal literal is fatal, while an empty hex literal, an
    /// empty fixed-point fraction, or empty radix digits only warns and
    /// substitutes <c>0</c>. Explicit "0o"/"0O"/"0b"/"0B"/"0x"/"0X" prefixes
    /// select octal/binary/hex; anything else after a leading zero - more
    /// digits, a <c>.</c>, or <c>r</c>/<c>R</c>/<c>_</c> - falls through to
    /// plain decimal/fixed-point/radix instead (so "010" is decimal 10,
    /// never octal 8 - a real, confirmed divergence from both C and this
    /// project's own <see cref="ZScriptTokenizer"/>).
    /// </summary>
    private BcsToken ReadNumber(int line, int column)
    {
        if (_current == '0')
        {
            Advance();
            switch (_current)
            {
                case 'b' or 'B': Advance(); return ReadRadixDigits(line, column, BcsTokenType.LitBinary, "binary", c => c is '0' or '1');
                case 'x' or 'X': Advance(); return ReadHex(line, column);
                case 'o' or 'O': Advance(); return ReadRadixDigits(line, column, BcsTokenType.LitOctal, "octal", c => c is >= '0' and <= '7');
                case '.': Advance(); return ReadFixedPoint(line, column, "0.");
                case 'r' or 'R' or '_': return ReadRadixLiteral(line, column, "0");
                default: return ReadZeroPrefixed(line, column);
            }
        }

        return ReadDecimal(line, column, "");
    }

    /// <summary>The "zero:" state - swallows any further leading zeros (a digit separator before one of them is allowed too), then decides decimal/fixed-point/radix/plain-zero exactly like the real compiler.</summary>
    private BcsToken ReadZeroPrefixed(int line, int column)
    {
        while (_current == '0' || (_current == '\'' && PeekNext() == '0')) Advance();

        if (_current is >= '0' and <= '9') return ReadDecimal(line, column, "");
        if (_current == '\'') return ReadDecimal(line, column, "");
        if (_current == '.') { Advance(); return ReadFixedPoint(line, column, "0."); }
        if (_current is 'r' or 'R' or '_') return ReadRadixLiteral(line, column, "0");

        return new BcsToken { Type = BcsTokenType.LitDecimal, Value = "0", IntValue = 0, DoubleValue = 0, Line = line, Column = column };
    }

    private BcsToken ReadDecimal(int line, int column, string prefix)
    {
        _sb.Length = 0;
        _sb.Append(prefix);
        while (true)
        {
            if (_current is >= '0' and <= '9') { _sb.Append((char)_current); Advance(); }
            else if (_current == '\'')
            {
                Advance();
                if (_current is not (>= '0' and <= '9')) { AddDiagnostic("missing decimal digit after digit separator", _line, _column); break; }
            }
            else if (_current == '.') { Advance(); return ReadFixedPoint(line, column, _sb + "."); }
            else if (_current is 'r' or 'R' or '_') return ReadRadixLiteral(line, column, _sb.ToString());
            else if (char.IsLetter((char)Math.Max(_current, 0))) { AddDiagnostic("invalid digit in decimal literal", _line, _column); Advance(); }
            else break;
        }

        var text = _sb.ToString();
        var value = text.Length == 0 ? 0 : int.Parse(text);
        return new BcsToken { Type = BcsTokenType.LitDecimal, Value = text, IntValue = value, DoubleValue = value, Line = line, Column = column };
    }

    private BcsToken ReadFixedPoint(int line, int column, string prefix)
    {
        _sb.Length = 0;
        _sb.Append(prefix);
        while (true)
        {
            if (_current is >= '0' and <= '9') { _sb.Append((char)_current); Advance(); }
            else if (_current == '\'')
            {
                Advance();
                if (_current is not (>= '0' and <= '9')) { AddDiagnostic("missing decimal digit after digit separator", _line, _column); break; }
            }
            else if (char.IsLetter((char)Math.Max(_current, 0))) { AddDiagnostic("invalid digit in fractional part of fixed-point literal", _line, _column); Advance(); }
            else break;
        }

        if (_sb[^1] == '.')
        {
            AddDiagnostic($"fixed-point literal has no digits after point, will interpret it as {_sb}0", line, column, BcsDiagnosticSeverity.Warning);
            _sb.Append('0');
        }

        var text = _sb.ToString();
        var value = double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        return new BcsToken { Type = BcsTokenType.LitFixed, Value = text, DoubleValue = value, IntValue = (int)value, Line = line, Column = column };
    }

    /// <summary>"<base>r<digits>"/"<base>_<digits>" - the separator character is always normalized to <c>_</c> in <see cref="BcsToken.Value"/>, matching the real compiler's own normalization.</summary>
    private BcsToken ReadRadixLiteral(int line, int column, string baseDigits)
    {
        Advance(); // the 'r'/'R'/'_' separator itself
        _sb.Length = 0;
        _sb.Append(baseDigits).Append('_');

        if (!char.IsLetterOrDigit((char)Math.Max(_current, 0)) && _current != '\'')
            AddDiagnostic($"radix literal has no digits after '{(_sb[^1] == 'r' ? "r" : "underscore")}', will interpret it as {_sb}0", line, column, BcsDiagnosticSeverity.Warning);

        while (true)
        {
            if (char.IsLetterOrDigit((char)Math.Max(_current, 0))) { _sb.Append(char.ToLowerInvariant((char)_current)); Advance(); }
            else if (_current == '\'')
            {
                Advance();
                if (!char.IsLetterOrDigit((char)Math.Max(_current, 0))) { AddDiagnostic("missing digit after digit separator", _line, _column); break; }
            }
            else break;
        }

        if (_sb[^1] == '_') _sb.Append('0');
        return new BcsToken { Type = BcsTokenType.LitRadix, Value = _sb.ToString(), Line = line, Column = column };
    }

    private BcsToken ReadHex(int line, int column)
    {
        _sb.Length = 0;
        while (true)
        {
            if (Uri.IsHexDigit((char)Math.Max(_current, 0))) { _sb.Append((char)_current); Advance(); }
            else if (_current == '\'')
            {
                Advance();
                if (!Uri.IsHexDigit((char)Math.Max(_current, 0))) { AddDiagnostic("missing hexadecimal digit after digit separator", _line, _column); break; }
            }
            else if (char.IsLetterOrDigit((char)Math.Max(_current, 0))) { AddDiagnostic("invalid digit in hexadecimal literal", _line, _column); Advance(); }
            else break;
        }

        if (_sb.Length == 0)
        {
            AddDiagnostic("hexadecimal literal has no digits, will interpret it as 0x0", line, column, BcsDiagnosticSeverity.Warning);
            _sb.Append('0');
        }

        var text = _sb.ToString();
        var value = Convert.ToInt32(text, 16);
        return new BcsToken { Type = BcsTokenType.LitHex, Value = text, IntValue = value, DoubleValue = value, Line = line, Column = column };
    }

    private BcsToken ReadRadixDigits(int line, int column, BcsTokenType type, string kindName, Func<int, bool> isDigit)
    {
        _sb.Length = 0;
        while (true)
        {
            if (isDigit(_current)) { _sb.Append((char)_current); Advance(); }
            else if (_current == '\'')
            {
                Advance();
                if (!isDigit(_current)) { AddDiagnostic($"missing {kindName} digit after digit separator", _line, _column); break; }
            }
            else if (char.IsLetterOrDigit((char)Math.Max(_current, 0))) { AddDiagnostic($"invalid digit in {kindName} literal", _line, _column); Advance(); }
            else break;
        }

        if (_sb.Length == 0) AddDiagnostic($"{kindName} literal has no digits", line, column);

        var text = _sb.ToString();
        var value = text.Length == 0 ? 0 : Convert.ToInt32(text, type == BcsTokenType.LitBinary ? 2 : 8);
        return new BcsToken { Type = type, Value = text, IntValue = value, DoubleValue = value, Line = line, Column = column };
    }

    /// <summary>Backslash+char stored verbatim, unprocessed - matches this project's own existing <see cref="ZScriptTokenizer"/> string-literal quirk (by coincidence, not by design - confirmed separately in the real compiler's own lexer). An unterminated string is a real, fatal diagnostic, not silent EOF.</summary>
    private BcsToken ReadString(int line, int column)
    {
        Advance(); // opening quote
        _sb.Length = 0;
        while (true)
        {
            if (_current == -1)
            {
                AddDiagnostic("unterminated string", line, column);
                return new BcsToken { Type = BcsTokenType.LitString, Value = _sb.ToString(), IsValid = false, Line = line, Column = column };
            }

            if (_current == '"') { Advance(); return new BcsToken { Type = BcsTokenType.LitString, Value = _sb.ToString(), Line = line, Column = column }; }

            if (_current == '\\')
            {
                _sb.Append('\\');
                Advance();
                if (_current != -1) { _sb.Append((char)_current); Advance(); }
                continue;
            }

            _sb.Append((char)_current);
            Advance();
        }
    }

    /// <summary>Real escape interpretation (unlike <see cref="ReadString"/>): <c>\a\b\f\n\r\t\v</c>, octal <c>\NNN</c> (&lt;=3 digits), hex <c>\xNN</c>/<c>\XNN</c> (&lt;=2 digits), <c>\\</c>, <c>\'</c> - an unrecognized escape, an empty/multi-character literal, or an unterminated literal are all real, fatal diagnostics.</summary>
    private BcsToken ReadChar(int line, int column)
    {
        Advance(); // opening quote
        if (_current == '\'' || _current == -1)
        {
            AddDiagnostic("missing character in character literal", line, column);
            return new BcsToken { Type = BcsTokenType.LitChar, IsValid = false, Line = line, Column = column };
        }

        char value;
        if (_current == '\\')
        {
            Advance();
            value = ReadCharEscape(line, column);
        }
        else
        {
            value = (char)_current;
            Advance();
        }

        if (_current != '\'')
        {
            AddDiagnostic("multiple characters in character literal", line, column);
            return new BcsToken { Type = BcsTokenType.LitChar, Value = value.ToString(), IntValue = value, IsValid = false, Line = line, Column = column };
        }

        Advance();
        return new BcsToken { Type = BcsTokenType.LitChar, Value = value.ToString(), IntValue = value, Line = line, Column = column };
    }

    private static readonly Dictionary<char, char> SingleCharEscapes = new()
    {
        ['a'] = '\a', ['b'] = '\b', ['f'] = '\f', ['n'] = '\n', ['r'] = '\r', ['t'] = '\t', ['v'] = '\v',
    };

    private char ReadCharEscape(int line, int column)
    {
        if (_current == -1) { AddDiagnostic("empty escape sequence", _line, _column); return '\0'; }

        if (_current == '\'') { var c = '\''; Advance(); return c; }

        if (SingleCharEscapes.TryGetValue((char)_current, out var single)) { Advance(); return single; }

        if (_current is >= '0' and <= '7')
        {
            var digits = new StringBuilder();
            while (_current is >= '0' and <= '7' && digits.Length < 3) { digits.Append((char)_current); Advance(); }
            var code = Convert.ToInt32(digits.ToString(), 8);
            if (code > 127) AddDiagnostic($"invalid character `\\{digits}`", line, column);
            return (char)code;
        }

        if (_current == '\\') { Advance(); return '\\'; }

        if (_current is 'x' or 'X')
        {
            Advance();
            var digits = new StringBuilder();
            while (Uri.IsHexDigit((char)Math.Max(_current, 0)) && digits.Length < 2) { digits.Append((char)_current); Advance(); }
            if (digits.Length == 0) { AddDiagnostic("empty escape sequence", line, column); return '\0'; }
            var code = Convert.ToInt32(digits.ToString(), 16);
            if (code > 127) AddDiagnostic($"invalid character `\\{digits}`", line, column);
            return (char)code;
        }

        AddDiagnostic("unknown escape sequence", line, column);
        return '\0';
    }

    private BcsToken ReadSlashOrComment(int line, int column)
    {
        Advance(); // '/'
        if (_current == '=') { Advance(); return new BcsToken { Type = BcsTokenType.OpAssignDivide, Value = "/=", Line = line, Column = column }; }
        if (_current == '/') { Advance(); return ReadLineComment(line, column); }
        if (_current == '*') { Advance(); return ReadBlockComment(line, column); }
        return new BcsToken { Type = BcsTokenType.OpDivide, Value = "/", Line = line, Column = column };
    }

    /// <summary>Stops before, not after, the trailing newline - so the <see cref="BcsTokenType.Newline"/> that follows is still its own real token.</summary>
    private BcsToken ReadLineComment(int line, int column)
    {
        _sb.Length = 0;
        while (_current != -1 && _current != '\n') { _sb.Append((char)_current); Advance(); }
        return new BcsToken { Type = BcsTokenType.LineComment, Value = _sb.ToString(), Line = line, Column = column };
    }

    /// <summary>Doesn't nest - the first "*/" closes the outermost comment, same as this project's own <see cref="ZScriptTokenizer"/>. Reaching end-of-input before it closes is a real, fatal diagnostic.</summary>
    private BcsToken ReadBlockComment(int line, int column)
    {
        _sb.Length = 0;
        while (true)
        {
            if (_current == -1)
            {
                AddDiagnostic("unterminated comment", line, column);
                return new BcsToken { Type = BcsTokenType.BlockComment, Value = _sb.ToString(), IsValid = false, Line = line, Column = column };
            }

            if (_current == '*' && PeekNext() == '/')
            {
                Advance();
                Advance();
                return new BcsToken { Type = BcsTokenType.BlockComment, Value = _sb.ToString(), Line = line, Column = column };
            }

            _sb.Append((char)_current);
            Advance();
        }
    }

    private BcsToken ReadNamedToken(int line, int column)
    {
        foreach (var text in NamedTokenTypesOrder)
        {
            if (!Matches(text)) continue;

            for (var i = 0; i < text.Length; i++) Advance();
            return new BcsToken { Type = NamedTokenTypes[text], Value = text, Line = line, Column = column };
        }

        var invalidChar = _current;
        AddDiagnostic("invalid character", line, column);
        Advance();
        return new BcsToken { Type = BcsTokenType.Invalid, Value = invalidChar == -1 ? "" : ((char)invalidChar).ToString(), IsValid = false, Line = line, Column = column };
    }

    /// <summary>Whether <paramref name="text"/> (a candidate operator/punctuation spelling) matches starting at <see cref="_current"/>, without consuming anything. The longest real BCS punctuation is 3 characters ("<![CDATA[<<=]]>", ">>=", "..."), so a one-off third-character peek (via <see cref="PeekAt"/>, since only <see cref="_current"/> and <see cref="PeekNext"/> are available as cheap lookahead) covers every case without needing a general-purpose N-char lookahead buffer.</summary>
    private bool Matches(string text)
    {
        if (text.Length == 0 || _current != text[0]) return false;
        if (text.Length == 1) return true;
        if (PeekNext() != text[1]) return false;
        if (text.Length == 2) return true;

        return text.Length == 3 && PeekAt(_reader.BaseStream.Position + 1) == text[2];
    }

    private char PeekAt(long position)
    {
        var saved = _reader.BaseStream.Position;
        _reader.BaseStream.Position = position;
        var c = (char)_reader.PeekChar();
        _reader.BaseStream.Position = saved;
        return c;
    }
}
