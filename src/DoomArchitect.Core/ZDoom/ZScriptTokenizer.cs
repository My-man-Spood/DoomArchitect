using System.Reflection;
using System.Text;

namespace DoomArchitect.Core.ZDoom;

/// <summary>Marks a <see cref="ZScriptTokenType"/> value with its literal source text (an operator, punctuation, etc.) so <see cref="ZScriptTokenizer"/> can build its named-token lookup by reflection.</summary>
public sealed class ZScriptTokenString : Attribute
{
    public string Value { get; }

    public ZScriptTokenString(string value)
    {
        Value = value;
    }
}

public enum ZScriptTokenType
{
    // generic tokens
    Identifier, // meow
    Integer, // -666
    Double, // 1.3
    String, // "..."
    Name, // '...'

    // comments
    LineComment, // // blablabla
    BlockComment, // /* blablabla */
    Whitespace, // whitespace is a legit token.
    Region, // #region, #endregion

    // invalid token
    Invalid,

    [ZScriptTokenString("#")] Preprocessor,
    [ZScriptTokenString("\n")] Newline,

    [ZScriptTokenString("{")] OpenCurly,
    [ZScriptTokenString("}")] CloseCurly,

    [ZScriptTokenString("(")] OpenParen,
    [ZScriptTokenString(")")] CloseParen,

    [ZScriptTokenString("[")] OpenSquare,
    [ZScriptTokenString("]")] CloseSquare,

    [ZScriptTokenString(".")] Dot,
    [ZScriptTokenString(",")] Comma,

    // == != < > <= >=
    [ZScriptTokenString("==")] OpEquals,
    [ZScriptTokenString("~==")] OpEqualsCaseInsensitive,
    [ZScriptTokenString("!=")] OpNotEquals,
    [ZScriptTokenString("<")] OpLessThan,
    [ZScriptTokenString(">")] OpGreaterThan,
    [ZScriptTokenString("<=")] OpLessOrEqual,
    [ZScriptTokenString(">=")] OpGreaterOrEqual,

    // ternary operator (x ? y : z), also the colon after state labels
    [ZScriptTokenString("?")] Questionmark,
    [ZScriptTokenString(":")] Colon,
    [ZScriptTokenString("::")] DoubleColon,

    // + - * / << >> ~ ^ & |
    [ZScriptTokenString("+")] OpAdd,
    [ZScriptTokenString("-")] OpSubtract,
    [ZScriptTokenString("*")] OpMultiply,
    [ZScriptTokenString("/")] OpDivide,
    [ZScriptTokenString("<<")] OpLeftShift,
    [ZScriptTokenString(">>")] OpRightShift,
    [ZScriptTokenString("~")] OpNegate,
    [ZScriptTokenString("^")] OpXor,
    [ZScriptTokenString("&")] OpAnd,
    [ZScriptTokenString("|")] OpOr,
    [ZScriptTokenString("..")] OpStringConcat,

    // = += -= *= /= <<= >>= ~= ^= &= |=
    [ZScriptTokenString("=")] OpAssign,
    [ZScriptTokenString("+=")] OpAssignAdd,
    [ZScriptTokenString("-=")] OpAssignSubtract,
    [ZScriptTokenString("*=")] OpAssignMultiply,
    [ZScriptTokenString("/=")] OpAssignDivide,
    [ZScriptTokenString("<<=")] OpAssignLeftShift,
    [ZScriptTokenString(">>=")] OpAssignRightShift,
    [ZScriptTokenString("~=")] OpAssignNegate,
    [ZScriptTokenString("^=")] OpAssignXor,
    [ZScriptTokenString("&=")] OpAssignAnd,
    [ZScriptTokenString("|=")] OpAssignOr,

    // unary: !
    [ZScriptTokenString("!")] OpUnaryNot,

    // semicolon
    [ZScriptTokenString(";")] Semicolon,
}

public sealed class ZScriptToken
{
    public ZScriptToken()
    {
        IsValid = true;
        WarningMessage = string.Empty;
    }

    public ZScriptToken(ZScriptToken other)
    {
        Type = other.Type;
        Value = other.Value;
        ValueInt = other.ValueInt;
        ValueDouble = other.ValueDouble;
        IsValid = other.IsValid;
        WarningMessage = other.WarningMessage;
        Position = other.Position;
    }

    public ZScriptTokenType Type { get; internal set; }
    public string Value { get; internal set; } = string.Empty;
    public int ValueInt { get; internal set; }
    public double ValueDouble { get; internal set; }
    public bool IsValid { get; internal set; }
    public string WarningMessage { get; internal set; }
    public long Position { get; internal set; }

