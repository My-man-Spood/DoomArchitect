namespace DoomArchitect.Core.ZDoom.Bcs;

/// <summary>
/// The common shape every <see cref="BcsParser"/> AST node shares - just
/// enough of a source span to support diagnostics today and hover/go-to-
/// definition later, without committing to any richer shape yet (this
/// pass's AST is deliberately minimal - see <see cref="BcsParser"/>'s own
/// remarks).
/// </summary>
public abstract class BcsNode
{
    public int Line { get; init; }
    public int Column { get; init; }
}

/// <summary>What a <see cref="BcsSymbol"/> actually is - just enough to pick a sensible completion-item icon on either consumer (Godot's <c>CodeCompletionKind</c>, LSP's <c>CompletionItemKind</c>), not a real type system.</summary>
public enum BcsSymbolKind
{
    Function,
    Variable,
    EnumType,
    EnumMember,
    Macro,
}

/// <summary>
/// One declared name, flattened out of wherever in the AST it was found
/// (a function's own name, one of its parameters, a local inside a
/// script/function body, an enum member, ...), carrying the position of
/// its own declaring token - needed for go-to-definition (where to jump)
/// and scope resolution (<see cref="BcsCompilationUnit.CollectSymbolsVisibleAt"/>
/// needs to know which function/script body, if any, a symbol's position
/// falls inside). Still deliberately *not* scope-aware on its own - two
/// symbols with the same <c>(Name, Kind)</c> but different positions (the
/// same local name redeclared in two different scripts) are now distinct
/// values rather than collapsing in a <c>HashSet</c>, which is correct for
/// go-to-definition (each has its own real location) even though it means
/// <see cref="BcsCompilationUnit.CollectSymbols"/>'s own dedup no longer
/// collapses same-named duplicates for free - callers that want a
/// name-unique completion list dedupe explicitly by <c>(Name, Kind)</c>.
/// </summary>
public readonly record struct BcsSymbol(string Name, BcsSymbolKind Kind, int Line, int Column);

/// <summary>The whole parsed file - an ordered top-level node list, exactly as encountered (directives and declarations can interleave in real BCS source).</summary>
public sealed class BcsCompilationUnit : BcsNode
{
    public List<BcsNode> Members { get; } = new();

    /// <summary>
    /// Every symbol visible everywhere in the file, regardless of cursor
    /// position - function/script/special names (a script only if
    /// <see cref="BcsScriptDeclaration.IsNamedScript"/>, since a bare
    /// script number isn't a real completable name), enum types and
    /// their members, top-level variable declarators, and <c>#define</c>d
    /// macro names. Deliberately excludes a function/script's own
    /// parameters and body locals - those are only visible from inside
    /// that specific body, which is exactly what
    /// <see cref="CollectSymbolsVisibleAt"/> and <see cref="FindDeclaration"/>
    /// add on top of this shared base.
    /// </summary>
    private IEnumerable<BcsSymbol> FileScopeSymbols()
    {
        foreach (var member in Members)
        {
            switch (member)
            {
                case BcsFunctionDeclaration function:
                    if (!string.IsNullOrEmpty(function.Name))
                        yield return new BcsSymbol(function.Name, BcsSymbolKind.Function, function.NameLine, function.NameColumn);
                    break;
                case BcsSpecialDeclaration special:
                    foreach (var name in special.Names) yield return name;
                    break;
                case BcsScriptDeclaration script:
                    if (script.IsNamedScript)
                        yield return new BcsSymbol(script.Number, BcsSymbolKind.Function, script.NumberLine, script.NumberColumn);
                    break;
                case BcsEnumDeclaration @enum:
                    if (!string.IsNullOrEmpty(@enum.Name))
                        yield return new BcsSymbol(@enum.Name, BcsSymbolKind.EnumType, @enum.Line, @enum.Column);
                    foreach (var enumMember in @enum.MemberNames) yield return enumMember;
                    break;
                case BcsVariableDeclaration variable:
                    foreach (var name in variable.DeclaratorNames) yield return name;
                    break;
                case BcsDefineDirective define:
                    if (!string.IsNullOrEmpty(define.Name))
                        yield return new BcsSymbol(define.Name, BcsSymbolKind.Macro, define.Line, define.Column);
                    break;
            }
        }
    }

