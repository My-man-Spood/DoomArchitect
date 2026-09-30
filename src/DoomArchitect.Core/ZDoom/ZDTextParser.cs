using System.Globalization;
using System.Text;

namespace DoomArchitect.Core.ZDoom;

/// <summary>
/// The shared string-token reader both <c>DecorateParser</c> and
/// <c>ZScriptParser</c> (Phase 2) build on - ported from UDB's real
/// <c>ZDTextParser</c>, keeping every actual character-level algorithm
/// (whitespace/comment/region skipping, token/line reading, structure
/// skipping) verbatim. What's deliberately NOT ported, because it belongs
/// to systems this project doesn't have and this feature doesn't need:
/// UDB's live "which script files are being watched for changes" resource
/// tracking (<c>ScriptResource</c>/<c>AddTextResource</c>), its ACS-compiler
/// integration and rich <c>TextResourceErrorItem</c>/<c>ErrorLogger</c>
/// diagnostics panel (replaced here with a bare <see cref="HasError"/>/
/// <see cref="ErrorDescription"/> pair - enough for a parser to know "stop,
/// this input is broken" and degrade gracefully, matching this codebase's
/// existing "a resource we can't fully parse just doesn't contribute,
/// nothing crashes" posture), and its color-name/rooted-path helpers
/// (<c>GetColorFromString</c>/<c>GetRootedPath</c>/<c>CheckInvalidPathChars</c>),
/// which exist for GZDB `$color` comments and Directory-resource path
/// resolution - neither relevant to discovering an actor's identity for the
/// Thing browser or the required-archive fingerprint check.
/// </summary>
public abstract class ZDTextParser
{
    protected string Whitespace = "\n \t\r \0"; // non-breaking space counts as whitespace too
    protected string SpecialTokens = ":{}+-\n;";
    protected bool SkipRegions = true;

    // `internal` (not `protected`) matching UDB's own real visibility here -
    // DecorateStateStructure/DecorateStateGoto (Phase 2) aren't subclasses
    // of this one but need direct stream access the same way UDB's own
    // versions do, being in the same assembly.
    internal Stream? DataStream { get; private set; }
    internal BinaryReader? DataReader { get; private set; }
    internal string SourceName { get; private set; } = string.Empty;
    protected long PrevStreamPosition;

    public int ErrorLine { get; private set; } = -1;
    public string? ErrorDescription { get; private set; }
    public bool HasError => ErrorDescription != null;

    /// <summary>Begins parsing <paramref name="stream"/> from its start - <paramref name="sourceName"/> is used only for error messages. An empty stream isn't an error - UDB's own real version still initializes everything for it, just skips a would-be warning (this project has no warning surface at this level to skip anyway).</summary>
    public virtual bool Parse(Stream stream, string sourceName)
    {
        ClearError();

        DataStream = stream;
        DataReader = new BinaryReader(stream, Encoding.ASCII);
        SourceName = sourceName;
        DataStream.Seek(0, SeekOrigin.Begin);
        return true;
    }

    /// <summary>
    /// Restores a previously-saved parse target - for a subclass (e.g.
    /// <c>DecorateParser</c>'s `#include` handling) that recurses into
    /// <see cref="Parse"/> again on the same instance to parse an included
    /// file's content, then needs to put the outer file's stream/reader
    /// back to keep reading it from where it left off. A plain "swap the
    /// whole stream/reader back" rather than seeking a shared stream,
    /// matching UDB's own real approach - the include's content is a
    /// genuinely separate <see cref="Stream"/>, not a sub-range of the same
    /// one.
    /// </summary>
    protected void RestoreParseTarget(Stream stream, BinaryReader reader, string sourceName)
    {
        DataStream = stream;
        DataReader = reader;
        SourceName = sourceName;
    }

    protected internal bool IsWhitespace(char c) => Whitespace.IndexOf(c) > -1;

    private bool IsSpecialToken(char c) => SpecialTokens.IndexOf(c) > -1;

    protected internal bool IsSpecialToken(string s) => s.Length > 0 && SpecialTokens.IndexOf(s[0]) > -1;

    protected internal string StripTokenQuotes(string token) => StripQuotes(token);

    public static string StripQuotes(string token)
    {
        if (!string.IsNullOrEmpty(token) && token[0] == '"') token = token[1..];
        if (!string.IsNullOrEmpty(token) && token[^1] == '"') token = token[..^1];
        return token;
    }

