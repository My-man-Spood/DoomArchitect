using System.Globalization;
using System.Text;

namespace DoomArchitect.Core.IO;

/// <summary>
/// Parses UDMF text into a generic <see cref="UdmfBlock"/> tree. A close
/// port of UDB's own <c>UniversalParser.InputStructure</c> - same grammar,
/// same key charset, same number/keyword classification rules, same
/// string escapes - reorganized into named methods around a small cursor
/// instead of one ~500-line character-dispatch loop, for reviewability.
/// One deliberate behavioral fix from UDB's own parser: a <c>\DDD</c>
/// string escape here correctly advances past all 3 digits (UDB's own
/// parser only advances 1, so the trailing 2 digits get reprocessed as
/// literal text - a genuine bug nothing depends on in practice).
/// </summary>
public static class UdmfTreeParser
{
    private const string KeyCharacters = "abcdefghijklmnopqrstuvwxyz0123456789_";
    private const string NumberStartCharacters = "0123456789-.";

    /// <summary>
    /// Parses UDMF text into a tree. <paramref name="warnings"/> collects
    /// non-fatal issues discovered during tokenizing - currently just a
    /// dropped <c>nan</c> field value, matching UDB's own
    /// "field is being dropped permanently" warning.
    /// </summary>
    public static UdmfBlock Parse(string text, List<string> warnings)
    {
        var cursor = new Cursor(text);
        var (assignments, blocks) = ParseCollection(cursor, isRoot: true, warnings);
        return new UdmfBlock(string.Empty, assignments, blocks);
    }

    private static (List<UdmfAssignment> Assignments, List<UdmfBlock> Blocks) ParseCollection(
        Cursor c, bool isRoot, List<string> warnings)
    {
        var assignments = new List<UdmfAssignment>();
        var blocks = new List<UdmfBlock>();

        while (true)
        {
            c.SkipInsignificant();

            if (c.AtEnd)
            {
                if (!isRoot) throw new UdmfParseException("Unexpected end of file - missing '}'.", c.Line);
                break;
            }

            if (c.Peek == '}')
            {
                if (isRoot) throw new UdmfParseException("Unexpected '}'.", c.Line);
                c.Advance();
                break;
            }

            var key = ReadKey(c);
            c.SkipInsignificant();

            if (c.AtEnd) throw new UdmfParseException($"Expected '=' or '{{' after key '{key}'.", c.Line);

            if (c.Peek == '{')
            {
                c.Advance();
                var (childAssignments, childBlocks) = ParseCollection(c, isRoot: false, warnings);
                blocks.Add(new UdmfBlock(key, childAssignments, childBlocks));
            }
            else if (c.Peek == '=')
            {
                c.Advance();
                var value = ReadValue(c, key, warnings);
                if (value.HasValue) assignments.Add(new UdmfAssignment(key, value.Value));
            }
            else
            {
                throw new UdmfParseException($"Expected '=' or '{{' after key '{key}'.", c.Line);
            }
        }

        return (assignments, blocks);
    }

    private static string ReadKey(Cursor c)
    {
        var start = c.Position;
        while (!c.AtEnd && IsKeyCharacter(c.Peek)) c.Advance();
        if (c.Position == start) throw new UdmfParseException("Expected a key.", c.Line);
        return c.Substring(start, c.Position - start).ToLowerInvariant();
    }

    private static bool IsKeyCharacter(char ch) =>
        KeyCharacters.IndexOf(char.ToLowerInvariant(ch)) > -1;

    /// <summary>Reads a value and consumes its terminating ';'. Returns null for a dropped 'nan' keyword.</summary>
    private static UdmfValue? ReadValue(Cursor c, string key, List<string> warnings)
    {
        c.SkipInsignificant();
        if (c.AtEnd) throw new UdmfParseException("Expected a value.", c.Line);

        if (c.Peek == '"')
        {
            var s = ReadQuotedString(c);
            c.SkipInsignificant();
            Expect(c, ';');
            return UdmfValue.OfString(s);
        }

        var line = c.Line;
        var raw = ReadRawUntilSemicolon(c).Trim();
        Expect(c, ';');

        if (raw.Length == 0) throw new UdmfParseException("Key has no value assigned.", line);

        if (NumberStartCharacters.IndexOf(raw[0]) > -1)
        {
            return ParseNumber(raw, line);
        }

        var lowered = raw.ToLowerInvariant();
        switch (lowered)
        {
            case "true": return UdmfValue.OfBool(true);
            case "false": return UdmfValue.OfBool(false);
            case "nan":
                warnings.Add($"UDMF map data line {line}: value of field {key} has a value of NaN (not a number). Field is being dropped permanently.");
                return null;
            default:
                throw new UdmfParseException($"Unknown keyword in assignment: '{raw}'.", line);
        }
    }

