using System.Text;

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

    /// <summary>Which file this `#define` actually came from - see <see cref="BcsNode.SourcePath"/>'s own remarks for the same convention. Threaded onto the <see cref="BcsDefineDirective"/> <see cref="BcsParser.Parse"/> builds for this entry.</summary>
    public string SourcePath { get; init; } = string.Empty;

    /// <summary>
    /// The real C-style signature text for hover, e.g.
    /// <c>"#define MAX_HEALTH 100"</c> or
    /// <c>"#define MAX(a, b) (a &gt; b ? a : b)"</c> - built once, here,
    /// from this macro's own real parameter list and raw (unexpanded)
    /// body tokens, user-requested once real macro definitions existed
    /// to show (before Phase 1, hovering a `#define`d name could only
    /// ever show a bare "macro NAME" label - there was nothing else to
    /// show). The variadic trailing parameter displays as <c>...</c>,
    /// not its real internal name <c>__VA_ARGS__</c>, matching what the
    /// user actually typed. A single space is inserted between two body
    /// tokens only when their real source columns had an actual gap -
    /// same heuristic <see cref="BcsPreprocessor.Stringize"/> already
    /// uses, safe here too since <see cref="BcsTokenizer"/> always
    /// tolerates whitespace variation when this gets re-tokenized for
    /// hover coloring (<c>BcsBbcodeFormatter.ColorizeCode</c>) - the
    /// only way omitting a needed space could ever merge two distinct
    /// tokens is a pair that had zero gap in the original source
    /// either, which by construction never needed one to begin with.
    /// </summary>
    public string BuildSignature()
    {
        var text = new StringBuilder("#define ").Append(Name);

        if (IsFunctionLike)
        {
            var displayParameters = new List<string>(Parameters);
            if (IsVariadic && displayParameters.Count > 0) displayParameters[^1] = "...";
            text.Append('(').Append(string.Join(", ", displayParameters)).Append(')');
        }

        for (var i = 0; i < Body.Count; i++)
        {
            var current = Body[i];
            text.Append(i == 0 ? " " : "");
            if (i > 0)
            {
                var previous = Body[i - 1];
                if (current.Line == previous.Line && current.Column > previous.Column + previous.Length) text.Append(' ');
            }

            text.Append(current.RawValue.Length > 0 ? current.RawValue : current.Value);
        }

        return text.ToString();
    }
}
