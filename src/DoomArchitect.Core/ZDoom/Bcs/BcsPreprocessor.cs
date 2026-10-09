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
/// `#endif` conditional compilation) plus Phase 3 (`#` stringizing and
/// `##` token-pasting - see <see cref="Stringize"/>/<see cref="Paste"/>)
/// plus Phase 4 (a real `#if`/`#elif` constant-expression evaluator -
/// see <see cref="EvaluateDirectiveCondition"/>) plus Phase 5 (true
/// `#include`/`#import` splicing - see <see cref="ReadIncludeOrImport"/>
/// and <see cref="BcsIncludeResolver"/>) of a staged port.
///
/// `#define`/`#libdefine`/`#undef`/`#if`/`#ifdef`/`#ifndef`/`#elif`/
/// `#else`/`#endif` are always intercepted here, fully - they never
/// reach <see cref="BcsParser.ParseHashDirective"/> at all. `#include`/
/// `#import` are intercepted too, but only when constructed with a
/// non-null <see cref="BcsIncludeResolver"/> (the `BcsParser.ParseProgram`
/// path) - without one (every plain `BcsParser.Parse(string)` call,
/// including every pre-Phase-5 test), they pass straight through
/// unchanged, exactly as <see cref="BcsParser"/> already handles every
/// *other* directive (`#library`, ...).
/// </summary>
/// <summary>
/// Splicing configuration for <see cref="BcsPreprocessor"/> - how to turn
/// a resolved `#include`/`#import` path into real text. <c>null</c>
/// (every plain <see cref="BcsParser.Parse(string)"/> call) means
/// splicing is disabled entirely; only <see cref="BcsParser.ParseProgram"/>
/// ever constructs a real one. Mirrors `DecorateParser`/`ZScriptParser`'s
/// own caller-injected-resolver pattern (<c>OnInclude</c>) rather than
/// baking in any particular I/O API.
/// </summary>
public sealed class BcsIncludeResolver
{
    public string? MainSourcePath { get; }
    public Func<string, string?> ReadFile { get; }

    public BcsIncludeResolver(string? mainSourcePath, Func<string, string?> readFile)
    {
        MainSourcePath = mainSourcePath;
        ReadFile = readFile;
    }
}

internal sealed class BcsPreprocessor
{
    /// <summary>
    /// One currently-open file in the splice - confirmed real structure
    /// (the real compiler's own `source.c` is a genuinely separate
    /// multi-file input stack, not a single-stream assumption).
    /// <see cref="ConditionalBaseline"/> is `_conditionalBranchTaken.Count`
    /// at the moment this frame was pushed - see <see cref="ReadFromCurrentSource"/>'s
    /// own remarks on why that's needed to correctly scope an unclosed
    /// `#if`-family block to *this* file when it closes.
    /// <see cref="ResolvedPath"/> is this frame's real, never-blanked
    /// resolved path (unlike <see cref="SourcePath"/>, which is blanked
    /// to <c>""</c> for the bottom/main frame) - used only for the real
    /// cycle check in <see cref="ReadIncludeOrImport"/>, which needs to
    /// detect a file including/importing *itself* even though its own
    /// reported tag is blank.
    /// </summary>
    private sealed record SourceFrame(BcsTokenizer Tokenizer, string SourcePath, string? Directory, int ConditionalBaseline, string ResolvedPath);

    private readonly List<BcsDiagnostic> _diagnostics;
    private readonly BcsIncludeResolver? _includeResolver;

    // The whole splice, bottom-to-top - the bottom frame (pushed at
    // construction, never popped) is the original file, always reporting
    // SourcePath "" regardless of what real path it has (matching the
    // existing "main file's own symbol has empty SourcePath" convention) -
    // that real path, when given, only ever feeds Directory, for resolving
    // its own first level of relative includes.
    private readonly Stack<SourceFrame> _sourceStack = new();

    // Resolved paths CURRENTLY on _sourceStack (an ancestor chain, not
    // "ever seen") - a real cycle guard, confirmed from source.c's own
    // p_load_included_source ("file already being loaded" is checked
    // against files still actively open, never against ones already
    // finished and popped). The main file's own resolved path, when
    // given, is seeded here once and never removed (it's never popped
    // until the whole parse ends, so it's always "active").
    //
    // Deliberately NOT a general "already included, skip" dedup anymore -
    // confirmed real (same source.c): #include has NO such dedup at all;
    // the identical file genuinely gets re-spliced every time it's
    // #included, which is exactly why a real shared file needs its own
    // manual #ifndef/#define include guard to be included safely from
    // more than one sibling.
    private readonly HashSet<string> _activeResolvedPaths = new(StringComparer.OrdinalIgnoreCase);

