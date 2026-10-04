namespace DoomArchitect.Core.ZDoom.Bcs;

/// <summary>
/// One real, stored `#define`/`#libdefine` - name, optional parameter
/// list (function-like) or none (object-like), and the raw, unexpanded
/// body token list - built by <see cref="BcsPreprocessor"/>, which also
/// owns actually expanding it. Kept separate from <see cref="BcsDefineDirective"/>
/// (the AST node completion/hover/go-to-def already use) - this type is
/// the preprocessor's own internal bookkeeping, not a parser AST node;
/// <see cref="BcsParser.Parse"/> builds one `BcsDefineDirective` per
/// entry the preprocessor reports via <c>Macros</c> after a full parse,
/// preserving that existing feature unchanged.
/// </summary>
internal sealed class BcsMacroDefinition
{
    public string Name { get; init; } = string.Empty;

    /// <summary>Plain mutable, not <c>init</c> - confirmed real grammar (<c>dirc.c</c>'s own <c>read_macro_name</c>/<c>read_macro_param_list</c>): whether a macro is function-like is only known *after* reading its name, by checking - whitespace-sensitively - whether a `(` immediately follows it, which <see cref="BcsPreprocessor.ReadDefine"/> only discovers partway through building this object.</summary>
    public bool IsFunctionLike { get; set; }

    /// <summary>The real `...` trailing variadic parameter, confirmed from <c>dirc.c</c>'s own <c>read_param_list</c> - its real name in the body is always <c>__VA_ARGS__</c>, already the last entry in <see cref="Parameters"/> when this is true.</summary>
    public bool IsVariadic { get; set; }

    public List<string> Parameters { get; } = new();

    /// <summary>Raw, unexpanded - confirmed real grammar (<c>dirc.c</c>'s own <c>read_body</c>) stops at the first unescaped newline.</summary>
    public List<BcsToken> Body { get; } = new();

    public int Line { get; init; }
    public int Column { get; init; }
    public string DocComment { get; init; } = string.Empty;
}