    public override string ToString() => $"<Token.{Type} ({Value})>";
}

/// <summary>
/// A pull-based ZScript lexer over a <see cref="BinaryReader"/> - ported
/// near-verbatim from UDB's real <c>ZScriptTokenizer</c>, which has no
/// dependency on any of UDB's own resource/config/app-singleton types, so
/// nothing here needed adapting beyond namespace and visibility (UDB's
/// `internal` becomes `public` throughout this file - this project has no
/// <c>InternalsVisibleTo</c>, so the test project needs direct access, the
/// same reasoning already applied to <see cref="Configuration.TestLaunchCommandBuilder"/>).
/// Known, deliberately-kept UDB behaviors, not bugs: block comments don't
/// nest, and string/name literal escape sequences aren't interpreted (a
/// backslash just includes the next character verbatim) - UDB's own source
/// flags the latter as a todo it never got to.
/// </summary>
public sealed class ZScriptTokenizer
{
    private readonly BinaryReader _reader;
    // Eagerly built by a static constructor (CLR-guaranteed thread-safe,
    // run exactly once) rather than UDB's own real lazy "if null, populate"
    // check in the instance constructor - that pattern is a genuine data
    // race under any concurrent construction, latent in UDB's real single-
    // threaded WinForms context but real and reproducible here once
    // multiple tests construct a tokenizer in parallel (xUnit's default).
    // Fixed rather than faithfully reproduced: nothing about this is
    // user-observable behavior, it's a pure implementation-detail bug.
    private static readonly Dictionary<string, ZScriptTokenType> _namedTokenTypes;
    private static readonly Dictionary<ZScriptTokenType, string> _namedTokenTypesReverse;
    private static readonly List<string> _namedTokenTypesOrder;

    static ZScriptTokenizer()
    {
        _namedTokenTypes = new Dictionary<string, ZScriptTokenType>();
        _namedTokenTypesReverse = new Dictionary<ZScriptTokenType, string>();
        _namedTokenTypesOrder = new List<string>();

        var tokenTypes = Enum.GetValues(typeof(ZScriptTokenType)).Cast<ZScriptTokenType>();
        foreach (var tokenType in tokenTypes)
        {
            var fi = typeof(ZScriptTokenType).GetField(tokenType.ToString())!;
            var attrs = (ZScriptTokenString[])fi.GetCustomAttributes(typeof(ZScriptTokenString), false);
            if (attrs.Length == 0) continue;

            _namedTokenTypes.Add(attrs[0].Value, tokenType);
            _namedTokenTypesReverse.Add(tokenType, attrs[0].Value);
            _namedTokenTypesOrder.Add(attrs[0].Value);
        }

        _namedTokenTypesOrder.Sort((a, b) =>
        {
            if (a.Length > b.Length) return -1;
            if (a.Length < b.Length) return 1;
            return 0;
        });
    }
    private readonly StringBuilder _sb = new();

    public BinaryReader Reader => _reader;
    public long LastPosition { get; private set; }

    private readonly List<long> _linePositions;

    public ZScriptTokenizer(BinaryReader br)
    {
        _reader = br;

        var cpos = br.BaseStream.Position;
        _linePositions = new List<long>();
        br.BaseStream.Position = 0;
        while (br.BaseStream.Position < br.BaseStream.Length)
        {
            var b = br.ReadByte();
            if (b == '\n') _linePositions.Add(br.BaseStream.Position);
        }
        br.BaseStream.Position = cpos;
    }

    public int PositionToLine(long pos)
    {
        for (var i = 0; i < _linePositions.Count; i++)
            if (pos <= _linePositions[i])
                return i + 1;
        return _linePositions.Count;
    }

    /// <summary>Skips whitespace, newlines, comments AND <c>#region</c>/<c>#endregion</c> lines.</summary>
    public void SkipWhitespace()
    {
        while (true)
        {
            var tok = ExpectToken(ZScriptTokenType.Newline, ZScriptTokenType.BlockComment, ZScriptTokenType.LineComment, ZScriptTokenType.Whitespace, ZScriptTokenType.Region);
            if (tok == null || !tok.IsValid) break;
        }
    }