    /// <summary>
    /// Every declared name in the file, flattened - the simple, no-cursor-
    /// context view completion used before scope-awareness. No real
    /// lexical scoping: a local declared in one script shows up
    /// regardless of where in the file it's collected from. Kept around
    /// unchanged for whatever still wants the plain flat view (existing
    /// tests do); live completion now goes through
    /// <see cref="CollectSymbolsVisibleAt"/> instead.
    /// </summary>
    public IReadOnlyList<BcsSymbol> CollectSymbols()
    {
        var symbols = new HashSet<BcsSymbol>(FileScopeSymbols());

        foreach (var member in Members)
        {
            switch (member)
            {
                case BcsFunctionDeclaration function:
                    foreach (var parameter in function.ParameterNames) symbols.Add(parameter);
                    foreach (var local in function.BodyLocals) symbols.Add(local);
                    break;
                case BcsScriptDeclaration script:
                    foreach (var parameter in script.ParameterNames) symbols.Add(parameter);
                    foreach (var local in script.BodyLocals) symbols.Add(local);
                    break;
            }
        }

        return symbols.ToList();
    }

    /// <summary>
    /// Every symbol actually visible from <paramref name="line"/> (1-based,
    /// matching every other position this parser produces): everything
    /// from <see cref="FileScopeSymbols"/> plus - only for whichever
    /// function/script body <paramref name="line"/> falls inside, if any -
    /// that member's own parameters and body locals. This is the
    /// scope-aware replacement for <see cref="CollectSymbols"/> that both
    /// completion providers use.
    /// </summary>
    public IReadOnlyList<BcsSymbol> CollectSymbolsVisibleAt(int line)
    {
        var symbols = new List<BcsSymbol>(FileScopeSymbols());

        foreach (var member in Members)
        {
            if (member is BcsFunctionDeclaration function && WithinBody(line, function.BodyLine, function.BodyEndLine))
            {
                symbols.AddRange(function.ParameterNames);
                symbols.AddRange(function.BodyLocals);
            }
            else if (member is BcsScriptDeclaration script && WithinBody(line, script.BodyLine, script.BodyEndLine))
            {
                symbols.AddRange(script.ParameterNames);
                symbols.AddRange(script.BodyLocals);
            }
        }

        return symbols;
    }

    /// <summary>
    /// Resolves <paramref name="name"/> (matched case-insensitively - BCS
    /// itself is case-insensitive) to the single declaration it refers to
    /// when used at <paramref name="line"/>, for go-to-definition. Checks
    /// the enclosing function/script body's own parameters/locals first
    /// (correct shadowing: a local wins over a same-named global when
    /// queried from inside that local's own scope), then falls back to
    /// <see cref="FileScopeSymbols"/>. Returns <c>null</c>, not a default
    /// struct, when nothing matches.
    /// </summary>
    public BcsSymbol? FindDeclaration(string name, int line)
    {
        bool NameMatches(BcsSymbol symbol) => string.Equals(symbol.Name, name, StringComparison.OrdinalIgnoreCase);

        foreach (var member in Members)
        {
            if (member is BcsFunctionDeclaration function && WithinBody(line, function.BodyLine, function.BodyEndLine))
            {
                var local = function.ParameterNames.Concat(function.BodyLocals)
                    .Where(NameMatches).Select(s => (BcsSymbol?)s).FirstOrDefault();
                if (local != null) return local;
            }
            else if (member is BcsScriptDeclaration script && WithinBody(line, script.BodyLine, script.BodyEndLine))
            {
                var local = script.ParameterNames.Concat(script.BodyLocals)
                    .Where(NameMatches).Select(s => (BcsSymbol?)s).FirstOrDefault();
                if (local != null) return local;
            }
        }

        return FileScopeSymbols().Where(NameMatches).Select(s => (BcsSymbol?)s).FirstOrDefault();
    }

    /// <summary><c>bodyLine &gt; 0</c> excludes a forward-declared function (<c>function foo();</c>, no body at all - never populated) from ever matching.</summary>
    private static bool WithinBody(int line, int bodyLine, int bodyEndLine) => bodyLine > 0 && line >= bodyLine && line <= bodyEndLine;
}

