using System.Collections.Generic;
using System.IO;
using System.Text;
using DoomArchitect.Core.ZDoom.Bcs;
using Godot;
using GodotDictionary = Godot.Collections.Dictionary;

/// <summary>
/// Drives a <see cref="CodeEdit"/>'s highlighting directly from
/// <see cref="BcsTokenizer"/> - the real lexer, not a regex/keyword-list
/// approximation (Godot's own built-in <c>CodeHighlighter</c> resource
/// would only ever get BCS's case-insensitive keywords, radix literals,
/// etc. right by accident). <see cref="Rebuild"/> re-tokenizes the whole
/// buffer once per edit (<see cref="ScriptDocument"/> calls it from the
/// <c>CodeEdit</c>'s own <c>TextChanged</c> signal) and caches a
/// per-line column-to-color map, since Godot calls
/// <see cref="_GetLineSyntaxHighlighting"/> once per visible line on
/// every redraw - re-tokenizing the whole file from inside that method
/// itself would redo the same work many times a frame.
///
/// Per <c>SyntaxHighlighter</c>'s own documented contract, a dictionary
/// entry's column marks where a colored region *starts*; it keeps that
/// color until the next entry or end of line. So every colored token
/// needs two entries here, not one: its own start column (the token's
/// color) and its end column (<see cref="DefaultColor"/>, to stop that
/// color from bleeding into whatever comes next - whitespace, an
/// uncolored identifier, or a token of a different color).
/// </summary>
public partial class BcsSyntaxHighlighter : SyntaxHighlighter
{
    // Chosen to read clearly against this project's existing dark editor
    // theme (Assets/BaseTheme.tres) - not an attempt to match any one
    // external editor's exact palette.
    private static readonly Color KeywordColor = new("#569cd6");
    private static readonly Color LiteralColor = new("#ce9178");
    private static readonly Color NumberColor = new("#b5cea8");
    private static readonly Color CommentColor = new("#6a9955");
    private static readonly Color PreprocessorColor = new("#c586c0");
    private static readonly Color DefaultColor = new("#d4d4d4");

    private readonly Dictionary<int, GodotDictionary> _lineHighlighting = new();

    /// <summary>
    /// Re-tokenizes <paramref name="text"/> and rebuilds the per-line
    /// cache <see cref="_GetLineSyntaxHighlighting"/> reads from - call
    /// whenever the owning <c>CodeEdit</c>'s text changes. Finishes by
    /// calling the base class's own public <c>UpdateCache()</c> - a real
    /// bug without it: Godot's <c>SyntaxHighlighter</c> keeps an internal
    /// per-line cache that's normally invalidated only for the specific
    /// edited line range, not the whole document, so inserting (or
    /// deleting) a newline - which shifts every line below it to a new
    /// line number - left every shifted line still showing whatever was
    /// cached for that line number *before* the shift, stale and wrong,
    /// confirmed live. <c>UpdateCache()</c> forces a full re-query
    /// instead of relying on that selective invalidation.
    /// </summary>
    public void Rebuild(string text)
    {
        _lineHighlighting.Clear();

        // Diagnostics are the parser's job (see ScriptDocument's own
        // separate diagnostics pass) - this only ever needs the token
        // stream, so a throwaway sink is fine here.
        var discardedDiagnostics = new List<BcsDiagnostic>();
        var tokenizer = new BcsTokenizer(new BinaryReader(new MemoryStream(Encoding.UTF8.GetBytes(text))), discardedDiagnostics);

        BcsTokenType? previousSignificant = null;
        while (true)
        {
            var token = tokenizer.ReadToken();
            if (token.Type == BcsTokenType.EndOfInput) break;

            var color = ColorFor(token.Type, previousSignificant);
            if (color != null) Highlight(token, color.Value);

            if (token.Type is not (BcsTokenType.Whitespace or BcsTokenType.Newline)) previousSignificant = token.Type;
        }

        UpdateCache();
    }

    /// <summary>
    /// Only a <see cref="BcsTokenType.BlockComment"/> can span more than
    /// one line in BCS's grammar (every other token kind errors out on a
    /// raw newline instead) - real, not theoretical: <see cref="BcsToken.Length"/>
    /// falls back to <see cref="BcsToken.Value"/>'s own length for a
    /// multi-line token (there's no single "how many columns" number for
    /// one), which on the *starting* line alone produced a column far
    /// past that line's actual text, with no highlight entries at all for
    /// the lines the comment continues onto - splitting on the embedded
    /// newlines <see cref="BcsTokenizer"/>'s own comment text already
    /// contains, and highlighting each line segment it touches, fixes
    /// both: the bogus end-of-line column and the missing lines.
    /// </summary>
    private void Highlight(BcsToken token, Color color)
    {
        var line = token.Line - 1;
        var startColumn = token.Column - 1;

        if (!token.Value.Contains('\n'))
        {
            var lineMap = GetOrAddLine(line);
            lineMap[startColumn] = new GodotDictionary { { "color", color } };
            lineMap[startColumn + token.Length] = new GodotDictionary { { "color", DefaultColor } };
            return;
        }

        var segments = token.Value.Split('\n');
        for (var i = 0; i < segments.Length; i++)
        {
            var column = i == 0 ? startColumn : 0;
            var lineMap = GetOrAddLine(line + i);
            lineMap[column] = new GodotDictionary { { "color", color } };
            lineMap[column + segments[i].Length] = new GodotDictionary { { "color", DefaultColor } };
        }
    }

    private GodotDictionary GetOrAddLine(int line)
    {
        if (_lineHighlighting.TryGetValue(line, out var existing)) return existing;
        var created = new GodotDictionary();
        _lineHighlighting[line] = created;
        return created;
    }

    /// <summary>
    /// <see cref="BcsTokenType.Hash"/> always colors as a preprocessor
    /// directive - BCS has no other use for a bare <c>#</c>. The
    /// directive's own *name* (<c>define</c>, <c>include</c>, ...) is a
    /// plain <see cref="BcsTokenType.Identifier"/> at the tokenizer level
    /// (confirmed: none of these are real reserved words - see
    /// <see cref="BcsParser"/>'s own remarks), so there's no token type to
    /// switch on for it the way keywords work - <paramref name="previousSignificant"/>
    /// (the last non-whitespace/newline token seen, tracked by
    /// <see cref="Rebuild"/>) is what lets this recognize "the identifier
    /// immediately after a #" regardless of which directive it actually
    /// is, known or not - matching a real compiler's own lexical
    /// position-based recognition rather than a hardcoded directive-name
    /// list that would just duplicate <see cref="BcsParser"/>'s own.
    /// </summary>
    private static Color? ColorFor(BcsTokenType type, BcsTokenType? previousSignificant) => type switch
    {
        BcsTokenType.Hash => PreprocessorColor,
        BcsTokenType.Identifier when previousSignificant == BcsTokenType.Hash => PreprocessorColor,
        BcsTokenType.LitString or BcsTokenType.LitChar => LiteralColor,
        BcsTokenType.LitDecimal or BcsTokenType.LitOctal or BcsTokenType.LitHex or
            BcsTokenType.LitBinary or BcsTokenType.LitFixed or BcsTokenType.LitRadix => NumberColor,
        BcsTokenType.LineComment or BcsTokenType.BlockComment => CommentColor,
        BcsTokenType.TypeName => KeywordColor,
        _ when BcsTokenizer.ReservedWordTypes.Contains(type) => KeywordColor,
        _ => null,
    };

    public override GodotDictionary _GetLineSyntaxHighlighting(int line) =>
        _lineHighlighting.TryGetValue(line, out var map) ? map : new GodotDictionary();
}