    private ZScriptToken? TryReadWhitespace()
    {
        var cpos = LastPosition = _reader.BaseStream.Position;
        if (_reader.PeekChar() == -1) return null;

        char c;
        try { c = _reader.ReadChar(); }
        catch (EndOfStreamException) { return null; }

        const string whitespace = " \r\t ";

        if (whitespace.Contains(c))
        {
            _sb.Length = 0;
            _sb.Append(c);
            while (_reader.PeekChar() != -1)
            {
                char cnext;
                try { cnext = _reader.ReadChar(); }
                catch (EndOfStreamException) { break; }

                if (whitespace.Contains(cnext))
                {
                    _sb.Append(cnext);
                    continue;
                }

                _reader.BaseStream.Position--;
                break;
            }

            return new ZScriptToken { Position = cpos, Type = ZScriptTokenType.Whitespace, Value = _sb.ToString() };
        }

        _reader.BaseStream.Position = cpos;
        return null;
    }

    private ZScriptToken? TryReadIdentifier()
    {
        var cpos = LastPosition = _reader.BaseStream.Position;
        if (_reader.PeekChar() == -1) return null;

        char c;
        try { c = _reader.ReadChar(); }
        catch (EndOfStreamException) { return null; }

        if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_')
        {
            _sb.Length = 0;
            _sb.Append(c);
            while (_reader.PeekChar() != -1)
            {
                char cnext;
                try { cnext = _reader.ReadChar(); }
                catch (EndOfStreamException) { break; }

                if ((cnext >= 'a' && cnext <= 'z') || (cnext >= 'A' && cnext <= 'Z') || cnext == '_' || (cnext >= '0' && cnext <= '9'))
                {
                    _sb.Append(cnext);
                    continue;
                }

                _reader.BaseStream.Position--;
                break;
            }

            return new ZScriptToken { Position = cpos, Type = ZScriptTokenType.Identifier, Value = _sb.ToString() };
        }

        _reader.BaseStream.Position = cpos;
        return null;
    }

    private ZScriptToken? TryReadNumber()
    {
        var cpos = LastPosition = _reader.BaseStream.Position;
        if (_reader.PeekChar() == -1) return null;

        char c;
        try { c = _reader.ReadChar(); }
        catch (EndOfStreamException) { return null; }

        if ((c >= '0' && c <= '9') || c == '.')
        {
            var isint = true;
            var isdouble = c == '.';
            var isexponent = false;
            if (isdouble)
            {
                var cnext = _reader.ReadChar();
                if (!(cnext >= '0' && cnext <= '9'))
                {
                    isint = false;
                    _reader.BaseStream.Position--;
                }
            }

            if (isint)
            {
                var isoctal = c == '0';
                var ishex = false;
                _sb.Length = 0;
                _sb.Append(c);
                while (_reader.PeekChar() != -1)
                {
                    char cnext;
                    try { cnext = _reader.ReadChar(); }
                    catch (EndOfStreamException) { break; }

                    if (!isdouble && cnext == 'x' && _sb.Length == 1)
                    {
                        isoctal = false;
                        ishex = true;
                    }
                    else if ((cnext >= '0' && cnext <= '7') ||
                             (!isoctal && cnext >= '8' && cnext <= '9') ||
                             (ishex && ((cnext >= 'a' && cnext <= 'f') || (cnext >= 'A' && cnext <= 'F'))))
                    {
                        _sb.Append(cnext);
                    }
                    else if (!ishex && !isdouble && !isexponent && cnext == '.')
                    {
                        isdouble = true;
                        isoctal = false;
                        _sb.Append('.');
                    }
                    else if (!isoctal && !ishex && !isexponent && (cnext == 'e' || cnext == 'E'))
                    {
                        isexponent = true;
                        isdouble = true;
                        _sb.Append('e');
                        if (_reader.PeekChar() == -1)
                        {
                            _reader.BaseStream.Position = cpos;
                            return null;
                        }
                        try { cnext = _reader.ReadChar(); }
                        catch (EndOfStreamException)
                        {
                            _reader.BaseStream.Position = cpos;
                            return null; // bad exponent notation
                        }
                        if (cnext == '-') _sb.Append('-');
                        else _reader.BaseStream.Position--;
                    }
                    else
                    {
                        _reader.BaseStream.Position--;
                        break;
                    }
                }

                var tok = new ZScriptToken { Position = cpos, Type = isdouble ? ZScriptTokenType.Double : ZScriptTokenType.Integer, Value = _sb.ToString() };
                try
                {
                    if (ishex || isoctal || !isdouble)
                    {
                        var numbase = ishex ? 16 : isoctal ? 8 : 10;
                        tok.ValueInt = Convert.ToInt32(tok.Value, numbase);
                        tok.ValueDouble = tok.ValueInt;
                    }
                    else
                    {
                        var dval = tok.Value[0] == '.' ? "0" + tok.Value : tok.Value;
                        tok.ValueDouble = Convert.ToDouble(dval);
                        tok.ValueInt = (int)tok.ValueDouble;
                    }
                }
                catch (OverflowException) // if the value is too small or too big, set it to the min or max, and set a warning message
                {
                    tok.WarningMessage = "Number " + tok.Value + " too " + (tok.Value[0] == '-' ? "small" : "big") + ". Set to ";

                    if (ishex || isoctal || !isdouble)
                    {
                        tok.ValueInt = tok.Value[0] == '-' ? int.MinValue : int.MaxValue;
                        tok.ValueDouble = tok.ValueInt;
                        tok.WarningMessage += tok.ValueInt;
                    }
                    else
                    {
                        tok.ValueDouble = tok.Value[0] == '-' ? double.MinValue : double.MaxValue;
                        tok.ValueInt = (int)tok.ValueDouble;
                        tok.WarningMessage += tok.ValueDouble;
                    }
                }
                catch (Exception)
                {
                    _reader.BaseStream.Position = cpos;
                    return null;
                }

                return tok;
            }
        }

        _reader.BaseStream.Position = cpos;
        return null;
    }