    /// <summary>
    /// Skips whitespace, line/block comments, and (when <see cref="SkipRegions"/>)
    /// `#region`/`#endregion` lines, leaving the read position right before
    /// the first non-whitespace character. Returns false at end of stream.
    /// </summary>
    protected internal bool SkipWhitespace(bool skipNewline)
    {
        var offset = skipNewline ? 0 : 1;
        char c;
        PrevStreamPosition = DataStream!.Position;

        do
        {
            if (DataStream.Position == DataStream.Length) return false;
            c = (char)DataReader!.ReadByte();

            if (c == '/')
            {
                if (DataStream.Position == DataStream.Length) return false;
                var c2 = (char)DataReader.ReadByte();
                if (c2 == '/')
                {
                    if (DataStream.Position == DataStream.Length) return false;
                    var c3 = (char)DataReader.ReadByte();

                    // Skip the entire line
                    var c4 = c3;
                    while (c4 != '\n' && DataStream.Position < DataStream.Length) c4 = (char)DataReader.ReadByte();

                    if (DataStream.Position == DataStream.Length) return true;
                    c = c4;
                }
                else if (c2 == '*')
                {
                    char c4, c3 = '\0';
                    PrevStreamPosition = DataStream.Position;
                    do
                    {
                        if (DataStream.Position == DataStream.Length)
                        {
                            // GZDoom doesn't warn about this either - not an error here.
                            return false;
                        }

                        c4 = c3;
                        c3 = (char)DataReader.ReadByte();
                    }
                    while (c4 != '*' || c3 != '/');
                    c = ' ';
                }
                else
                {
                    DataStream.Seek(-1, SeekOrigin.Current); // not a comment, rewind from reading c2
                }
            }
            else if (SkipRegions && c == '#')
            {
                var startpos = DataStream.Position - 1;
                var s = ReadToken().ToLowerInvariant();
                if (s == "region" || s == "endregion")
                {
                    var ch = ' ';
                    while (ch != '\n' && DataStream.Position < DataStream.Length) ch = (char)DataReader.ReadByte();
                    c = ch;
                }
                else
                {
                    DataStream.Seek(startpos, SeekOrigin.Begin); // rewind so this token can be read again
                    return true;
                }
            }
        }
        while (Whitespace.IndexOf(c, offset) > -1);

        DataStream.Seek(-1, SeekOrigin.Current); // go back so this non-whitespace character can be read again
        return true;
    }

    /// <summary>Reads all sequential non-whitespace characters, or a single special-token character. Empty string at end of stream.</summary>
    protected internal string ReadToken() => ReadToken(true);

    protected internal string ReadToken(bool multiline)
    {
        if (DataStream!.Position == DataStream.Length) return string.Empty;

        PrevStreamPosition = DataStream.Position;

        var token = "";
        var quotedstring = false;

        var c = (char)DataReader!.ReadByte();
        while (!IsWhitespace(c) || quotedstring || IsSpecialToken(c))
        {
            if (!multiline && c == '\r')
            {
                DataStream.Seek(-1, SeekOrigin.Current); // go back so the line number stays correct
                return token;
            }

            if (!quotedstring && IsSpecialToken(c))
            {
                if (token.Length == 0)
                {
                    token += c;
                    break;
                }

                DataStream.Seek(-1, SeekOrigin.Current); // this is a new token, read it again next time
                break;
            }

            if (c == '"')
            {
                if (quotedstring) quotedstring = false;
                if (token.Length == 0) quotedstring = true;

                token += c;
                if (!quotedstring) break; // break after the closing quote
            }
            else if (c == '/' && !quotedstring)
            {
                if (DataStream.Position == DataStream.Length) return token;
                var c2 = (char)DataReader.ReadByte();
                if (c2 == '/' || c2 == '*')
                {
                    DataStream.Seek(-2, SeekOrigin.Current); // comment start - read it again as a comment
                    break;
                }

                DataStream.Seek(-1, SeekOrigin.Current);
                token += c;
            }
            else
            {
                token += c;
            }

            if (DataStream.Position < DataStream.Length) c = (char)DataReader.ReadByte();
            else break;
        }

        return token;
    }

    /// <summary>Same shape as <see cref="ReadToken(bool)"/> but with a caller-supplied special-token set instead of <see cref="SpecialTokens"/>. Null at end of stream.</summary>
    protected internal string? ReadToken(string specialTokens)
    {
        if (DataStream!.Position == DataStream.Length) return null;

        PrevStreamPosition = DataStream.Position;

        var token = "";
        var quotedstring = false;

        var c = (char)DataReader!.ReadByte();
        while (!IsWhitespace(c) || quotedstring || specialTokens.IndexOf(c) != -1)
        {
            if (!quotedstring && specialTokens.IndexOf(c) != -1)
            {
                if (token.Length == 0)
                {
                    token += c;
                    break;
                }

                DataStream.Seek(-1, SeekOrigin.Current);
                break;
            }

            if (c == '"')
            {
                if (quotedstring) quotedstring = false;
                if (token.Length == 0) quotedstring = true;

                token += c;
                if (!quotedstring) break;
            }
            else if (c == '/' && !quotedstring)
            {
                if (DataStream.Position == DataStream.Length) return token;
                var c2 = (char)DataReader.ReadByte();
                if (c2 == '/' || c2 == '*')
                {
                    DataStream.Seek(-2, SeekOrigin.Current);
                    break;
                }

                DataStream.Seek(-1, SeekOrigin.Current);
                token += c;
            }
            else
            {
                token += c;
            }

            if (DataStream.Position < DataStream.Length) c = (char)DataReader.ReadByte();
            else break;
        }

        return token;
    }

