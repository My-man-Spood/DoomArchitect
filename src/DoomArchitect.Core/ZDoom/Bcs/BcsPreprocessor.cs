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
/// This is Phase 1 of a staged port - real object-like and
/// function-like `#define`/`#undef` with parameter substitution and
/// recursive rescanning, within a single file. Deliberately deferred
/// to later phases, not silently dropped: `##` token-pasting and `#`
/// stringizing; `#if`/`#elif` (needs its own constant-expression
/// evaluator); `#ifdef`/`#ifndef`/`#else`/`#endif` conditional
/// compilation; cross-file macro visibility (a `#define` in an
/// `#include`d file is not yet visible to the including file - true
/// textual-splice `#include` semantics are a separate, later
/// reconciliation with the existing post-hoc symbol-merging
/// <see cref="BcsProgram"/> already does for completion/hover/go-to-def).
///
/// Only `#define`/`#libdefine`/`#undef` are intercepted here, fully -
/// they never reach <see cref="BcsParser.ParseHashDirective"/> at all
/// anymore. Every other directive (`#include`, `#import`, `#library`,
/// ...) passes straight through unchanged, exactly as
/// <see cref="BcsParser"/> already handles it.
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

                if (directiveName.Type == BcsTokenType.Identifier)
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

    /// <summary>Raw, unexpanded tokens up to (not including) the terminating newline - confirmed real grammar (`dirc.c`'s own `read_body`) stops there. Whitespace/comments inside the body aren't preserved - they only ever mattered for the function-like-vs-object-like check already handled in <see cref="ReadDefine"/>, and for `##`/stringizing, both deferred.</summary>
    private void ReadMacroBody(BcsMacroDefinition macro)
    {
        while (true)
        {
            var token = _tokenizer.NextSignificantToken(includeNewlines: true);
            if (token.Type is BcsTokenType.Newline or BcsTokenType.EndOfInput) break;
            macro.Body.Add(token);
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
    /// Pure substitution, one pass over the macro's own body - each
    /// argument is first recursively expanded through
    /// <see cref="ExpandTokenList"/> (confirmed real semantics for the
    /// common case; both `#`/`##`, which would need the *unexpanded*
    /// argument instead, are deferred), then substituted wherever its
    /// parameter name appears in the body; everything else passes
    /// through unchanged. Deliberately not entangled with the live
    /// pull loop - directly unit-testable against hand-built token
    /// lists.
    /// </summary>
    private List<BcsToken> Expand(BcsMacroDefinition macro, List<List<BcsToken>> args)
    {
        var expandedArgs = new List<List<BcsToken>>(args.Count);
        foreach (var arg in args) expandedArgs.Add(ExpandTokenList(arg));

        var result = new List<BcsToken>();
        foreach (var token in macro.Body)
        {
            if (token.Type == BcsTokenType.Identifier)
            {
                var paramIndex = macro.Parameters.FindIndex(p => string.Equals(p, token.Value, StringComparison.OrdinalIgnoreCase));
                if (paramIndex >= 0)
                {
                    if (paramIndex < expandedArgs.Count) result.AddRange(expandedArgs[paramIndex]);
                    continue;
                }
            }

            result.Add(token);
        }

        return result;
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
