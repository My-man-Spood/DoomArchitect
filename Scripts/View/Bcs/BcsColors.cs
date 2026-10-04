using DoomArchitect.Core.ZDoom.Bcs;
using Godot;

/// <summary>
/// The single source of truth for "what color does this BCS token get" -
/// shared by <see cref="BcsSyntaxHighlighter"/> (the live editor buffer)
/// and <see cref="BcsBbcodeFormatter"/> (hover tooltips), so a hovered
/// signature/type reads with the exact same coloring the user already
/// sees in the buffer itself, not a separate palette that could drift
/// out of sync with it.
/// </summary>
public static class BcsColors
{
    // Chosen to read clearly against this project's existing dark editor
    // theme (Assets/BaseTheme.tres) - not an attempt to match any one
    // external editor's exact palette.
    public static readonly Color Keyword = new("#569cd6");
    public static readonly Color Literal = new("#ce9178");
    public static readonly Color Number = new("#b5cea8");
    public static readonly Color Comment = new("#6a9955");
    public static readonly Color Preprocessor = new("#c586c0");
    public static readonly Color Default = new("#d4d4d4");

    /// <summary>
    /// <see cref="BcsTokenType.Hash"/> always colors as a preprocessor
    /// directive - BCS has no other use for a bare <c>#</c>. The
    /// directive's own *name* (<c>define</c>, <c>include</c>, ...) is a
    /// plain <see cref="BcsTokenType.Identifier"/> at the tokenizer level
    /// (confirmed: none of these are real reserved words - see
    /// <c>BcsParser</c>'s own remarks), so there's no token type to
    /// switch on for it the way keywords work - <paramref name="previousSignificant"/>
    /// (the last non-whitespace/newline token seen) is what lets this
    /// recognize "the identifier immediately after a #" regardless of
    /// which directive it actually is, known or not.
    /// </summary>
    public static Color? ColorFor(BcsTokenType type, BcsTokenType? previousSignificant) => type switch
    {
        BcsTokenType.Hash => Preprocessor,
        BcsTokenType.Identifier when previousSignificant == BcsTokenType.Hash => Preprocessor,
        BcsTokenType.LitString or BcsTokenType.LitChar => Literal,
        BcsTokenType.LitDecimal or BcsTokenType.LitOctal or BcsTokenType.LitHex or
            BcsTokenType.LitBinary or BcsTokenType.LitFixed or BcsTokenType.LitRadix => Number,
        BcsTokenType.LineComment or BcsTokenType.BlockComment => Comment,
        BcsTokenType.TypeName => Keyword,
        _ when BcsTokenizer.ReservedWordTypes.Contains(type) => Keyword,
        _ => null,
    };
}
