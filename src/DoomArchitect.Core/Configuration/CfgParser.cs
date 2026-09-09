using System.Globalization;
using System.Text;

namespace DoomArchitect.Core.Configuration;

/// <summary>
/// Parses UDB's real <c>.cfg</c> grammar into an ordered
/// <see cref="CfgStatement"/> tree - confirmed against UDB's own
/// <c>CodeImp.DoomBuilder.IO.Configuration</c>
/// (<c>InputStructure</c>/<c>ParseAssignment</c>/<c>ParseString</c>/
/// <c>ParseNumber</c>/<c>ParseKeyword</c>/<c>ParseFunction</c>), read
/// directly rather than assumed. Reorganized around a small cursor instead
/// of one large character-dispatch loop with mutable <c>ref</c> parameters,
/// matching how <see cref="IO.UdmfTreeParser"/> was already reworked from
/// UDB's own UDMF parser - but kept as its own independent parser rather
/// than sharing a base with it (see this project's TODO.md architecture
/// notes: UDB itself keeps <c>UniversalParser</c> and <c>Configuration</c>
/// as two separate classes despite both being curly-brace/assignment
/// grammars, and the two really do differ - see remarks below).
///
/// Deliberate differences from UDMF's grammar, all confirmed in source,
/// not guessed:
/// - A key is not restricted to a letter/digit/underscore charset - it's
///   "every character up to the next structural delimiter, trimmed",
///   which is what lets a bare integer (<c>3004 { ... }</c>) work as a
///   block key with no special-casing.
/// - Keys are case-sensitive (UDB's own parser never lowercases them,
///   unlike <see cref="IO.UdmfTreeParser"/>'s deliberate lowercasing).
/// - A key with no value at all (<c>somekey;</c>) or an explicit
///   <c>null</c> keyword are both valid and distinct from a missing key -
///   modeled as <see cref="CfgAssignStatement.Value"/> being null.
/// - Hexadecimal literals (UDMF's <c>0x...</c>) are not supported at all -
///   UDB's own <c>ParseNumber</c> has no such case, so a value starting
///   with <c>0x</c> would fail to parse as a number there too.
/// - A trailing <c>f</c> suffix (<c>1.5f</c>) marks a single-precision
///   float, distinct from a plain double (<c>1.5</c>) in UDB's own model
///   (needed there for round-trip write-formatting). This project only
///   ever reads <c>.cfg</c> data, never writes it back out, so both
///   collapse to <see cref="CfgValueKind.Double"/> here - a deliberate
///   simplification, not a parsing gap.
/// </summary>
public static class CfgParser
{
    private const string NumberStartCharacters = "0123456789-.";

    public static IReadOnlyList<CfgStatement> Parse(string text)
    {
        var cursor = new Cursor(text);
        var statements = ParseBody(cursor, isRoot: true);
        return statements;
    }

    private static List<CfgStatement> ParseBody(Cursor c, bool isRoot)
    {
        var statements = new List<CfgStatement>();
        var key = new StringBuilder();

        while (!c.AtEnd)
        {
            var ch = c.Advance();

            switch (ch)
            {
                case '{':
                {
                    var blockKey = RequireKey(key, c.Line);
                    var body = ParseBody(c, isRoot: false);
                    statements.Add(new CfgBlockStatement(blockKey, body));
                    break;
                }

                case '}':
                    if (isRoot) throw new CfgParseException("Unexpected '}'.", c.Line);
                    return statements;

                case '(':
                {
                    var functionName = key.ToString().Trim();
                    key.Clear();
                    statements.Add(ParseFunctionCall(c, functionName));
                    break;
                }

                case '=':
                {
                    var assignKey = RequireKey(key, c.Line);
                    var value = ParseAssignmentValue(c);
                    statements.Add(new CfgAssignStatement(assignKey, value));
                    break;
                }

                case ';':
                {
                    var bareKey = key.ToString().Trim();
                    key.Clear();
                    if (bareKey.Length > 0) statements.Add(new CfgAssignStatement(bareKey, null));
                    break;
                }

                case '\n':
                    // Spaces aren't allowed in a real key, but a bare
                    // newline while accumulating one is folded into a
                    // space rather than rejected outright - it gets
                    // trimmed away at the point the key is actually used,
                    // exactly like UDB's own parser.
                    key.Append(' ');
                    break;

                case '/' when !c.AtEnd && c.Peek == '/':
                    c.Advance();
                    c.SkipToEndOfLine();
                    break;

                case '/' when !c.AtEnd && c.Peek == '*':
                    c.Advance();
                    c.SkipBlockComment();
                    break;

                case '/':
                    // A lone '/' that isn't a comment opener is silently
                    // discarded (not appended to the key) - matches UDB's
                    // own real parser exactly, an obscure quirk that
                    // doesn't matter for any legitimate key, but costs
                    // nothing to mirror precisely.
                    break;

                default:
                    key.Append(ch);
                    break;
            }
        }

        if (!isRoot) throw new CfgParseException("Unexpected end of file - missing '}'.", c.Line);
        return statements;
    }