    private ZScriptToken? TryReadStringOrComment(bool allowstring, bool allowname, bool allowblock, bool allowline, bool allowregion)
    {
        var cpos = LastPosition = _reader.BaseStream.Position;
        if (_reader.PeekChar() == -1) return null;

        char c;
        try { c = _reader.ReadChar(); }
        catch (EndOfStreamException) { return null; }

        switch (c)
        {
            case '/': // comment
            {
                if (!allowblock && !allowline) break;
                char cnext;
                try { cnext = _reader.ReadChar(); }
                catch (EndOfStreamException) { break; } // invalid

                if (cnext == '/')
                {
                    if (!allowline) break;
                    // line comment: read until newline but not including it
                    _sb.Length = 0;
                    while (true)
                    {
                        try { cnext = _reader.ReadChar(); }
                        catch (EndOfStreamException) { break; }
                        if (cnext == '\n')
                        {
                            _reader.BaseStream.Position--;
                            break;
                        }
                        _sb.Append(cnext);
                    }

                    return new ZScriptToken { Position = cpos, Type = ZScriptTokenType.LineComment, Value = _sb.ToString() };
                }

                if (cnext == '*')
                {
                    if (!allowblock) break;
                    // block comment: read until closing sequence (doesn't nest)
                    _sb.Length = 0;
                    while (true)
                    {
                        try { cnext = _reader.ReadChar(); }
                        catch (EndOfStreamException) { break; }
                        if (cnext == '*')
                        {
                            var cnext2 = _reader.ReadChar();
                            if (cnext2 == '/') break;
                            _reader.BaseStream.Position--;
                        }
                        _sb.Append(cnext);
                    }

                    return new ZScriptToken { Position = cpos, Type = ZScriptTokenType.BlockComment, Value = _sb.ToString() };
                }
                break;
            }
            case '#': // #region and #endregion
            {
                if (!allowregion) break;
                _sb.Length = 0;

                while (true)
                {
                    char cnext;
                    try { cnext = _reader.ReadChar(); }
                    catch (EndOfStreamException) { break; }
                    if (cnext == '\n')
                    {
                        _reader.BaseStream.Position--;
                        break;
                    }
                    _sb.Append(cnext);
                }

                var line = _sb.ToString();

                // GZDoom doesn't care what follows #region/#endregion (e.g. "#regionlalala" is valid), but the keyword itself must be lowercase.
                if (line.StartsWith("region") || line.StartsWith("endregion"))
                    return new ZScriptToken { Position = cpos, Type = ZScriptTokenType.Region, Value = "" };

                break;
            }
            case '"':
            case '\'':
            {
                if ((c == '"' && !allowstring) || (c == '\'' && !allowname)) break;
                var type = c == '"' ? ZScriptTokenType.String : ZScriptTokenType.Name;
                _sb.Length = 0;
                while (true)
                {
                    try
                    {
                        // escape sequences aren't interpreted - a backslash just includes the next char verbatim (matches UDB's own known-incomplete behavior)
                        var cnext = _reader.ReadChar();
                        if (cnext == '\\')
                        {
                            cnext = _reader.ReadChar();
                            _sb.Append(cnext);
                        }
                        else if (cnext == c)
                        {
                            return new ZScriptToken { Position = cpos, Type = type, Value = _sb.ToString() };
                        }
                        else
                        {
                            _sb.Append(cnext);
                        }
                    }
                    catch (EndOfStreamException)
                    {
                        _reader.BaseStream.Position = cpos;
                        return null; // unterminated string, ends with EOF
                    }
                }
            }
        }

        _reader.BaseStream.Position = cpos;
        return null;
    }