    // #import's own, genuinely different real semantics (confirmed from
    // library.c's own load_imported_lib: "Return the library if it is
    // already loaded") - once a library has been imported anywhere, a
    // later #import of that same resolved path, from any file, is a
    // silent no-op (reuse, not a re-splice) - permanently, never reset,
    // unlike #include's own lack of any such dedup.
    private readonly HashSet<string> _importedPaths = new(StringComparer.OrdinalIgnoreCase);

    // Every resolved path actually spliced in, in encounter order -
    // BcsProgram's own IncludedPaths reporting.
    private readonly List<string> _includedPaths = new();

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

    // Scratch cursor used only while evaluating one #if/#elif condition
    // expression (see EvaluateDirectiveCondition and the EvalXxx methods
    // below it) - single-field threading, the same shape BcsParser's own
    // _current/Advance() already uses, safe since only one condition is
    // ever being evaluated at a time (no reentrancy).
    private BcsToken _condToken = null!;

    public BcsPreprocessor(BcsTokenizer tokenizer, List<BcsDiagnostic> diagnostics, BcsIncludeResolver? includeResolver = null)
    {
        _diagnostics = diagnostics;
        _includeResolver = includeResolver;

        var mainPath = includeResolver?.MainSourcePath;
        var mainResolvedPath = mainPath != null ? Path.GetFullPath(mainPath) : "";
        _sourceStack.Push(new SourceFrame(tokenizer, SourcePath: "", Directory: mainPath != null ? Path.GetDirectoryName(mainPath) : null, ConditionalBaseline: 0, ResolvedPath: mainResolvedPath));
        if (mainPath != null) _activeResolvedPaths.Add(mainResolvedPath); // never removed - the bottom frame is never popped until the whole parse ends
    }

    /// <summary>Every `#define`/`#libdefine` encountered, in source order, even one later shadowed by a redefinition - <see cref="BcsParser.Parse"/> builds one <see cref="BcsDefineDirective"/> per entry after a full parse, preserving the existing completion/hover/go-to-def-for-macro-names feature unchanged.</summary>
    public IReadOnlyList<BcsMacroDefinition> Macros => _macroOrder;

    /// <summary>Every resolved path actually spliced in via `#include`/`#import`, in encounter order - <see cref="BcsProgram"/>'s own reporting.</summary>
    public IReadOnlyList<string> IncludedPaths => _includedPaths;

    private BcsTokenizer CurrentTokenizer => _sourceStack.Peek().Tokenizer;
    private string CurrentSourcePath => _sourceStack.Peek().SourcePath;
    private string? CurrentDirectory => _sourceStack.Peek().Directory;

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
                        ReadIfdef(directiveName);
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

                    if (_includeResolver != null && directiveName.Value is "include" or "import")
                    {
                        ReadIncludeOrImport(directiveName);
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
                AddDiagnostic("unterminated #if/#ifdef/#ifndef - missing #endif", token.Line, token.Column);
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

        return ReadFromCurrentSource(includeNewlines, out comments);
    }

    private void PushBack(BcsToken token) => _pushback.Push(token);

    /// <summary>
    /// The one place that actually reads from whichever file is on top
    /// of <see cref="_sourceStack"/>, popping exhausted frames (their
    /// own `EndOfInput`) and retrying until a real token is found or the
    /// BOTTOM frame's own genuine final EOF is reached - that bottom
    /// EOF is returned as-is, letting the main dispatch loop's own
    /// existing final-unclosed-`#if` check still fire for it. Stamps
    /// <see cref="BcsToken.SourcePath"/> on every token this way, the
    /// one central point for it. Before popping a frame, closes out any
    /// `#if`/`#ifdef`/`#ifndef` opened while THAT frame was active but
    /// never `#endif`'d (confirmed real: an unclosed conditional inside
    /// one `#include`d file is that file's own problem, not something
    /// that should silently bleed into the including file's own later
    /// reading) - reports one diagnostic per still-open block, attributed
    /// to the closing frame's own path, and restores
    /// <see cref="_conditionalBranchTaken"/> to what it was before that
    /// frame was entered.
    /// </summary>
    private BcsToken ReadFromCurrentSource(bool includeNewlines, out List<BcsToken> comments)
    {
        while (true)
        {
            var token = CurrentTokenizer.NextSignificantToken(includeNewlines, out comments);
            if (token.Type != BcsTokenType.EndOfInput || _sourceStack.Count == 1)
            {
                token.SourcePath = CurrentSourcePath;
                return token;
            }

            var frame = _sourceStack.Pop();
            _activeResolvedPaths.Remove(frame.ResolvedPath);
            while (_conditionalBranchTaken.Count > frame.ConditionalBaseline)
            {
                _diagnostics.Add(new BcsDiagnostic("unterminated #if/#ifdef/#ifndef - missing #endif", token.Line, token.Column, BcsDiagnosticSeverity.Error, frame.SourcePath));
                _conditionalBranchTaken.Pop();
            }
        }
    }

