using System.Collections.Generic;
using System.IO;
using System.Text;
using DoomArchitect.Core.ZDoom.Bcs;

/// <summary>
/// Prepares hover text for display in a BBCode-enabled
/// <see cref="Godot.RichTextLabel"/> tooltip (see
/// <c>BcsCodeEdit._MakeCustomTooltip</c>) - two distinct paths for two
/// distinct kinds of text, picked by the caller (<c>ScriptDocument.GetBcsTooltip</c>),
/// since they need different handling and there's no way to tell them
/// apart from the string alone:
/// <list type="bullet">
/// <item><see cref="ColorizeCode"/> - a <see cref="BcsSymbol.Describe"/>
/// result, e.g. <c>"function int Add(int a, int b)"</c>. Real,
/// well-formed BCS-ish syntax, safe to re-tokenize and re-emit: wraps
/// each colorable token in a <c>[color=#rrggbb]</c> tag using
/// <see cref="BcsColors"/>, the exact same palette
/// <see cref="BcsSyntaxHighlighter"/> paints the live editor buffer
/// with, so a hovered signature reads with the same coloring the user
/// already sees in their own code.</item>
/// <item><see cref="EscapePlainText"/> - a parser diagnostic's own
/// message. Plain English prose, not code (e.g. <c>"expected ']', got '{'"</c>) -
/// deliberately *not* re-tokenized: a real compiler diagnostic can
/// legitimately quote punctuation like that, and a lexer's token
/// <em>value</em> strips a literal's own delimiters (confirmed live: a
/// quoted <c>']'</c> re-tokenizes to just <c>]</c>, silently dropping
/// the surrounding quotes) - exactly the kind of information loss this
/// path avoids by never tokenizing prose in the first place. Only
/// escapes the two characters that would otherwise be misread as a
/// BBCode tag.</item>
/// </list>
/// </summary>
public static class BcsBbcodeFormatter
{
    public static string ColorizeCode(string text)
    {
        var sb = new StringBuilder();

        var discardedDiagnostics = new List<BcsDiagnostic>();
        var tokenizer = new BcsTokenizer(new BinaryReader(new MemoryStream(Encoding.UTF8.GetBytes(text))), discardedDiagnostics);

        BcsTokenType? previousSignificant = null;
        while (true)
        {
            var token = tokenizer.ReadToken();
            if (token.Type == BcsTokenType.EndOfInput) break;

            // RawValue (the original, pre-case-folded spelling) for an
            // Identifier/TypeName - Value alone would silently re-lowercase
            // a declared name (confirmed live: "Add" round-tripped as
            // "add") since that's the canonical, case-folded form a real
            // parser needs, not what a human should be shown back. Every
            // other token kind (keywords, punctuation, literals, numbers)
            // has no such distinction - RawValue is only ever set for
            // those two types in the first place (see BcsTokenizer.RawValue's
            // own remarks), so Value is already the right text there.
            var tokenText = token.Type is BcsTokenType.Identifier or BcsTokenType.TypeName ? token.RawValue : token.Value;
            var escaped = EscapeForBbcode(tokenText);
            var color = BcsColors.ColorFor(token.Type, previousSignificant);
            sb.Append(color is { } c ? $"[color=#{c.ToHtml(false)}]{escaped}[/color]" : escaped);

            if (token.Type is not (BcsTokenType.Whitespace or BcsTokenType.Newline)) previousSignificant = token.Type;
        }

        return sb.ToString();
    }

    public static string EscapePlainText(string text) => EscapeForBbcode(text);

    /// <summary><c>[lb]</c>/<c>[rb]</c> are <see cref="Godot.RichTextLabel"/>'s own documented escape sequences for a literal <c>[</c>/<c>]</c> that shouldn't be read as a tag.</summary>
    private static string EscapeForBbcode(string text) => text.Replace("[", "[lb]").Replace("]", "[rb]");
}