    public ZScriptToken? ExpectToken(params ZScriptTokenType[] oneof)
    {
        var cpos = _reader.BaseStream.Position;

        try
        {
            if (oneof.Contains(ZScriptTokenType.Whitespace))
            {
                var tok = TryReadWhitespace();
                if (tok != null) return tok;
            }

            if (oneof.Contains(ZScriptTokenType.Identifier))
            {
                var tok = TryReadIdentifier();
                if (tok != null) return tok;
            }

            var blinecomment = oneof.Contains(ZScriptTokenType.LineComment);
            var bblockcomment = oneof.Contains(ZScriptTokenType.BlockComment);
            var bstring = oneof.Contains(ZScriptTokenType.String);
            var bname = oneof.Contains(ZScriptTokenType.Name);
            var bregion = oneof.Contains(ZScriptTokenType.Region);

            if (bstring || bname || bblockcomment || blinecomment || bregion)
            {
                var tok = TryReadStringOrComment(bstring, bname, bblockcomment, blinecomment, bregion);
                if (tok != null) return tok;
            }

            if (oneof.Contains(ZScriptTokenType.Integer) || oneof.Contains(ZScriptTokenType.Double))
            {
                var tok = TryReadNumber();
                if (tok != null && oneof.Contains(tok.Type)) return tok;
            }

            var namedTokenBuf = _reader.ReadChars(_namedTokenTypesOrder![0].Length);
            var namedToken = new string(namedTokenBuf);
            foreach (var expected in oneof)
            {
                if (!_namedTokenTypesReverse!.TryGetValue(expected, out var namedTokenType)) continue;
                if (namedToken.StartsWith(namedTokenType))
                {
                    _reader.BaseStream.Position = cpos + namedTokenType.Length;
                    return new ZScriptToken { Position = cpos, Type = _namedTokenTypes![namedTokenType], Value = namedTokenType };
                }
            }
        }
        catch (Exception)
        {
            try { _reader.BaseStream.Position = cpos; }
            catch (Exception) { /* stream may already be closed - nothing more to do */ }

            return null;
        }

        // the expected token wasn't found - read whatever token actually is there and mark it invalid
        _reader.BaseStream.Position = cpos;
        var invalid = ReadToken();
        if (invalid != null) invalid.IsValid = false;
        _reader.BaseStream.Position = cpos;
        return invalid;
    }

    /// <summary><paramref name="short_circuit"/> only checks for string/name literals and "everything else" (reported as an invalid token) - it skips identifier/number/named-token matching.</summary>
    public ZScriptToken? ReadToken(bool short_circuit = false)
    {
        if (_reader.PeekChar() == -1) return null;

        try
        {
            var tok = TryReadWhitespace();
            if (tok != null) return tok;

            if (!short_circuit)
            {
                tok = TryReadIdentifier();
                if (tok != null) return tok;

                tok = TryReadNumber();
                if (tok != null) return tok;
            }

            tok = TryReadStringOrComment(true, true, true, true, true);
            if (tok != null) return tok;

            if (!short_circuit)
            {
                tok = TryReadNamedToken();
                if (tok != null) return tok;
            }

            return new ZScriptToken { Position = _reader.BaseStream.Position, Type = ZScriptTokenType.Invalid, Value = "" + _reader.ReadChar(), IsValid = false };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private ZScriptToken? TryReadNamedToken()
    {
        var cpos = LastPosition = _reader.BaseStream.Position;

        var namedTokenBuf = _reader.ReadChars(_namedTokenTypesOrder![0].Length);
        var namedToken = new string(namedTokenBuf);
        foreach (var namedTokenType in _namedTokenTypesOrder)
        {
            if (namedToken.StartsWith(namedTokenType))
            {
                _reader.BaseStream.Position = cpos + namedTokenType.Length;
                return new ZScriptToken { Position = cpos, Type = _namedTokenTypes![namedTokenType], Value = namedTokenType };
            }
        }

        _reader.BaseStream.Position = cpos;
        return null;
    }

    public static string TokensToString(IEnumerable<ZScriptToken> tokens)
    {
        var outs = "";
        foreach (var tok in tokens) outs += tok.Value;
        return outs;
    }
}