    private static string RequireKey(StringBuilder key, int line)
    {
        var trimmed = key.ToString().Trim();
        key.Clear();
        if (trimmed.Length == 0) throw new CfgParseException("Missing key name in assignment or scope.", line);
        return trimmed;
    }

    /// <summary>Reads a value up to (and consuming) its terminating ';'.</summary>
    private static CfgValue? ParseAssignmentValue(Cursor c)
    {
        while (!c.AtEnd)
        {
            var ch = c.Peek;

            if (ch == '"')
            {
                var s = ParseString(c);
                SkipToStatementEnd(c);
                return CfgValue.OfString(s);
            }

            if (NumberStartCharacters.IndexOf(ch) > -1)
            {
                return ParseNumber(c);
            }

            if (ch == '\n')
            {
                c.Advance();
                continue;
            }

            if (ch == ';')
            {
                c.Advance();
                return null; // "key = ;" - degenerate but matches a bare null assignment
            }

            if (ch is not (' ' or '\t'))
            {
                return ParseKeyword(c);
            }

            c.Advance();
        }

        throw new CfgParseException("Unexpected end of data.", c.Line);
    }

    /// <summary>Consumes trailing whitespace/newlines up to and including the statement-terminating ';'.</summary>
    private static void SkipToStatementEnd(Cursor c)
    {
        while (!c.AtEnd)
        {
            var ch = c.Advance();
            if (ch == ';') return;
            if (ch != ' ' && ch != '\t' && ch != '\n') throw new CfgParseException("Expected ';'.", c.Line);
        }

        throw new CfgParseException("Unexpected end of data.", c.Line);
    }