/// <summary><c>#include "path"</c> - a pure textual inclusion (can duplicate-define if the same file is pulled in more than once - see this project's own memory on the real ACS/BCS `#include` vs `#import` distinction).</summary>
public sealed class BcsIncludeDirective : BcsNode
{
    public string Path { get; init; } = string.Empty;
}

/// <summary><c>#import "path"</c> - links against a separately-compiled library, safe to import from multiple consumers (unlike <c>#include</c>).</summary>
public sealed class BcsImportDirective : BcsNode
{
    public string Path { get; init; } = string.Empty;
}

/// <summary>
/// <c>#define NAME ...</c> - only <see cref="Name"/> is extracted
/// (original casing preserved, same as every other declared name); the
/// macro's value/parameter list is still not modeled at all, deliberate
/// scope limiting - this exists purely so a `#define`d name can be
/// offered by completion, not to support macro expansion (which would
/// need a real preprocessor pass this project doesn't have).
/// </summary>
public sealed class BcsDefineDirective : BcsNode
{
    public string? Name { get; init; }
}

/// <summary>
/// <c>#library "name"</c>/<c>library "name";</c> - this pass accepts
/// either a <c>#</c>-prefixed or bare keyword spelling defensively, since
/// exactly where this attaches in the real grammar wasn't pinned down
/// during planning (it's absent from the confirmed <c>#</c>-directive
/// table in <c>dirc.c</c> - verify against <c>src/parse/stmt.c</c> before
/// treating this node's shape as authoritative).
/// </summary>
public sealed class BcsLibraryDirective : BcsNode
{
    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// <c>script N (type) flag... { ... }</c> - <see cref="Number"/> is the
/// script's number-or-name token's raw text (not resolved/validated),
/// <see cref="FlagTokens"/> is the raw, unresolved token text list for
/// whatever sits between the optional <c>(type)</c> and the opening
/// <c>{</c> (e.g. <c>open</c>, <c>net</c>) - resolving these into typed
/// booleans is semantic-layer work, explicitly out of scope here (see
/// <see cref="BcsParser"/>'s own remarks on why raw tokens, not resolved
/// flags, for this first pass). <see cref="ParameterNames"/> is
/// extracted from that same optional parenthesized group while real
/// tokens are still available - deliberately sidesteps ever having to
/// decide whether that group was a bare type/flag marker like
/// <c>(open)</c> (no preceding type keyword before an identifier inside
/// it, so nothing is extracted) or a real parameter list like
/// <c>(int a, int b)</c> (extracts <c>a</c>/<c>b</c>) - both are valid,
/// see <see cref="BcsParser"/>'s own remarks on this. <see cref="IsNamedScript"/>
/// records whether <see cref="Number"/> came from a string or bare
/// identifier token (a real, referenceable name, e.g. <c>script "main" open {...}</c>)
/// rather than a plain number - <see cref="BcsCompilationUnit"/>'s symbol
/// methods need this to know whether <see cref="Number"/> itself is a
/// meaningful completable/navigable name, with <see cref="NumberLine"/>/
/// <see cref="NumberColumn"/> giving its own declaration position for
/// go-to-definition. <see cref="BodyLine"/>/<see cref="BodyColumn"/> and
/// <see cref="BodyEndLine"/>/<see cref="BodyEndColumn"/> are the body's
/// opening <c>{</c> and matching closing <c>}</c> positions respectively -
/// the span <see cref="BcsCompilationUnit.CollectSymbolsVisibleAt"/> uses
/// to decide whether a cursor line is "inside this script."
/// </summary>
public sealed class BcsScriptDeclaration : BcsNode
{
    public string Number { get; init; } = string.Empty;
    public bool IsNamedScript { get; init; }
    public int NumberLine { get; init; }
    public int NumberColumn { get; init; }
    public string? TypeKeyword { get; init; }
    public List<string> FlagTokens { get; } = new();
    public List<BcsSymbol> ParameterNames { get; } = new();
    public List<BcsSymbol> BodyLocals { get; } = new();
    public int BodyLine { get; init; }
    public int BodyColumn { get; init; }
    public int BodyEndLine { get; init; }
    public int BodyEndColumn { get; init; }
}

/// <summary>
/// <c>special ...;</c> - header captured as raw token text for this pass
/// (<see cref="HeaderTokens"/>), same as before. <see cref="Names"/> is
/// new: a <c>special</c> statement can declare several, comma-separated
/// (confirmed from the real compiler's own <c>src/parse/dec.c</c>,
/// <c>p_read_special_list</c>) and each one's own real shape is
/// <c>[-]decimal ':' identifier '(' ... ')' ...</c> - deliberately
/// <b>not</b> a <c>Parameters</c> field to match <see cref="BcsFunctionDeclaration"/>:
/// confirmed from that same source (<c>read_special_param</c>) that
/// special parameters are declared by type only and are never named at
/// all, so there is nothing to extract there.
/// </summary>
public sealed class BcsSpecialDeclaration : BcsNode
{
    public List<string> HeaderTokens { get; } = new();
    public List<BcsSymbol> Names { get; } = new();
}

/// <summary>
/// <c>function TYPE NAME ( params ) { ... }</c> - <see cref="HeaderTokens"/>
/// is the original raw capture, kept unchanged; <see cref="Name"/>/
/// <see cref="ParameterNames"/> are extracted alongside it, while real
/// tokens (not yet flattened to plain strings) are still available -
/// re-deriving them later from <see cref="HeaderTokens"/> alone would be
/// genuinely ambiguous (a reserved word, an identifier with the same
/// text, and punctuation all become indistinguishable once flattened).
/// <see cref="NameLine"/>/<see cref="NameColumn"/> is the name identifier's
/// own position (for go-to-definition on the function itself).
/// <see cref="BodyLocals"/> is every declaration-shaped name found inside
/// the function's own body - see <see cref="BcsParser"/>'s
/// <c>DeclarationScanner</c> for how "declaration-shaped" is recognized
/// without a real expression/statement grammar. <see cref="BodyLine"/>/
/// <see cref="BodyColumn"/>/<see cref="BodyEndLine"/>/<see cref="BodyEndColumn"/>
/// are left at their default (0) for a forward declaration (<c>function
/// foo();</c>, no body at all) - <see cref="BcsCompilationUnit"/>'s
/// <c>WithinBody</c> check treats a 0 start as "never inside."
/// </summary>
public sealed class BcsFunctionDeclaration : BcsNode
{
    public List<string> HeaderTokens { get; } = new();
    public string? Name { get; init; }
    public int NameLine { get; init; }
    public int NameColumn { get; init; }
    public List<BcsSymbol> ParameterNames { get; } = new();
    public List<BcsSymbol> BodyLocals { get; } = new();
    public int BodyLine { get; init; }
    public int BodyColumn { get; init; }
    public int BodyEndLine { get; init; }
    public int BodyEndColumn { get; init; }
}

/// <summary><c>enum [name] { ... };</c> - <see cref="MemberNames"/> is new: previously the body was skipped with zero extraction, losing every member name entirely.</summary>
public sealed class BcsEnumDeclaration : BcsNode
{
    public string? Name { get; init; }
    public List<BcsSymbol> MemberNames { get; } = new();
}

/// <summary>
/// Covers bare/<c>global</c>/<c>world</c>/<c>static</c>/<c>const</c>
/// variable declarations - a type keyword plus raw declarator tokens up
/// to the terminating <c>;</c> (<see cref="DeclaratorTokens"/>, kept
/// unchanged). <see cref="DeclaratorNames"/> is new: the actual declared
/// name(s) extracted live while parsing, same reasoning as
/// <see cref="BcsFunctionDeclaration.Name"/> - handles multiple
/// comma-separated declarators (<c>int a, b, c;</c>) and the real
/// indexed form (<c>global int 0:a, 1:b;</c>, confirmed from
/// <c>dec.c</c>'s <c>read_instance_list</c>/<c>read_storage_index</c>).
/// </summary>
public sealed class BcsVariableDeclaration : BcsNode
{
    public string TypeKeyword { get; init; } = string.Empty;
    public List<string> DeclaratorTokens { get; } = new();
    public List<BcsSymbol> DeclaratorNames { get; } = new();
}

/// <summary>A catch-all placeholder wherever error recovery had to skip tokens, so the AST stays structurally complete enough to answer "where exactly did this stop making sense" even around a real syntax error.</summary>
public sealed class BcsSyntaxErrorNode : BcsNode
{
    public string SkippedText { get; init; } = string.Empty;
}
