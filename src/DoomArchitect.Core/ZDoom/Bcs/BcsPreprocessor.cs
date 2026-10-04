using System.IO;
using System.Text;

namespace DoomArchitect.Core.ZDoom.Bcs;

/// <summary>
/// A new layer sitting between the unchanged <see cref="BcsTokenizer"/>
/// and <see cref="BcsParser"/>, mirroring the real compiler's own
/// layering (confirmed from `zt-bcc`'s `src/parse/token/`: raw tokens →
/// preprocessor tokens → macro-expanded tokens → what the main parser
/// sees) rather than inventing a new shape. <see cref="BcsParser"/>
/// pulls from this exactly the way it used to pull from
/// <see cref="BcsTokenizer"/> directly (same two
/// <see cref="NextSignificantToken(bool)"/> overloads) - everything
/// returned is already fully macro-expanded.
///
/// This is Phase 1 (`#define`/`#undef` with parameter substitution and
/// recursive rescanning) plus Phase 2 (`#ifdef`/`#ifndef`/`#else`/
/// `#endif` conditional compilation, and a bare `#if` tolerated as
/// always-true - see <see cref="ReadIfdef"/>'s own remarks for exactly
/// why) plus Phase 3 (`#` stringizing and `##` token-pasting - see
/// <see cref="Stringize"/>/<see cref="Paste"/>) of a staged port.
/// Deliberately deferred to later phases, not silently dropped: real
/// `#if`/`#elif` condition *evaluation* (needs its own constant-
/// expression evaluator - until it exists, both are treated leniently
/// rather than correctly evaluated, see <see cref="ReadIfdef"/>); cross-
/// file macro visibility (a `#define` in an `#include`d file is not
/// yet visible to the including file - true textual-splice `#include`
/// semantics are a separate, later reconciliation with the existing
/// post-hoc symbol-merging <see cref="BcsProgram"/> already does for
/// completion/hover/go-to-def).
///
/// Only `#define`/`#libdefine`/`#undef`/`#if`/`#ifdef`/`#ifndef`/
/// `#elif`/`#else`/`#endif` are intercepted here, fully - they never
/// reach <see cref="BcsParser.ParseHashDirective"/> at all anymore.
/// Every other directive (`#include`, `#import`, `#library`, ...)
/// passes straight through unchanged, exactly as <see cref="BcsParser"/>
/// already handles it.
/// </summary>
internal sealed class BcsPreprocessor
{
    private readonly BcsTokenizer _tokenizer;
    private readonly List<BcsDiagnostic> _diagnostics;

    // BCS is case-insensitive throughout this project - same reasoning applies to macro names.
    private readonly Dictionary<string, BcsMacroDefinition> _macros = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<BcsMacroDefinition> _macroOrder = new();

    // Fully-expanded output waiting to be drained one token at a time -
    // never re-checked for further expansion once enqueued (that
    // already happened recursively before anything was enqueued; see
    // ExpandTokenList's own remarks on why re-checking here would
    // actually break the self-reference guard).
    private readonly Queue<BcsToken> _pending = new();

    // A handful of tokens peeked one step ahead and not consumed after
    // all (e.g. "was the directive after '#' really define/undef?",
    // "does this function-like macro name actually have a '(' right
    // after it?") - checked before ever asking the tokenizer for more.
    private readonly Stack<BcsToken> _pushback = new();

    // Macro names currently being expanded, anywhere in the current
    // nested-expansion chain - confirmed real self-reference behavior
    // (dirc.c's own TK_MACRONAME marking): a macro's own name appearing
    // inside its own expansion is never re-expanded, but a *different*
    // macro encountered during that same expansion still is.
    private readonly HashSet<string> _expanding = new(StringComparer.OrdinalIgnoreCase);

    // One entry per currently-open #if/#ifdef/#ifndef block (confirmed
    // real structure: dirc.c's own push_ifdirc/pop_ifdirc stack) - true
    // once *some* branch in this chain (the original if, an elif, or
    // the else) has been entered, which is what makes a later #elif/
    // #else in the same chain correctly skip even if encountered.
    private readonly Stack<bool> _conditionalBranchTaken = new();

    public BcsPreprocessor(BcsTokenizer tokenizer, List<BcsDiagnostic> diagnostics)
    {
        _tokenizer = tokenizer;
        _diagnostics = diagnostics;
    }

    /// <summary>Every `#define`/`#libdefine` encountered, in source order, even one later shadowed by a redefinition - <see cref="BcsParser.Parse"/> builds one <see cref="BcsDefineDirective"/> per entry after a full parse, preserving the existing completion/hover/go-to-def-for-macro-names feature unchanged.</summary>
    public IReadOnlyList<BcsMacroDefinition> Macros => _macroOrder;

