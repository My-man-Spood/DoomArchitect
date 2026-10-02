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

/// <summary>The whole parsed file - an ordered top-level node list, exactly as encountered (directives and declarations can interleave in real BCS source).</summary>
public sealed class BcsCompilationUnit : BcsNode
{
    public List<BcsNode> Members { get; } = new();
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
/// flags, for this first pass).
/// </summary>
public sealed class BcsScriptDeclaration : BcsNode
{
    public string Number { get; init; } = string.Empty;
    public string? TypeKeyword { get; init; }
    public List<string> FlagTokens { get; } = new();
    public int BodyLine { get; init; }
    public int BodyColumn { get; init; }
}

/// <summary><c>special ...;</c> - header captured as raw token text for this pass; not individually parsed into name/id/argument-count fields yet.</summary>
public sealed class BcsSpecialDeclaration : BcsNode
{
    public List<string> HeaderTokens { get; } = new();
}

/// <summary><c>function ... { ... }</c> - same "capture the header, skip the body" treatment as <see cref="BcsScriptDeclaration"/>.</summary>
public sealed class BcsFunctionDeclaration : BcsNode
{
    public List<string> HeaderTokens { get; } = new();
}

/// <summary><c>enum [name] { ... };</c>.</summary>
public sealed class BcsEnumDeclaration : BcsNode
{
    public string? Name { get; init; }
}

/// <summary>Covers bare/<c>global</c>/<c>world</c>/<c>static</c>/<c>const</c> variable declarations - a type keyword plus raw declarator tokens up to the terminating <c>;</c>.</summary>
public sealed class BcsVariableDeclaration : BcsNode
{
    public string TypeKeyword { get; init; } = string.Empty;
    public List<string> DeclaratorTokens { get; } = new();
}

/// <summary>A catch-all placeholder wherever error recovery had to skip tokens, so the AST stays structurally complete enough to answer "where exactly did this stop making sense" even around a real syntax error.</summary>
public sealed class BcsSyntaxErrorNode : BcsNode
{
    public string SkippedText { get; init; } = string.Empty;
}