    private static string ParseString(Cursor c)
    {
        c.Advance(); // opening quote
        var sb = new StringBuilder();

        while (true)
        {
            if (c.AtEnd) throw new CfgParseException("Unterminated string.", c.Line);
            var ch = c.Advance();
            if (ch == '"') return sb.ToString();

            if (ch != '\\')
            {
                sb.Append(ch);
                continue;
            }

            if (c.AtEnd) throw new CfgParseException("Unterminated string escape.", c.Line);
            var escaped = c.Advance();
            switch (escaped)
            {
                case '\\': sb.Append('\\'); break;
                case '"': sb.Append('"'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                default:
                    if (char.IsDigit(escaped))
                    {
                        sb.Append(ReadNumericEscape(c, escaped));
                    }
                    else
                    {
                        sb.Append(escaped);
                    }

                    break;
            }
        }
    }

    /// <summary>A `\DDD` escape: exactly 3 decimal digits interpreted as an ASCII character code.</summary>
    private static char ReadNumericEscape(Cursor c, char firstDigit)
    {
        var digits = new string(new[] { firstDigit, ReadDigit(c), ReadDigit(c) });
        return (char)int.Parse(digits, CultureInfo.InvariantCulture);
    }

    private static char ReadDigit(Cursor c)
    {
        if (c.AtEnd || !char.IsDigit(c.Peek)) throw new CfgParseException("Invalid \\DDD string escape.", c.Line);
        return c.Advance();
    }

    private static CfgValue ParseNumber(Cursor c)
    {
        var line = c.Line;
        var sb = new StringBuilder();

        while (!c.AtEnd && c.Peek is not (';' or ',' or ')'))
        {
            var ch = c.Advance();
            if (ch != '\n') sb.Append(ch);
        }

        if (c.AtEnd) throw new CfgParseException("Unexpected end of data.", line);
        if (c.Peek == ';') c.Advance(); // ')' / ',' are left for the caller (function-argument context)

        var raw = sb.ToString().Trim();

        if (raw.EndsWith("f", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(raw[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
            {
                return CfgValue.OfDouble(f);
            }

            throw new CfgParseException($"Invalid number: '{raw}'.", line);
        }

        if (raw.Contains('.'))
        {
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            {
                return CfgValue.OfDouble(d);
            }

            throw new CfgParseException($"Invalid number: '{raw}'.", line);
        }

        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
        {
            return CfgValue.OfInt(l);
        }

        throw new CfgParseException($"Invalid number: '{raw}'.", line);
    }

    private static CfgValue? ParseKeyword(Cursor c)
    {
        var line = c.Line;
        var sb = new StringBuilder();

        while (!c.AtEnd && c.Peek is not (';' or ',' or ')'))
        {
            var ch = c.Advance();
            if (ch != '\n') sb.Append(ch);
        }

        if (c.AtEnd) throw new CfgParseException("Unexpected end of data.", line);
        if (c.Peek == ';') c.Advance();

        switch (sb.ToString().Trim().ToLowerInvariant())
        {
            case "true": return CfgValue.OfBool(true);
            case "false": return CfgValue.OfBool(false);
            case "null": return null;
            default: throw new CfgParseException($"Unknown keyword in assignment: '{sb}'.", line);
        }
    }

    /// <summary>
    /// <c>include(...)</c> is the only function call UDB's real grammar
    /// recognizes - any other name is a parse error there too. Arguments
    /// are comma-separated string/number/keyword values, same value
    /// grammar as an assignment; this project only ever needs the string
    /// form (a path, optionally a dotted sub-path).
    /// </summary>
    private static CfgIncludeStatement ParseFunctionCall(Cursor c, string functionName)
    {
        if (!string.Equals(functionName, "include", StringComparison.OrdinalIgnoreCase))
        {
            throw new CfgParseException($"Unknown function call: '{functionName}'.", c.Line);
        }

        var args = new List<string>();

        while (true)
        {
            SkipWhitespaceAndComments(c);
            if (c.AtEnd) throw new CfgParseException("Unexpected end of data.", c.Line);

            if (c.Peek == ')')
            {
                c.Advance();
                break;
            }

            if (c.Peek == ',')
            {
                c.Advance();
                continue;
            }

            if (c.Peek != '"')
            {
                throw new CfgParseException("include() arguments must be strings.", c.Line);
            }

            args.Add(ParseString(c));
        }

        // No trailing ';' is consumed here - control returns to the
        // enclosing scope's own loop, which harmlessly treats a following
        // ';' as a no-op (the accumulating key was already cleared before
        // this call), exactly like UDB's own ParseFunction/InputStructure
        // split does.
        if (args.Count < 1) throw new CfgParseException("include() requires at least one argument.", c.Line);
        return new CfgIncludeStatement(args[0], args.Count > 1 ? args[1] : null);
    }

    private static void SkipWhitespaceAndComments(Cursor c)
    {
        while (!c.AtEnd)
        {
            if (c.Peek is ' ' or '\t' or '\n')
            {
                c.Advance();
                continue;
            }

            if (c.Peek == '/' && c.PeekNext == '/')
            {
                c.Advance();
                c.Advance();
                c.SkipToEndOfLine();
                continue;
            }

            if (c.Peek == '/' && c.PeekNext == '*')
            {
                c.Advance();
                c.Advance();
                c.SkipBlockComment();
                continue;
            }

            break;
        }
    }

    private sealed class Cursor
    {
        private readonly string _text;
        private int _pos;

        public Cursor(string text)
        {
            _text = text;
        }

        public int Line { get; private set; } = 1;

        public bool AtEnd => _pos >= _text.Length;

        public char Peek => _text[_pos];

        public char PeekNext => _pos + 1 < _text.Length ? _text[_pos + 1] : '\0';

        public char Advance()
        {
            var ch = _text[_pos++];
            if (ch == '\n') Line++;
            return ch;
        }

        public void SkipToEndOfLine()
        {
            while (!AtEnd && Peek != '\n') Advance();
            if (!AtEnd) Advance(); // consume the newline itself
        }

        public void SkipBlockComment()
        {
            while (!AtEnd && !(Peek == '*' && PeekNext == '/')) Advance();
            if (!AtEnd)
            {
                Advance();
                Advance();
            }
        }
    }
}