    public BcsToken NextSignificantToken(bool includeNewlines = false) => NextSignificantToken(includeNewlines, out _);

    public BcsToken NextSignificantToken(bool includeNewlines, out List<BcsToken> skippedComments)
    {
        var comments = new List<BcsToken>();

        while (true)
        {
            if (_pending.Count > 0)
            {
                // Already fully expanded (recursively rescanned) before being enqueued - deliberately not re-checked here, see ExpandTokenList's own remarks.
                skippedComments = comments;
                return _pending.Dequeue();
            }

            var token = PullOneRaw(includeNewlines, out var newComments);
            comments.AddRange(newComments);

            if (token.Type == BcsTokenType.Hash)
            {
                var directiveName = PullOneRaw(includeNewlines: false, out var moreComments);
                comments.AddRange(moreComments);

                // Identifier covers every directive name here except "if"
                // and "else" - confirmed real bug, found live: those two
                // collide with actual BCS statement keywords
                // (BcsTokenType.If/Else, used for real if-statements), so
                // the tokenizer gives them their own dedicated token types
                // instead of Identifier, same as any other reserved word.
                // "ifdef"/"ifndef"/"elif" aren't reserved words anywhere
                // else in the language, so they tokenize as plain
                // Identifier and never needed this.
                if (directiveName.Type is BcsTokenType.Identifier or BcsTokenType.If or BcsTokenType.Else)
                {
                    if (string.Equals(directiveName.Value, "define", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(directiveName.Value, "libdefine", StringComparison.OrdinalIgnoreCase))
                    {
                        ReadDefine(token, BcsParser.ExtractDocComment(comments, token.Line));
                        comments = new List<BcsToken>(); // the whole directive line is gone - start fresh for whatever follows
                        continue;
                    }

                    if (string.Equals(directiveName.Value, "undef", StringComparison.OrdinalIgnoreCase))
                    {
                        ReadUndef();
                        comments = new List<BcsToken>();
                        continue;
                    }

                    if (directiveName.Value is "ifdef" or "ifndef" or "if")
                    {
                        ReadIfdef(directiveName.Value);
                        comments = new List<BcsToken>();
                        continue;
                    }

                    if (directiveName.Value is "elif" or "else")
                    {
                        ReadElseOrElif(directiveName);
                        comments = new List<BcsToken>();
                        continue;
                    }

                    if (string.Equals(directiveName.Value, "endif", StringComparison.OrdinalIgnoreCase))
                    {
                        ReadEndif(directiveName);
                        comments = new List<BcsToken>();
                        continue;
                    }
                }

                // Not ours - every other directive stays BcsParser's own responsibility, untouched.
                PushBack(directiveName);
                skippedComments = comments;
                return token;
            }

            if (token.Type == BcsTokenType.Identifier && TryStartExpansion(token, includeNewlines))
            {
                continue; // the (fully expanded) result is now in _pending
            }

            if (token.Type == BcsTokenType.EndOfInput && _conditionalBranchTaken.Count > 0)
            {
                // Confirmed real behavior (dirc.c's own p_confirm_ifdircs_closed) - one diagnostic per still-open block would be more faithful, but a single one is enough to flag the real mistake (a missing #endif) without piling on.
                _diagnostics.Add(new BcsDiagnostic("unterminated #if/#ifdef/#ifndef - missing #endif", token.Line, token.Column));
                _conditionalBranchTaken.Clear();
            }

            skippedComments = comments;
            return token;
        }
    }

    private BcsToken PullOneRaw(bool includeNewlines, out List<BcsToken> comments)
    {
        if (_pushback.Count > 0)
        {
            comments = new List<BcsToken>();
            return _pushback.Pop();
        }

        return _tokenizer.NextSignificantToken(includeNewlines, out comments);
    }

    private void PushBack(BcsToken token) => _pushback.Push(token);

    /// <summary>
    /// <c>#define NAME [(params)] [body]</c> - confirmed real grammar
    /// (`dirc.c`'s own `read_define`/`read_macro_name`/
    /// `read_macro_param_list`/`read_macro_body`). Function-like vs.
    /// object-like is whitespace-sensitive (confirmed: the real
    /// compiler reads the token right after the name via its own raw,
    /// non-skipping stream read specifically for this check, not its
    /// normal whitespace-skipping one) - `#define FOO(x)` (no space) is
    /// function-like; `#define FOO (x)` (a space) is object-like with a
    /// body that happens to start with a parenthesized expression. Our
    /// own <see cref="BcsTokenizer.NextSignificantToken(bool)"/> always
    /// skips whitespace, so this one spot goes around it and calls
    /// <see cref="BcsTokenizer.ReadToken"/> directly.
    /// </summary>
    private void ReadDefine(BcsToken hashToken, string docComment)
    {
        var nameToken = PullOneRaw(includeNewlines: false, out _);
        if (nameToken.Type != BcsTokenType.Identifier)
        {
            _diagnostics.Add(new BcsDiagnostic("expected a macro name after '#define'", nameToken.Line, nameToken.Column));
            SkipToEndOfLine();
            return;
        }

        var macro = new BcsMacroDefinition
        {
            Name = nameToken.RawValue,
            Line = nameToken.Line,
            Column = nameToken.Column,
            DocComment = docComment,
        };

        var raw = _tokenizer.ReadToken();
        if (raw.Type == BcsTokenType.OpenParen)
        {
            ReadMacroParameters(macro);
        }
        else if (raw.Type != BcsTokenType.Whitespace)
        {
            PushBack(raw);
        }

        ReadMacroBody(macro);
        ValidateMacroBody(macro);

        _macros[macro.Name] = macro; // last definition wins for lookup - real redefinition-conflict diagnostics are out of scope for this phase
        _macroOrder.Add(macro); // every definition still gets its own completion/hover/go-to-def entry, redefinition or not - matches this project's own existing position-based symbol identity
    }

    /// <summary><c>(name (',' name)* (',' '...')? )</c> or just <c>(...)</c> - confirmed real grammar (`dirc.c`'s own `read_param_list`); the opening `(` is already consumed by the caller.</summary>
    private void ReadMacroParameters(BcsMacroDefinition macro)
    {
        macro.IsFunctionLike = true;

        var token = _tokenizer.NextSignificantToken();
        if (token.Type == BcsTokenType.CloseParen) return; // no parameters at all

        while (true)
        {
            if (token.Type == BcsTokenType.Ellipsis)
            {
                macro.IsVariadic = true;
                macro.Parameters.Add("__VA_ARGS__");
                token = _tokenizer.NextSignificantToken();
                break;
            }

            if (token.Type != BcsTokenType.Identifier)
            {
                _diagnostics.Add(new BcsDiagnostic("expected a macro parameter name", token.Line, token.Column));
                break;
            }

            macro.Parameters.Add(token.RawValue);
            token = _tokenizer.NextSignificantToken();
            if (token.Type != BcsTokenType.Comma) break;
            token = _tokenizer.NextSignificantToken();
        }

        if (token.Type == BcsTokenType.CloseParen) return;
        _diagnostics.Add(new BcsDiagnostic("expected ')'", token.Line, token.Column));
    }

    /// <summary>
    /// Raw, unexpanded tokens up to (not including) the terminating
    /// newline - confirmed real grammar (`dirc.c`'s own `read_body`)
    /// stops there. Whitespace/comments inside the body aren't
    /// preserved - they only ever mattered for the function-like-vs-
    /// object-like check already handled in <see cref="ReadDefine"/>,
    /// and for `##`/stringizing, both deferred. Pulls via
    /// <see cref="PullOneRaw"/>, not the tokenizer directly - a real
    /// bug otherwise, confirmed live: for an empty body
    /// (`#define FEATURE` with nothing after the name), `ReadDefine`
    /// pushes the `Newline`/`EndOfInput` it already read back so this
    /// method can see it; calling the tokenizer directly here would
    /// silently skip straight past that pushed-back token and start
    /// consuming the *next* line as if it were part of this macro's
    /// own body.
    /// </summary>
    private void ReadMacroBody(BcsMacroDefinition macro)
    {
        while (true)
        {
            var token = PullOneRaw(includeNewlines: true, out _);
            if (token.Type is BcsTokenType.Newline or BcsTokenType.EndOfInput) break;
            macro.Body.Add(token);
        }
    }

    /// <summary>
    /// Define-time validation for `#`/`##` inside a macro body - confirmed
    /// real diagnostics from `dirc.c`'s own `read_body`/`read_body_item`,
    /// which catch these as soon as the macro is defined rather than at
    /// every later call site. A lone `#` inside an OBJECT-like macro's
    /// body is deliberately never checked here at all - confirmed real
    /// behavior (`TK_PROCESSEDHASH`): it's just a literal `#` there, no
    /// stringize meaning, nothing to validate.
    /// </summary>
    private void ValidateMacroBody(BcsMacroDefinition macro)
    {
        if (macro.Body.Count == 0) return;

        if (macro.Body[0].Type == BcsTokenType.HashHash)
        {
            _diagnostics.Add(new BcsDiagnostic("'##' operator at beginning of macro body", macro.Body[0].Line, macro.Body[0].Column));
        }

        if (macro.Body[^1].Type == BcsTokenType.HashHash)
        {
            _diagnostics.Add(new BcsDiagnostic("'##' operator at end of macro body", macro.Body[^1].Line, macro.Body[^1].Column));
        }

        for (var i = 0; i < macro.Body.Count; i++)
        {
            if (macro.Body[i].Type != BcsTokenType.Hash || !macro.IsFunctionLike) continue;

            var next = i + 1 < macro.Body.Count ? macro.Body[i + 1] : null;
            var isValidParam = next is { Type: BcsTokenType.Identifier } &&
                macro.Parameters.Any(p => string.Equals(p, next.Value, StringComparison.OrdinalIgnoreCase));

            if (!isValidParam)
            {
                _diagnostics.Add(new BcsDiagnostic(
                    $"'{(next is null ? "?" : TokenText(next))}' is not a parameter of macro '{macro.Name}'",
                    macro.Body[i].Line, macro.Body[i].Column));
            }
        }
    }

    /// <summary><c>#undef NAME</c> - not real `TK_*` grammar this project modeled before Phase 1, but cheap and real (confirmed from `dirc.c`'s own `read_undef`/`remove_macro`): simply removes a macro so later uses are no longer expanded.</summary>
    private void ReadUndef()
    {
        var token = _tokenizer.NextSignificantToken();
        if (token.Type == BcsTokenType.Identifier) _macros.Remove(token.Value);
        else _diagnostics.Add(new BcsDiagnostic("expected a macro name after '#undef'", token.Line, token.Column));

        SkipToEndOfLine();
    }

    private void SkipToEndOfLine()
    {
        while (_tokenizer.NextSignificantToken(includeNewlines: true) is { Type: not (BcsTokenType.Newline or BcsTokenType.EndOfInput) }) { }
    }

    /// <summary>
    /// <c>#ifdef NAME</c>/<c>#ifndef NAME</c> - confirmed real grammar
    /// (`dirc.c`'s own `read_ifdef`): active when the name is (for
    /// `ifdef`) or isn't (for `ifndef`) a currently-defined macro,
    /// confirmed via the exact same lookup <see cref="TryStartExpansion"/>
    /// already uses. A bare <c>#if</c> is tolerated here too but
    /// deliberately *not* evaluated - the real `p_eval_prep_expr` is a
    /// whole separate constant-expression evaluator this pass doesn't
    /// have yet. Rather than silently falling through to "unknown
    /// directive" (what would otherwise happen - `#if` was never in
    /// this pass's directive table before Phase 2 either) or guessing
    /// wrong in a way that could hide real code from completion, a bare
    /// `#if`'s condition is treated as always true: its own tokens are
    /// discarded and the branch is simply taken unconditionally. This
    /// is a real, deliberate divergence from the real compiler - not a
    /// bug - until the evaluator exists in a later phase.
    /// </summary>
    private void ReadIfdef(string directive)
    {
        var nameToken = _tokenizer.NextSignificantToken();
        var isDefined = nameToken.Type == BcsTokenType.Identifier && _macros.ContainsKey(nameToken.Value);
        SkipToEndOfLine(); // the rest of the line - a bare #if's own (unevaluated) condition tokens, or nothing more for #ifdef/#ifndef

        var branchActive = string.Equals(directive, "ifndef", StringComparison.OrdinalIgnoreCase) ? !isDefined
            : string.Equals(directive, "ifdef", StringComparison.OrdinalIgnoreCase) ? isDefined
            : true; // bare #if - always true, see this method's own remarks

        if (nameToken.Type != BcsTokenType.Identifier && directive != "if")
        {
            _diagnostics.Add(new BcsDiagnostic($"expected a macro name after '#{directive}'", nameToken.Line, nameToken.Column));
        }

        _conditionalBranchTaken.Push(branchActive);
        if (!branchActive) SkipInactiveRegion();
    }

    /// <summary>
    /// <c>#elif</c>/<c>#else</c> reached during *normal* (active)
    /// reading - meaning some earlier branch in this same chain was
    /// already taken (confirmed real semantics: once one branch of an
    /// if/elif/else chain runs, every later sibling is skipped
    /// regardless of its own condition - real `#else`'s condition is
    /// unconditional anyway, and a real `#elif`'s would need the same
    /// deferred evaluator `#if` does). <see cref="SkipInactiveRegion"/>
    /// is what actually *finds* an elif/else that should become active
    /// instead - this method only ever runs for one that shouldn't.
    /// </summary>
    private void ReadElseOrElif(BcsToken directiveName)
    {
        SkipToEndOfLine(); // an #elif's own (unevaluated) condition, or nothing more for #else
        if (_conditionalBranchTaken.Count == 0)
        {
            _diagnostics.Add(new BcsDiagnostic($"'#{directiveName.Value}' with no open '#if'/'#ifdef'/'#ifndef'", directiveName.Line, directiveName.Column));
            return;
        }

        SkipInactiveRegion(); // this chain already took a branch - skip past the rest of it, however many more elif/else sections follow, down to this level's own #endif
    }

    private void ReadEndif(BcsToken directiveName)
    {
        SkipToEndOfLine();
        if (_conditionalBranchTaken.Count == 0)
        {
            _diagnostics.Add(new BcsDiagnostic("'#endif' with no open '#if'/'#ifdef'/'#ifndef'", directiveName.Line, directiveName.Column));
            return;
        }

        _conditionalBranchTaken.Pop();
    }

    /// <summary>
    /// Skips forward - emitting nothing - until finding, at *this*
    /// level (tracking nested `#if`-family depth so a nested block's
    /// own `#endif` doesn't get mistaken for this level's), either an
    /// `#elif`/`#else` that should now become active (this chain
    /// hasn't taken a branch yet) or this level's own `#endif` (nothing
    /// becomes active - the whole block simply closes). Confirmed real
    /// structure (`dirc.c`'s own `find_endif`/`read_search_dirc`).
    /// `#elif`'s own condition is - again - not evaluated: the first
    /// `#elif` reached while searching is treated as the one to take,
    /// which is only correct when there's exactly one (a real divergence
    /// once a chain has more than one `#elif`, deferred along with real
    /// evaluation).
    /// </summary>
    private void SkipInactiveRegion()
    {
        var depth = 0;
        while (true)
        {
            var token = _tokenizer.NextSignificantToken(includeNewlines: true);
            if (token.Type == BcsTokenType.EndOfInput) return; // the EOF-level unclosed-#if check in NextSignificantToken itself will still fire for the outer block

            if (token.Type != BcsTokenType.Hash) continue;

            var name = _tokenizer.NextSignificantToken();
            // Same "if"/"else" vs. Identifier gotcha as the main dispatch loop (see its own remarks) - both are real keyword token types here, not Identifier.
            if (name.Type is not (BcsTokenType.Identifier or BcsTokenType.If or BcsTokenType.Else)) continue;

            if (name.Value is "if" or "ifdef" or "ifndef")
            {
                depth++;
                SkipToEndOfLine();
                continue;
            }

            if (depth > 0)
            {
                if (string.Equals(name.Value, "endif", StringComparison.OrdinalIgnoreCase)) depth--;
                SkipToEndOfLine();
                continue;
            }

            // depth == 0 - this directive belongs to the level we're actually searching for.
            if (name.Value is "elif" or "else")
            {
                SkipToEndOfLine();
                _conditionalBranchTaken.Pop();
                _conditionalBranchTaken.Push(true); // becomes active - resume normal reading right after this line
                return;
            }

            if (string.Equals(name.Value, "endif", StringComparison.OrdinalIgnoreCase))
            {
                SkipToEndOfLine();
                _conditionalBranchTaken.Pop(); // block closes with no branch taken - resume normal reading right after this line
                return;
            }
        }
    }

    /// <summary>
    /// If <paramref name="nameToken"/> is a defined macro name (and not
    /// already mid-expansion, the self-reference guard), consumes
    /// whatever the real invocation shape needs (nothing more, for an
    /// object-like macro; a balanced `(args)` for a function-like one -
    /// confirmed real semantics: a function-like macro name *not*
    /// immediately followed by `(` is just an ordinary identifier, not
    /// an invocation at all) and enqueues the fully-expanded,
    /// fully-rescanned result into <see cref="_pending"/>. Returns
    /// false (consuming nothing beyond the name itself) when it isn't a
    /// real invocation after all.
    /// </summary>
    private bool TryStartExpansion(BcsToken nameToken, bool includeNewlines)
    {
        if (!_macros.TryGetValue(nameToken.Value, out var macro)) return false;
        if (_expanding.Contains(macro.Name)) return false;

        var args = new List<List<BcsToken>>();
        if (macro.IsFunctionLike)
        {
            var next = PullOneRaw(includeNewlines, out _);
            if (next.Type != BcsTokenType.OpenParen)
            {
                PushBack(next);
                return false;
            }

            args = ReadArguments();
            if (!macro.IsVariadic && args.Count != macro.Parameters.Count)
            {
                _diagnostics.Add(new BcsDiagnostic(
                    $"macro '{macro.Name}' expects {macro.Parameters.Count} argument(s), got {args.Count}",
                    nameToken.Line, nameToken.Column, BcsDiagnosticSeverity.Warning));
            }
        }

        _expanding.Add(macro.Name);
        var expanded = ExpandTokenList(Expand(macro, args));
        _expanding.Remove(macro.Name);

        foreach (var token in expanded) _pending.Enqueue(token);
        return true;
    }

    /// <summary>Reads a balanced argument list live from the token stream - the opening `(` is already consumed by the caller. Comma-separated, respecting nested parens (an argument can itself contain a call).</summary>
    private List<List<BcsToken>> ReadArguments()
    {
        var args = new List<List<BcsToken>>();
        var current = new List<BcsToken>();
        var depth = 1;

        while (depth > 0)
        {
            var token = PullOneRaw(includeNewlines: false, out _);
            if (token.Type == BcsTokenType.EndOfInput)
            {
                _diagnostics.Add(new BcsDiagnostic("unterminated macro invocation", token.Line, token.Column));
                break;
            }

            if (token.Type == BcsTokenType.OpenParen) { depth++; current.Add(token); continue; }
            if (token.Type == BcsTokenType.CloseParen)
            {
                depth--;
                if (depth == 0) break;
                current.Add(token);
                continue;
            }
            if (token.Type == BcsTokenType.Comma && depth == 1)
            {
                args.Add(current);
                current = new List<BcsToken>();
                continue;
            }

            current.Add(token);
        }

        if (current.Count > 0 || args.Count > 0) args.Add(current);
        return args;
    }

    /// <summary>
    /// Two passes over the macro's own body - confirmed real structure
    /// (`stream.c`'s own `expand_macro`/`expand_id`, two separate walks
    /// rather than one, for the same reason: `##`'s own operands must
    /// already be fully substituted - though NOT yet macro-rescanned,
    /// see <see cref="Paste"/> - before they can be pasted, so
    /// substitution has to finish completely first).
    ///
    /// Pass 1 (substitution): each parameter occurrence is replaced -
    /// with its argument pre-expanded through <see cref="ExpandTokenList"/>
    /// in the common case (confirmed real semantics), but with the
    /// RAW, unexpanded argument instead when the parameter sits
    /// immediately next to a `##` on either side (confirmed real:
    /// `stream.c`'s own `expand_id` checks exactly this adjacency before
    /// deciding whether to pre-expand at all) - an empty such argument
    /// contributes a single <see cref="BcsTokenType.Placemarker"/>
    /// sentinel rather than nothing, so pass 2 can tell "genuinely
    /// nothing here" apart from "the next unrelated body token just
    /// happens to follow." A `#`-stringize of a parameter is resolved
    /// here too (always against the raw argument - confirmed real,
    /// `stringize()` never pre-expands). Every other body token,
    /// including a bare `##` itself, passes through unchanged so pass 2
    /// can find it.
    ///
    /// Pass 2 (concatenation): splices every `(left, '##', right)` into
    /// one token via <see cref="Paste"/>, or - when a placemarker
    /// sentinel stands on one side - passes the other side through
    /// alone (or drops both, if both sides are empty). Deliberately not
    /// entangled with the live pull loop - directly unit-testable
    /// against hand-built token lists.
    /// </summary>
    private List<BcsToken> Expand(BcsMacroDefinition macro, List<List<BcsToken>> args)
    {
        var expandedArgs = new List<List<BcsToken>>(args.Count);
        foreach (var arg in args) expandedArgs.Add(ExpandTokenList(arg));

        var substituted = new List<BcsToken>();
        for (var i = 0; i < macro.Body.Count; i++)
        {
            var token = macro.Body[i];

            if (token.Type == BcsTokenType.Hash && macro.IsFunctionLike &&
                i + 1 < macro.Body.Count && macro.Body[i + 1].Type == BcsTokenType.Identifier)
            {
                var stringizeIndex = macro.Parameters.FindIndex(p => string.Equals(p, macro.Body[i + 1].Value, StringComparison.OrdinalIgnoreCase));
                if (stringizeIndex >= 0 && stringizeIndex < args.Count)
                {
                    substituted.Add(Stringize(token, args[stringizeIndex]));
                    i++; // the parameter name itself is already consumed into the stringized result
                    continue;
                }
            }

            if (token.Type == BcsTokenType.Identifier)
            {
                var paramIndex = macro.Parameters.FindIndex(p => string.Equals(p, token.Value, StringComparison.OrdinalIgnoreCase));
                if (paramIndex >= 0)
                {
                    var adjacentToConcat =
                        (i > 0 && macro.Body[i - 1].Type == BcsTokenType.HashHash) ||
                        (i + 1 < macro.Body.Count && macro.Body[i + 1].Type == BcsTokenType.HashHash);

                    if (adjacentToConcat)
                    {
                        var raw = paramIndex < args.Count ? args[paramIndex] : new List<BcsToken>();
                        if (raw.Count == 0)
                        {
                            substituted.Add(new BcsToken { Type = BcsTokenType.Placemarker, Line = token.Line, Column = token.Column });
                        }
                        else
                        {
                            substituted.AddRange(raw);
                        }
                    }
                    else if (paramIndex < expandedArgs.Count)
                    {
                        substituted.AddRange(expandedArgs[paramIndex]);
                    }

                    continue;
                }
            }

            substituted.Add(token);
        }

        // Mutates in place rather than walking forward pairwise - confirmed
        // real structure (`expand_macro`'s own final loop: `concat()`
        // rewrites `lside` into the paste result but does NOT advance past
        // it, so the very next check re-examines whether *that* result is
        // itself followed by another '##'). This is what correctly
        // resolves a CHAIN (`a ## b ## c`): the shared middle operand
        // first merges with its left neighbor, and the merged result is
        // then immediately re-checked against the next '##' instead of
        // being skipped over as if it were two independent pastes.
        var working = new List<BcsToken>(substituted);
        var idx = 0;
        while (idx < working.Count)
        {
            if (idx + 1 >= working.Count || working[idx + 1].Type != BcsTokenType.HashHash) { idx++; continue; }

            var hashHash = working[idx + 1];
            var left = working[idx];
            var hasRight = idx + 2 < working.Count;
            var right = hasRight ? working[idx + 2] : new BcsToken { Type = BcsTokenType.Placemarker, Line = hashHash.Line, Column = hashHash.Column };

            BcsToken? merged;
            if (left.Type == BcsTokenType.Placemarker && right.Type == BcsTokenType.Placemarker) merged = null;
            else if (left.Type == BcsTokenType.Placemarker) merged = right;
            else if (right.Type == BcsTokenType.Placemarker) merged = left;
            else merged = Paste(left, right, hashHash); // null on an invalid combination (diagnostic already reported) - nothing survives, same as the both-empty case

            working.RemoveRange(idx + 1, hasRight ? 2 : 1);
            working[idx] = merged ?? new BcsToken { Type = BcsTokenType.Placemarker, Line = hashHash.Line, Column = hashHash.Column };
            // idx deliberately NOT advanced - see this method's own remarks
        }

        return working.Where(t => t.Type != BcsTokenType.Placemarker).ToList(); // a defensive final sweep, same as the real compiler's own - every placemarker should already have been consumed above
    }

    private static string TokenText(BcsToken token) => token.RawValue.Length > 0 ? token.RawValue : token.Value;

    /// <summary>
    /// `#` operator (confirmed real semantics, `stream.c`'s own
    /// `stringize`): turns one parameter's RAW, unexpanded argument
    /// tokens into a single new string literal - confirmed real that
    /// the argument is never macro-expanded first here, unlike normal
    /// substitution. Unlike the real compiler, this tokenizer never
    /// keeps whitespace as its own token at all (see
    /// <see cref="BcsToken.Length"/>'s own remarks) - a single space is
    /// reinserted between two adjacent argument tokens only when their
    /// real source columns actually had a gap, which approximates but
    /// can't perfectly reproduce the real compiler's own literal-
    /// whitespace preservation; a narrow, documented divergence; not a
    /// bug.
    /// </summary>
    private static BcsToken Stringize(BcsToken hashToken, List<BcsToken> rawArgumentTokens)
    {
        var text = new StringBuilder();
        for (var i = 0; i < rawArgumentTokens.Count; i++)
        {
            var current = rawArgumentTokens[i];
            if (i > 0)
            {
                var previous = rawArgumentTokens[i - 1];
                if (current.Line == previous.Line && current.Column > previous.Column + previous.Length) text.Append(' ');
            }

            text.Append(TokenText(current));
        }

        return new BcsToken { Type = BcsTokenType.LitString, Value = text.ToString(), Line = hashToken.Line, Column = hashToken.Column, Length = hashToken.Length };
    }

    /// <summary>
    /// `##` operator (confirmed real semantics, `stream.c`'s own
    /// `concat`/`concat_tangible`): joins two adjacent tokens' own
    /// source text into one new token. Rather than hand-porting the
    /// real compiler's own ~150-line hand-built `concat_result`
    /// compatibility table (every legal lside/rside type pair), this
    /// re-lexes the combined text through a fresh, throwaway
    /// <see cref="BcsTokenizer"/> - whatever a single token would
    /// really read as, that's the result; combined text that reads as
    /// more than one token (or none, or something invalid) is the same
    /// "produces an invalid token" real diagnostic, just detected
    /// differently. The pasted token is deliberately NOT rescanned for
    /// further macro expansion here - confirmed real (`concat_tangible`
    /// never re-enters macro expansion on its own result either); that
    /// happens for free afterward anyway, since <see cref="Expand"/>'s
    /// own result is always rescanned by whichever caller invoked it
    /// (<see cref="TryStartExpansion"/>/<see cref="ExpandTokenList"/>).
    /// </summary>
    private BcsToken? Paste(BcsToken left, BcsToken right, BcsToken hashHashToken)
    {
        var combinedText = TokenText(left) + TokenText(right);
        var pasteDiagnostics = new List<BcsDiagnostic>();
        var tokenizer = new BcsTokenizer(new BinaryReader(new MemoryStream(Encoding.UTF8.GetBytes(combinedText))), pasteDiagnostics);
        var pasted = tokenizer.NextSignificantToken();
        var trailing = tokenizer.NextSignificantToken();

        if (pasteDiagnostics.Count > 0 || pasted.Type is BcsTokenType.EndOfInput or BcsTokenType.Invalid || trailing.Type != BcsTokenType.EndOfInput)
        {
            _diagnostics.Add(new BcsDiagnostic(
                $"concatenating '{TokenText(left)}' and '{TokenText(right)}' via '##' produces an invalid token",
                hashHashToken.Line, hashHashToken.Column));
            return null;
        }

        pasted.Line = hashHashToken.Line;
        pasted.Column = hashHashToken.Column;
        return pasted;
    }

    /// <summary>
    /// Rescans a flat, already-in-memory token list for further macro
    /// invocations - used both for the top-level expansion result and
    /// for pre-expanding each argument in isolation. Self-contained by
    /// necessity: a function-like macro name at the very end of this
    /// list with no `(` immediately following *within it* is treated
    /// as not invoked, same as the live pull loop's own rule, even on
    /// the rare chance its real invocation's `(...)` would have
    /// continued past this list's own boundary - confirmed narrow,
    /// accepted limitation, not a crash risk (nobody writes code that
    /// splits a nested call's parens across an outer macro argument
    /// boundary).
    /// </summary>
    private List<BcsToken> ExpandTokenList(List<BcsToken> tokens)
    {
        var result = new List<BcsToken>();
        var i = 0;

        while (i < tokens.Count)
        {
            var token = tokens[i];

            if (token.Type == BcsTokenType.Identifier && _macros.TryGetValue(token.Value, out var macro) && !_expanding.Contains(macro.Name))
            {
                if (!macro.IsFunctionLike)
                {
                    _expanding.Add(macro.Name);
                    result.AddRange(ExpandTokenList(Expand(macro, new List<List<BcsToken>>())));
                    _expanding.Remove(macro.Name);
                    i++;
                    continue;
                }

                if (i + 1 < tokens.Count && tokens[i + 1].Type == BcsTokenType.OpenParen)
                {
                    var (args, nextIndex) = ReadArgumentsFromList(tokens, i + 1);
                    _expanding.Add(macro.Name);
                    result.AddRange(ExpandTokenList(Expand(macro, args)));
                    _expanding.Remove(macro.Name);
                    i = nextIndex;
                    continue;
                }
            }

            result.Add(token);
            i++;
        }

        return result;
    }

    /// <summary>Same shape as <see cref="ReadArguments"/>, over an in-memory list instead of the live stream. <paramref name="openParenIndex"/> must itself be `(`. Returns the parsed arguments and the index of the token immediately after the matching `)`.</summary>
    private static (List<List<BcsToken>> Args, int NextIndex) ReadArgumentsFromList(List<BcsToken> tokens, int openParenIndex)
    {
        var args = new List<List<BcsToken>>();
        var current = new List<BcsToken>();
        var depth = 1;
        var i = openParenIndex + 1;

        while (i < tokens.Count && depth > 0)
        {
            var token = tokens[i];
            if (token.Type == BcsTokenType.OpenParen) { depth++; current.Add(token); i++; continue; }
            if (token.Type == BcsTokenType.CloseParen)
            {
                depth--;
                i++;
                if (depth == 0) break;
                current.Add(token);
                continue;
            }
            if (token.Type == BcsTokenType.Comma && depth == 1)
            {
                args.Add(current);
                current = new List<BcsToken>();
                i++;
                continue;
            }

            current.Add(token);
            i++;
        }

        if (current.Count > 0 || args.Count > 0) args.Add(current);
        return (args, i);
    }
}