    private static UdmfValue ParseNumber(string text, int line)
    {
        if (text.Length > 2 && (text[0] == '0') && (text[1] is 'x' or 'X'))
        {
            var hex = text[2..];
            if (long.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hexValue))
            {
                return UdmfValue.OfInt(hexValue);
            }

            throw new UdmfParseException("Value too big.", line);
        }

        var lowered = text.ToLowerInvariant();
        if (text.Contains('.') || lowered.Contains("e-"))
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
            {
                return UdmfValue.OfDouble(doubleValue);
            }

            throw new UdmfParseException($"Invalid number: '{text}'.", line);
        }

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
        {
            return UdmfValue.OfInt(longValue);
        }

        throw new UdmfParseException($"Invalid number: '{text}'.", line);
    }

    private static string ReadQuotedString(Cursor c)
    {
        Expect(c, '"');
        var sb = new StringBuilder();

        while (true)
        {
            if (c.AtEnd) throw new UdmfParseException("Unterminated string.", c.Line);
            var ch = c.Advance();
            if (ch == '"') break;

            if (ch != '\\')
            {
                sb.Append(ch);
                continue;
            }

            if (c.AtEnd) throw new UdmfParseException("Unterminated string escape.", c.Line);
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

        return sb.ToString();
    }

    /// <summary>
    /// A `\DDD` escape: exactly 3 decimal digits (the first already
    /// consumed by the caller), interpreted as a decimal character code.
    /// UDB's own parser only advances past 1 of the 2 remaining digits,
    /// so they leak into the string as literal text - fixed here (see
    /// type-level remarks).
    /// </summary>
    private static char ReadNumericEscape(Cursor c, char firstDigit)
    {
        var digits = new string(new[] { firstDigit, ReadDigit(c), ReadDigit(c) });
        return (char)int.Parse(digits, CultureInfo.InvariantCulture);
    }

    private static char ReadDigit(Cursor c)
    {
        if (c.AtEnd || !char.IsDigit(c.Peek)) throw new UdmfParseException("Invalid \\DDD string escape.", c.Line);
        return c.Advance();
    }

    private static string ReadRawUntilSemicolon(Cursor c)
    {
        var start = c.Position;
        while (!c.AtEnd && c.Peek != ';') c.Advance();
        if (c.AtEnd) throw new UdmfParseException("Expected ';'.", c.Line);
        return c.Substring(start, c.Position - start);
    }

    private static void Expect(Cursor c, char expected)
    {
        c.SkipInsignificant();
        if (c.AtEnd || c.Peek != expected) throw new UdmfParseException($"Expected '{expected}'.", c.Line);
        c.Advance();
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

        public int Position => _pos;

        public bool AtEnd => _pos >= _text.Length;

        public char Peek => _text[_pos];

        public char Advance()
        {
            var ch = _text[_pos++];
            if (ch == '\n') Line++;
            return ch;
        }

        public string Substring(int start, int length) => _text.Substring(start, length);

        public void SkipInsignificant()
        {
            while (!AtEnd)
            {
                var ch = Peek;
                if (char.IsWhiteSpace(ch))
                {
                    Advance();
                    continue;
                }

                if (ch == '/' && _pos + 1 < _text.Length && _text[_pos + 1] == '/')
                {
                    while (!AtEnd && Peek != '\n') Advance();
                    continue;
                }

                if (ch == '/' && _pos + 1 < _text.Length && _text[_pos + 1] == '*')
                {
                    Advance();
                    Advance();
                    while (!AtEnd && !(Peek == '*' && _pos + 1 < _text.Length && _text[_pos + 1] == '/')) Advance();
                    if (!AtEnd)
                    {
                        Advance();
                        Advance();
                    }

                    continue;
                }

                break;
            }
        }
    }
}
