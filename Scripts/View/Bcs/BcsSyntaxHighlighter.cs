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
    private static readonly Color DefaultColor = new("#d4d4d4");

    private readonly Dictionary<int, GodotDictionary> _lineHighlighting = new();

    /// <summary>Re-tokenizes <paramref name="text"/> and rebuilds the per-line cache <see cref="_GetLineSyntaxHighlighting"/> reads from - call whenever the owning <c>CodeEdit</c>'s text changes.</summary>
    public void Rebuild(string text)
    {
        _lineHighlighting.Clear();

        // Diagnostics are the parser's job (see ScriptDocument's own
        // separate diagnostics pass) - this only ever needs the token
        // stream, so a throwaway sink is fine here.
        var discardedDiagnostics = new List<BcsDiagnostic>();
        var tokenizer = new BcsTokenizer(new BinaryReader(new MemoryStream(Encoding.UTF8.GetBytes(text))), discardedDiagnostics);

        while (true)
        {
            var token = tokenizer.ReadToken();
            if (token.Type == BcsTokenType.EndOfInput) break;

            var color = ColorFor(token.Type);
            if (color == null) continue;

            // BcsToken positions are 1-based (matching the real compiler); Godot line/column indices are 0-based.
            var line = token.Line - 1;
            var startColumn = token.Column - 1;
            var endColumn = startColumn + token.Length;

            var lineMap = GetOrAddLine(line);
            lineMap[startColumn] = new GodotDictionary { { "color", color.Value } };
            lineMap[endColumn] = new GodotDictionary { { "color", DefaultColor } };
        }
    }

    private GodotDictionary GetOrAddLine(int line)
    {
        if (_lineHighlighting.TryGetValue(line, out var existing)) return existing;
        var created = new GodotDictionary();
        _lineHighlighting[line] = created;
        return created;
    }

    private static Color? ColorFor(BcsTokenType type) => type switch
    {
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