    /// <summary>Reads the rest of the current line (trimmed). Null at end of stream.</summary>
    protected internal string? ReadLine()
    {
        if (DataStream!.Position == DataStream.Length) return null;

        var token = "";
        var c = (char)DataReader!.ReadByte();
        while (c != '\n')
        {
            token += c;
            if (DataStream.Position < DataStream.Length) c = (char)DataReader.ReadByte();
            else break;
        }

        return token.Trim();
    }

    public bool NextTokenIs(string expectedToken) => NextTokenIs(expectedToken, true);

    public bool NextTokenIs(string expectedToken, bool reportError)
    {
        if (!SkipWhitespace(true))
        {
            if (reportError) ReportError("Unexpected end of the structure");
            return false;
        }

        var prevPosition = DataStream!.Position;
        var token = ReadToken();

        if (string.Compare(token, expectedToken, StringComparison.OrdinalIgnoreCase) != 0)
        {
            if (reportError) ReportError($"Expected \"{expectedToken}\", but got \"{token}\"");
            DataStream.Seek(prevPosition, SeekOrigin.Begin); // rewind so this structure can be read again
            return false;
        }

        return true;
    }

    protected internal bool ReadSignedFloat(ref float value) => ReadSignedFloat(ReadToken(false), ref value);

    protected internal bool ReadSignedFloat(string token, ref float value)
    {
        var sign = 1;
        if (token == "-")
        {
            sign = -1;
            token = ReadToken(false);
        }

        var success = float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var val);
        if (success) value = val * sign;
        return success;
    }

    protected internal bool ReadSignedInt(ref int value) => ReadSignedInt(ReadToken(false), ref value);

    protected internal bool ReadSignedInt(string token, ref int value)
    {
        var sign = 1;
        if (token == "-")
        {
            sign = -1;
            token = ReadToken(false);
        }

        var success = int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val);
        if (success) value = val * sign;
        return success;
    }

    protected internal bool ReadByte(ref byte value) => ReadByte(ReadToken(false), ref value);

    protected internal bool ReadByte(string token, ref byte value)
    {
        if (token == "-") return false;
        if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) || result is < 0 or > 255) return false;

        value = (byte)result;
        return true;
    }

    /// <summary>Skips a whole `{ ... }` structure (nested braces tracked), optionally stopping early if a token in <paramref name="breakAt"/> is seen before the opening brace.</summary>
    protected void SkipStructure() => SkipStructure(new HashSet<string>());

    protected void SkipStructure(HashSet<string> breakAt)
    {
        if (breakAt.Count > 0) breakAt = new HashSet<string>(breakAt, StringComparer.OrdinalIgnoreCase);

        string token;
        do
        {
            if (!SkipWhitespace(true)) break;
            token = ReadToken();
            if (string.IsNullOrEmpty(token)) break;
            if (breakAt.Contains(token))
            {
                DataStream!.Seek(-token.Length - 1, SeekOrigin.Current);
                return;
            }
        }
        while (token != "{");

        var scopelevel = 1;
        do
        {
            if (!SkipWhitespace(true)) break;
            token = ReadToken();
            if (string.IsNullOrEmpty(token)) break;
            if (token == "{") scopelevel++;
            if (token == "}") scopelevel--;
        }
        while (scopelevel > 0);
    }

    protected internal void ReportError(string message)
    {
        ErrorDescription = message;
        ErrorLine = DataStream != null ? GetCurrentLineNumber() : -1;
    }

    protected void ClearError()
    {
        ErrorDescription = null;
        ErrorLine = -1;
    }

    /// <summary>Recomputes the 0-based line number at the current stream position by rescanning from the start - simple and only ever called on error, not a hot path.</summary>
    protected virtual int GetCurrentLineNumber()
    {
        var pos = DataStream!.Position;
        var finishpos = Math.Min(PrevStreamPosition, pos);
        long readpos = 0;
        var linenumber = -1;

        DataStream.Seek(0, SeekOrigin.Begin);
        var textreader = new StreamReader(DataStream, Encoding.ASCII, leaveOpen: true);
        while (readpos < finishpos + 1)
        {
            var line = textreader.ReadLine();
            if (line == null) break;
            readpos += line.Length + 2;
            linenumber++;
        }

        DataStream.Seek(pos, SeekOrigin.Begin);
        return Math.Max(linenumber, 0);
    }
}
