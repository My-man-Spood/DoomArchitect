namespace DoomArchitect.Core.Configuration;

/// <summary>
/// Resolves <c>include()</c> statements and produces a fully merged
/// <see cref="CfgBlock"/> tree from a root <c>.cfg</c> file - the
/// <c>Configuration.FunctionInclude</c>/<c>Combine</c> half of UDB's real
/// <c>Configuration</c> class (confirmed by reading
/// <c>Source/Core/IO/Configuration.cs</c> directly, not assumed), kept
/// separate from <see cref="CfgParser"/> since resolving includes needs
/// file I/O and a cache the pure parser doesn't have.
///
/// The merge semantics matter and are easy to get backwards: when
/// <c>include("Foo.cfg")</c> is processed, the *included* file's own
/// values win over whatever this scope already defined at that point for a
/// plain leaf value (nested blocks recursively merge instead of one
/// replacing the other) - verified against UDB's real
/// <c>Combine(cs, inc, ...)</c> call, where <c>cs</c> (this scope so far)
/// is the first argument and <c>inc</c> (the included content) is the
/// second, and <c>Combine</c>'s own logic always lets its second argument
/// win a leaf conflict. Anything written *after* the <c>include()</c> in
/// the same scope naturally overrides it again, since statements are
/// executed strictly in source order against one running scope - this is
/// why the raw parse tree preserves that order instead of bucketing
/// assignments/blocks/includes into three separate lists up front.
///
/// Self-inclusion is rejected using UDB's own real (narrow) check: the
/// include's literal path argument, as written, compared against the
/// including file's bare filename - not a general circular-include
/// detector (UDB's own doesn't have one either). A general recursion guard
/// is still added on top purely to turn a genuine multi-file cycle into a
/// clear exception instead of a stack overflow - a robustness addition,
/// not a behavior UDB is known to have.
/// </summary>
public sealed class CfgLoader
{
    private readonly ICfgFileSource _source;
    private readonly Dictionary<string, Scope> _cache = new();

    public CfgLoader(ICfgFileSource source)
    {
        _source = source;
    }

    public CfgBlock Load(string path)
    {
        var scope = LoadScope(path, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        return ToPublic(scope, key: string.Empty);
    }

    private Scope LoadScope(string path, HashSet<string> inclusionChain)
    {
        if (_cache.TryGetValue(path, out var cached)) return cached;

        if (!inclusionChain.Add(path))
        {
            throw new InvalidOperationException($"Circular include chain detected while loading '{path}'.");
        }

        try
        {
            var text = _source.ReadText(path);
            var statements = CfgParser.Parse(text);
            var scope = Execute(statements, path, inclusionChain);
            _cache[path] = scope;
            return scope;
        }
        finally
        {
            inclusionChain.Remove(path);
        }
    }

    private Scope Execute(IReadOnlyList<CfgStatement> statements, string currentPath, HashSet<string> inclusionChain)
    {
        var scope = new Scope();

        foreach (var statement in statements)
        {
            switch (statement)
            {
                case CfgAssignStatement assign:
                    // A bare/null-valued assignment (`key;` or `key = null;`)
                    // is stored as a null entry and simply skipped when the
                    // scope is converted to its public form - no consumer
                    // in this codebase needs to distinguish "absent" from
                    // "present but null".
                    scope.Entries[assign.Key] = assign.Value.HasValue ? assign.Value.Value : null;
                    break;

                case CfgBlockStatement block:
                    var child = Execute(block.Body, currentPath, inclusionChain);
                    scope.Entries[block.Key] = scope.Entries.TryGetValue(block.Key, out var existingChild) && existingChild is Scope existingChildScope
                        ? Combine(existingChildScope, child)
                        : child;
                    break;

                case CfgIncludeStatement include:
                    CheckSelfInclude(currentPath, include.Path);
                    var includedPath = _source.ResolveRelative(currentPath, include.Path);
                    var includedScope = LoadScope(includedPath, inclusionChain);
                    var scopeToMerge = include.SubPath != null
                        ? NavigateSubPath(includedScope, include.SubPath, includedPath)
                        : includedScope;
                    scope = Combine(scope, scopeToMerge);
                    break;
            }
        }

        return scope;
    }

    private static void CheckSelfInclude(string currentPath, string includeArgument)
    {
        var currentFileName = Path.GetFileName(currentPath);
        if (string.Equals(includeArgument, currentFileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"'{currentPath}' cannot include itself.");
        }
    }

    private static Scope NavigateSubPath(Scope scope, string subPath, string sourcePathForError)
    {
        var current = scope;
        foreach (var segment in subPath.Split('.'))
        {
            if (!current.Entries.TryGetValue(segment, out var next) || next is not Scope nextScope)
            {
                throw new InvalidOperationException($"Include sub-path '{subPath}' not found in '{sourcePathForError}'.");
            }

            current = nextScope;
        }

        return current;
    }

    /// <summary>
    /// <c>d2</c> wins a leaf-value conflict; a nested scope present in both
    /// recursively merges instead of one replacing the other. Exactly
    /// UDB's real <c>Configuration.Combine</c> semantics.
    /// </summary>
    private static Scope Combine(Scope d1, Scope d2)
    {
        var result = new Scope();
        foreach (var (key, value) in d1.Entries) result.Entries[key] = value;

        foreach (var (key, value) in d2.Entries)
        {
            if (value is Scope d2Child)
            {
                result.Entries[key] = result.Entries.TryGetValue(key, out var existing) && existing is Scope existingChild
                    ? Combine(existingChild, d2Child)
                    : d2Child;
            }
            else
            {
                result.Entries[key] = value;
            }
        }

        return result;
    }

    private static CfgBlock ToPublic(Scope scope, string key)
    {
        var assignments = new List<CfgAssignment>();
        var blocks = new List<CfgBlock>();

        foreach (var (entryKey, value) in scope.Entries)
        {
            switch (value)
            {
                case Scope childScope:
                    blocks.Add(ToPublic(childScope, entryKey));
                    break;
                case CfgValue cfgValue:
                    assignments.Add(new CfgAssignment(entryKey, cfgValue));
                    break;
                // null (a bare/null-valued assignment): intentionally dropped, see Execute's remarks.
            }
        }

        return new CfgBlock(key, assignments, blocks);
    }

    /// <summary>A scope in progress - each entry is either a boxed <see cref="CfgValue"/>, a nested <see cref="Scope"/>, or null.</summary>
    private sealed class Scope
    {
        public Dictionary<string, object?> Entries { get; } = new();
    }
}