    /// <summary>Same stack-aware popping/stamping as <see cref="ReadFromCurrentSource"/>, for the one spot (<see cref="ReadDefine"/>'s whitespace-sensitivity check) that needs the tokenizer's raw, non-skipping read instead.</summary>
    private BcsToken ReadRawTokenFromCurrentSource()
    {
        while (true)
        {
            var token = CurrentTokenizer.ReadToken();
            if (token.Type != BcsTokenType.EndOfInput || _sourceStack.Count == 1)
            {
                token.SourcePath = CurrentSourcePath;
                return token;
            }

            var frame = _sourceStack.Pop();
            _activeResolvedPaths.Remove(frame.ResolvedPath);
            while (_conditionalBranchTaken.Count > frame.ConditionalBaseline)
            {
                _diagnostics.Add(new BcsDiagnostic("unterminated #if/#ifdef/#ifndef - missing #endif", token.Line, token.Column, BcsDiagnosticSeverity.Error, frame.SourcePath));
                _conditionalBranchTaken.Pop();
            }
        }
    }

    private void AddDiagnostic(string message, int line, int column, BcsDiagnosticSeverity severity = BcsDiagnosticSeverity.Error) =>
        _diagnostics.Add(new BcsDiagnostic(message, line, column, severity, CurrentSourcePath));

    /// <summary>
    /// A rooted path is used as-is; a relative one resolves against
    /// <paramref name="baseDir"/> when there is one (a real file-backed
    /// source). With no base directory at all (a lump/PK3-entry-backed
    /// source, or a brand new unsaved buffer - this project's own
    /// in-memory-editing cases, which the real compiler never has, since
    /// it always runs from a real file on disk) the raw name is passed
    /// through unchanged rather than failing outright - letting whoever
    /// supplied <see cref="BcsIncludeResolver.ReadFile"/> decide what a
    /// bare name like <c>"zcommon.acs"</c> means (a resource-set search,
    /// for both <c>ScriptDocument</c>'s live diagnostics and
    /// <c>ScriptCompilerRunner</c>'s own include-discovery pass).
    /// </summary>
    private static string ResolveIncludePath(string rawPath, string? baseDir)
    {
        if (Path.IsPathRooted(rawPath)) return rawPath;
        return baseDir == null ? rawPath : Path.Combine(baseDir, rawPath);
    }

    /// <summary>
    /// `#include "path"`/`#import "path"` - confirmed real resolution
    /// (`zt-bcc`'s own `src/task.c`, `identify_file_relative`): a
    /// relative path resolves against the INCLUDING file's own
    /// directory (<see cref="CurrentDirectory"/> at the moment this
    /// directive is read, not the original main file's), an absolute
    /// one is used as-is. Only intercepted at all when
    /// <see cref="_includeResolver"/> is non-null - see this class's
    /// own remarks. Splices live: on success, pushes a new
    /// <see cref="SourceFrame"/> and the directive itself vanishes
    /// (no `BcsIncludeDirective`/`BcsImportDirective` node), exactly
    /// like `#define` vanishing today.
    ///
    /// `#include` and `#import` have genuinely different real dedup
    /// semantics here, confirmed from source - NOT the same mechanism
    /// this code used to treat them as:
    /// <list type="bullet">
    /// <item>`#include` (`source.c`'s own `p_load_included_source`): NO
    /// general dedup at all. The same file genuinely gets re-spliced
    /// every time it's `#include`d - a real shared file needs its own
    /// manual `#ifndef`/`#define` include guard to be safely
    /// `#include`d from more than one sibling. Only a true CYCLE (this
    /// exact file still actively open somewhere up the current chain)
    /// is special-cased - a real diagnostic ("file already being
    /// loading", confirmed wording), not a silent skip.</item>
    /// <item>`#import` (`library.c`'s own `load_imported_lib`: "Return
    /// the library if it is already loaded"): genuinely deduped,
    /// permanently - once a given resolved path has been imported
    /// anywhere, importing it again (from the same or a different
    /// file) is a silent no-op, by design, so many independent files
    /// can each `#import` a shared library without caring whether
    /// another one already did.</item>
    /// </list>
    /// </summary>
    private void ReadIncludeOrImport(BcsToken directiveNameToken)
    {
        var isImport = string.Equals(directiveNameToken.Value, "import", StringComparison.OrdinalIgnoreCase);

        var pathToken = PullOneRaw(includeNewlines: false, out _);
        if (pathToken.Type != BcsTokenType.LitString)
        {
            AddDiagnostic($"expected a string literal after '#{directiveNameToken.Value}'", pathToken.Line, pathToken.Column);
            SkipToEndOfLine();
            return;
        }

        var rawPath = pathToken.Value;
        SkipToEndOfLine();

        // Never blame a problem found deep inside an already-spliced-in
        // file on the editing buffer - it's correctly attributable now
        // (SourcePath exists), just not here; this only reports a
        // directive written directly in whichever file is at the BOTTOM
        // of the stack right now.
        var reportDiagnostics = _sourceStack.Count == 1;

        var resolved = ResolveIncludePath(rawPath, CurrentDirectory);
        var normalized = Path.GetFullPath(resolved);

        if (isImport && !_importedPaths.Add(normalized)) return; // already imported anywhere - silent reuse, confirmed real #import semantics

        if (_activeResolvedPaths.Contains(normalized))
        {
            // A real cycle - this exact file is still actively open somewhere up the current chain (confirmed real diagnostics: source.c's own "file already being loaded" for #include, library.c's own "library attempting to import itself" for #import).
            AddDiagnostic(isImport ? "library attempting to import itself" : "file already being loaded", pathToken.Line, pathToken.Column);
            return;
        }

        var text = _includeResolver!.ReadFile(resolved);
        if (text == null)
        {
            if (reportDiagnostics) AddDiagnostic($"included file not found: '{rawPath}'", pathToken.Line, pathToken.Column, BcsDiagnosticSeverity.Warning);
            return;
        }

        _includedPaths.Add(resolved);
        _activeResolvedPaths.Add(normalized);
        var includedTokenizer = new BcsTokenizer(new BinaryReader(new MemoryStream(Encoding.UTF8.GetBytes(text))), _diagnostics, resolved);
        _sourceStack.Push(new SourceFrame(includedTokenizer, resolved, Path.GetDirectoryName(resolved), ConditionalBaseline: _conditionalBranchTaken.Count, ResolvedPath: normalized));
    }

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
            AddDiagnostic("expected a macro name after '#define'", nameToken.Line, nameToken.Column);
            SkipToEndOfLine();
            return;
        }

        var macro = new BcsMacroDefinition
        {
            Name = nameToken.RawValue,
            Line = nameToken.Line,
            Column = nameToken.Column,
            DocComment = docComment,
            SourcePath = nameToken.SourcePath,
        };

        var raw = ReadRawTokenFromCurrentSource();
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

        var token = ReadFromCurrentSource(includeNewlines: false, out _);
        if (token.Type == BcsTokenType.CloseParen) return; // no parameters at all

        while (true)
        {
            if (token.Type == BcsTokenType.Ellipsis)
            {
                macro.IsVariadic = true;
                macro.Parameters.Add("__VA_ARGS__");
                token = ReadFromCurrentSource(includeNewlines: false, out _);
                break;
            }

            if (token.Type != BcsTokenType.Identifier)
            {
                AddDiagnostic("expected a macro parameter name", token.Line, token.Column);
                break;
            }

            macro.Parameters.Add(token.RawValue);
            token = ReadFromCurrentSource(includeNewlines: false, out _);
            if (token.Type != BcsTokenType.Comma) break;
            token = ReadFromCurrentSource(includeNewlines: false, out _);
        }

        if (token.Type == BcsTokenType.CloseParen) return;
        AddDiagnostic("expected ')'", token.Line, token.Column);
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
            AddDiagnostic("'##' operator at beginning of macro body", macro.Body[0].Line, macro.Body[0].Column);
        }

        if (macro.Body[^1].Type == BcsTokenType.HashHash)
        {
            AddDiagnostic("'##' operator at end of macro body", macro.Body[^1].Line, macro.Body[^1].Column);
        }

        for (var i = 0; i < macro.Body.Count; i++)
        {
            if (macro.Body[i].Type != BcsTokenType.Hash || !macro.IsFunctionLike) continue;

            var next = i + 1 < macro.Body.Count ? macro.Body[i + 1] : null;
            var isValidParam = next is { Type: BcsTokenType.Identifier } &&
                macro.Parameters.Any(p => string.Equals(p, next.Value, StringComparison.OrdinalIgnoreCase));

            if (!isValidParam)
            {
                AddDiagnostic(
                    $"'{(next is null ? "?" : TokenText(next))}' is not a parameter of macro '{macro.Name}'",
                    macro.Body[i].Line, macro.Body[i].Column);
            }
        }
    }

    /// <summary><c>#undef NAME</c> - not real `TK_*` grammar this project modeled before Phase 1, but cheap and real (confirmed from `dirc.c`'s own `read_undef`/`remove_macro`): simply removes a macro so later uses are no longer expanded.</summary>
    private void ReadUndef()
    {
        var token = ReadFromCurrentSource(includeNewlines: false, out _);
        if (token.Type == BcsTokenType.Identifier) _macros.Remove(token.Value);
        else AddDiagnostic("expected a macro name after '#undef'", token.Line, token.Column);

        SkipToEndOfLine();
    }

    private void SkipToEndOfLine()
    {
        while (PullOneRaw(includeNewlines: true, out _) is { Type: not (BcsTokenType.Newline or BcsTokenType.EndOfInput) }) { }
    }

    /// <summary>
    /// <c>#ifdef NAME</c>/<c>#ifndef NAME</c> - confirmed real grammar
    /// (`dirc.c`'s own `read_ifdef`): active when the name is (for
    /// `ifdef`) or isn't (for `ifndef`) a currently-defined macro,
    /// confirmed via the exact same lookup <see cref="TryStartExpansion"/>
    /// already uses; deliberately read raw (not through the auto-
    /// expanding main pull loop), same reasoning as `defined`'s own
    /// name in <see cref="EvalDefined"/> - we need to know whether the
    /// NAME ITSELF is a macro, not what it would expand to. A real
    /// `#if`'s condition is now actually evaluated (Phase 4 - see
    /// <see cref="EvaluateDirectiveCondition"/>), confirmed real
    /// grammar from `token/expr.c`'s own `p_eval_prep_expr`.
    /// </summary>
    private void ReadIfdef(BcsToken directiveNameToken)
    {
        var directive = directiveNameToken.Value;
        bool branchActive;

        if (string.Equals(directive, "if", StringComparison.OrdinalIgnoreCase))
        {
            branchActive = EvaluateDirectiveCondition(directiveNameToken) != 0;
            SkipToEndOfLine(); // defensive - anything left on the line past the expression itself (a trailing comment, or a token the evaluator's own recovery didn't consume)
        }
        else
        {
            var nameToken = ReadFromCurrentSource(includeNewlines: false, out _);
            var isDefined = nameToken.Type == BcsTokenType.Identifier && _macros.ContainsKey(nameToken.Value);
            SkipToEndOfLine();

            branchActive = string.Equals(directive, "ifndef", StringComparison.OrdinalIgnoreCase) ? !isDefined : isDefined;

            if (nameToken.Type != BcsTokenType.Identifier)
            {
                AddDiagnostic($"expected a macro name after '#{directive}'", nameToken.Line, nameToken.Column);
            }
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
            AddDiagnostic($"'#{directiveName.Value}' with no open '#if'/'#ifdef'/'#ifndef'", directiveName.Line, directiveName.Column);
            return;
        }

        SkipInactiveRegion(); // this chain already took a branch - skip past the rest of it, however many more elif/else sections follow, down to this level's own #endif
    }

    private void ReadEndif(BcsToken directiveName)
    {
        SkipToEndOfLine();
        if (_conditionalBranchTaken.Count == 0)
        {
            AddDiagnostic("'#endif' with no open '#if'/'#ifdef'/'#ifndef'", directiveName.Line, directiveName.Column);
            return;
        }

        _conditionalBranchTaken.Pop();
    }

    /// <summary>
    /// Skips forward - emitting nothing - until finding, at *this*
    /// level (tracking nested `#if`-family depth so a nested block's
    /// own `#endif` doesn't get mistaken for this level's), an
    /// `#elif` whose own condition is now actually true (Phase 4 - a
    /// false `#elif` is itself skipped too, same as any other sibling,
    /// and the search continues past it), an `#else` (unconditionally
    /// becomes active - confirmed real, `#else` has no condition of its
    /// own at all), or this level's own `#endif` (nothing becomes
    /// active - the whole block simply closes). Confirmed real
    /// structure (`dirc.c`'s own `find_endif`/`read_search_dirc`).
    ///
    /// Phase 5 real interaction, caught while writing this phase's own
    /// tests: under splicing, "EOF" while scanning no longer reliably
    /// means "truly nothing left" - if the block being skipped was
    /// opened inside a now-exhausted `#include`d file,
    /// <see cref="ReadFromCurrentSource"/>'s own per-frame cleanup
    /// already force-closed it (and reported the real "unterminated
    /// #if" diagnostic) and transparently resumed the OUTER file - so
    /// the first "EOF" this method would ever actually observe is really
    /// the outer file's own next real token. Tracking the conditional
    /// stack's own depth at entry and bailing the instant it drops below
    /// that (pushing the just-read token back first, so nothing is lost)
    /// is what stops this method from then blindly scanning through the
    /// including file's own code hunting for a sibling that can no
    /// longer exist.
    /// </summary>
    private void SkipInactiveRegion()
    {
        var expectedDepth = _conditionalBranchTaken.Count;
        var depth = 0;
        while (true)
        {
            var token = PullOneRaw(includeNewlines: true, out _);
            if (_conditionalBranchTaken.Count < expectedDepth) { PushBack(token); return; } // the block we were skipping was already force-closed by a frame-pop cleanup - nothing more to do here
            if (token.Type == BcsTokenType.EndOfInput) return; // the EOF-level unclosed-#if check in NextSignificantToken itself will still fire for the outer block

            if (token.Type != BcsTokenType.Hash) continue;

            var name = PullOneRaw(includeNewlines: false, out _);
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
            if (string.Equals(name.Value, "elif", StringComparison.OrdinalIgnoreCase))
            {
                var conditionValue = EvaluateDirectiveCondition(name);
                SkipToEndOfLine();
                if (conditionValue == 0) continue; // this elif's own condition was false - it's skipped too, same as any other sibling; keep searching

                _conditionalBranchTaken.Pop();
                _conditionalBranchTaken.Push(true); // becomes active - resume normal reading right after this line
                return;
            }

            if (string.Equals(name.Value, "else", StringComparison.OrdinalIgnoreCase))
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

    // --- Phase 4: #if/#elif constant-expression evaluation ---
    //
    // Confirmed real grammar from `token/expr.c`'s own `p_eval_prep_expr` -
    // a small, SEPARATE evaluator from the real statement-expression
    // grammar (BcsParser.Expressions.cs): no assignment, no postfix
    // (`[]`/`.`/calls/`++`/`--`), no format-cast tags, and it actually
    // computes an int value rather than just validating shape. Every
    // token is read through the normal auto-expanding main pull loop
    // (confirmed real: `p_read_expanpreptk`), EXCEPT the name tested by
    // `defined`/`defined(...)` (see EvalDefined) - confirmed real
    // (`eval_defined`'s own non-expanding preptk reads): `defined` needs
    // to know whether the name ITSELF is a macro, not what it expands to.

    /// <summary>
    /// Reads and evaluates one #if/#elif condition expression. The last
    /// thing read is always one token PAST the expression itself (needed
    /// to decide whether a binary operator follows) - pushed back before
    /// returning, every time, since otherwise whenever that lookahead
    /// token happens to BE the line's own terminating newline, it would
    /// vanish before the caller's own <see cref="SkipToEndOfLine"/> ever
    /// saw it, and that call would then incorrectly swallow the entire
    /// NEXT line looking for a newline that already went by - a real bug
    /// caught live, not a hypothetical one.
    /// </summary>
    private int EvaluateDirectiveCondition(BcsToken directiveNameToken)
    {
        CondAdvance();
        if (_condToken.Type is BcsTokenType.Newline or BcsTokenType.EndOfInput)
        {
            AddDiagnostic("missing expression", directiveNameToken.Line, directiveNameToken.Column);
            PushBack(_condToken);
            return 0;
        }

        var value = EvalTernary();
        PushBack(_condToken);
        return value;
    }

    private void CondAdvance() => _condToken = NextSignificantToken(includeNewlines: true);

    /// <summary>`?:` - confirmed real "Elvis" form (`a ?: b`, the middle operand optional), same as the real statement-expression grammar's own ternary.</summary>
    private int EvalTernary()
    {
        var value = EvalLogicalOr();
        if (_condToken.Type != BcsTokenType.Questionmark) return value;

        CondAdvance();
        var middle = value;
        if (_condToken.Type != BcsTokenType.Colon) middle = EvalTernary(); // confirmed real: the middle operand recurses back to full precedence (eval_binary), not a restricted level

        if (_condToken.Type != BcsTokenType.Colon)
        {
            AddDiagnostic("expected ':'", _condToken.Line, _condToken.Column);
            return value;
        }

        CondAdvance();
        var right = EvalTernary();
        return value != 0 ? middle : right;
    }

    private int EvalLogicalOr()
    {
        var left = EvalLogicalAnd();
        while (_condToken.Type == BcsTokenType.OpLogicalOr)
        {
            CondAdvance();
            var right = EvalLogicalAnd();
            left = left != 0 || right != 0 ? 1 : 0;
        }

        return left;
    }

    private int EvalLogicalAnd()
    {
        var left = EvalBitOr();
        while (_condToken.Type == BcsTokenType.OpLogicalAnd)
        {
            CondAdvance();
            var right = EvalBitOr();
            left = left != 0 && right != 0 ? 1 : 0;
        }

        return left;
    }

    private int EvalBitOr()
    {
        var left = EvalBitXor();
        while (_condToken.Type == BcsTokenType.OpBitOr) { CondAdvance(); left |= EvalBitXor(); }
        return left;
    }

    private int EvalBitXor()
    {
        var left = EvalBitAnd();
        while (_condToken.Type == BcsTokenType.OpBitXor) { CondAdvance(); left ^= EvalBitAnd(); }
        return left;
    }

    private int EvalBitAnd()
    {
        var left = EvalEquality();
        while (_condToken.Type == BcsTokenType.OpBitAnd) { CondAdvance(); left &= EvalEquality(); }
        return left;
    }

    private int EvalEquality()
    {
        var left = EvalRelational();
        while (true)
        {
            if (_condToken.Type == BcsTokenType.OpEquals) { CondAdvance(); left = left == EvalRelational() ? 1 : 0; }
            else if (_condToken.Type == BcsTokenType.OpNotEquals) { CondAdvance(); left = left != EvalRelational() ? 1 : 0; }
            else return left;
        }
    }

    private int EvalRelational()
    {
        var left = EvalShift();
        while (true)
        {
            switch (_condToken.Type)
            {
                case BcsTokenType.OpLessThan: CondAdvance(); left = left < EvalShift() ? 1 : 0; break;
                case BcsTokenType.OpLessOrEqual: CondAdvance(); left = left <= EvalShift() ? 1 : 0; break;
                case BcsTokenType.OpGreaterThan: CondAdvance(); left = left > EvalShift() ? 1 : 0; break;
                case BcsTokenType.OpGreaterOrEqual: CondAdvance(); left = left >= EvalShift() ? 1 : 0; break;
                default: return left;
            }
        }
    }

    private int EvalShift()
    {
        var left = EvalAdditive();
        while (true)
        {
            if (_condToken.Type == BcsTokenType.OpLeftShift) { CondAdvance(); left <<= EvalAdditive(); }
            else if (_condToken.Type == BcsTokenType.OpRightShift) { CondAdvance(); left >>= EvalAdditive(); }
            else return left;
        }
    }

    private int EvalAdditive()
    {
        var left = EvalMultiplicative();
        while (true)
        {
            if (_condToken.Type == BcsTokenType.OpAdd) { CondAdvance(); left += EvalMultiplicative(); }
            else if (_condToken.Type == BcsTokenType.OpSubtract) { CondAdvance(); left -= EvalMultiplicative(); }
            else return left;
        }
    }

    private int EvalMultiplicative()
    {
        var left = EvalPrefix();
        while (true)
        {
            if (_condToken.Type == BcsTokenType.OpMultiply) { CondAdvance(); left *= EvalPrefix(); }
            else if (_condToken.Type is BcsTokenType.OpDivide or BcsTokenType.OpMod)
            {
                var isDivide = _condToken.Type == BcsTokenType.OpDivide;
                var opToken = _condToken;
                CondAdvance();
                var right = EvalPrefix();
                if (right == 0)
                {
                    // Confirmed real diagnostic ("division by zero") - the real compiler aborts compilation entirely on this; we recover instead by resolving the whole condition to false and letting the caller's own SkipToEndOfLine clean up whatever's left on the line.
                    AddDiagnostic("division by zero", opToken.Line, opToken.Column);
                    return 0;
                }

                left = isDivide ? left / right : left % right;
            }
            else return left;
        }
    }

    private int EvalPrefix()
    {
        switch (_condToken.Type)
        {
            case BcsTokenType.OpAdd: CondAdvance(); return EvalPrefix();
            case BcsTokenType.OpSubtract: CondAdvance(); return -EvalPrefix();
            case BcsTokenType.OpLogicalNot: CondAdvance(); return EvalPrefix() == 0 ? 1 : 0;
            case BcsTokenType.OpBitNot: CondAdvance(); return ~EvalPrefix();
            default: return EvalPrimary();
        }
    }

    /// <summary>
    /// Confirmed real primary set (`eval_primary`'s own switch): a char
    /// literal, `defined`, a decimal/octal/hex literal, or a
    /// parenthesized sub-expression - and nothing else. Notably NOT
    /// included (confirmed - they fall to the real compiler's own
    /// `default: longjmp`, the same "invalid expression" error as
    /// anything else unrecognized, not silently treated as 0): a
    /// fixed-point/binary/radix literal, or a string literal - all real
    /// token kinds this project's own tokenizer produces, just never
    /// legal here in the real grammar either.
    /// </summary>
    private int EvalPrimary()
    {
        switch (_condToken.Type)
        {
            case BcsTokenType.LitChar:
            {
                var value = _condToken.IntValue;
                CondAdvance();
                return value;
            }

            case BcsTokenType.Identifier when string.Equals(_condToken.Value, "defined", StringComparison.OrdinalIgnoreCase):
                return EvalDefined();

            case BcsTokenType.Identifier:
                // Confirmed real (eval_id): any other identifier reaching here means the auto-expanding read already tried and failed to expand it (not a macro) - it evaluates to plain 0, same as the standard C-preprocessor convention.
                CondAdvance();
                return 0;

            case BcsTokenType.LitDecimal:
            case BcsTokenType.LitOctal:
            case BcsTokenType.LitHex:
            {
                var value = _condToken.IntValue;
                CondAdvance();
                return value;
            }

            case BcsTokenType.OpenParen:
            {
                CondAdvance();
                var value = EvalTernary();
                if (_condToken.Type != BcsTokenType.CloseParen)
                {
                    AddDiagnostic("expected ')'", _condToken.Line, _condToken.Column);
                    return value;
                }

                CondAdvance();
                return value;
            }

            default:
                AddDiagnostic("invalid expression", _condToken.Line, _condToken.Column);
                CondAdvance(); // consume the offending token so evaluation keeps making forward progress
                return 0;
        }
    }

    /// <summary>
    /// `defined NAME` / `defined(NAME)` - confirmed real (`eval_defined`):
    /// the name is read RAW, through <see cref="PullOneRaw"/>, never the
    /// auto-expanding main pull loop - `defined` needs to know whether
    /// the name ITSELF is currently a macro, not what it would expand
    /// to (which would be nonsensical - an undefined name doesn't
    /// expand to anything meaningful to test).
    /// </summary>
    private int EvalDefined()
    {
        var nameToken = PullOneRaw(includeNewlines: true, out _);
        var paren = nameToken.Type == BcsTokenType.OpenParen;
        if (paren) nameToken = PullOneRaw(includeNewlines: true, out _);

        bool isDefined;
        if (nameToken.Type == BcsTokenType.Identifier)
        {
            isDefined = _macros.ContainsKey(nameToken.Value);
        }
        else
        {
            AddDiagnostic("expected a macro name after 'defined'", nameToken.Line, nameToken.Column);
            isDefined = false;
        }

        if (paren)
        {
            var closeToken = PullOneRaw(includeNewlines: true, out _);
            if (closeToken.Type != BcsTokenType.CloseParen)
            {
                AddDiagnostic("expected ')'", closeToken.Line, closeToken.Column);
                PushBack(closeToken); // not actually a ')' - let the normal pull loop see it, same recovery posture used everywhere else here
            }
        }

        CondAdvance(); // resume normal auto-expanding reads for whatever follows "defined"/"defined(...)"
        return isDefined ? 1 : 0;
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
                AddDiagnostic(
                    $"macro '{macro.Name}' expects {macro.Parameters.Count} argument(s), got {args.Count}",
                    nameToken.Line, nameToken.Column, BcsDiagnosticSeverity.Warning);
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
                AddDiagnostic("unterminated macro invocation", token.Line, token.Column);
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

        return new BcsToken { Type = BcsTokenType.LitString, Value = text.ToString(), Line = hashToken.Line, Column = hashToken.Column, Length = hashToken.Length, SourcePath = hashToken.SourcePath };
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
            AddDiagnostic(
                $"concatenating '{TokenText(left)}' and '{TokenText(right)}' via '##' produces an invalid token",
                hashHashToken.Line, hashHashToken.Column);
            return null;
        }

        pasted.Line = hashHashToken.Line;
        pasted.Column = hashHashToken.Column;
        pasted.SourcePath = hashHashToken.SourcePath;
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
