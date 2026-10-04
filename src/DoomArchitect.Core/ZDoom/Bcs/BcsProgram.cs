namespace DoomArchitect.Core.ZDoom.Bcs;

/// <summary>
/// The file currently being parsed (<see cref="MainUnit"/>) plus every
/// file it transitively <c>#include</c>s/<c>#import</c>s
/// (<see cref="IncludedUnits"/>), built by <see cref="BcsParser.ParseProgram"/>.
/// Each included unit's own *file-scope* symbols (never its locals/
/// parameters - see <see cref="BcsCompilationUnit.CollectFileScopeSymbols"/>'s
/// own remarks) are folded in and stamped with that file's resolved path
/// via <see cref="BcsSymbol.SourcePath"/>, so completion/hover/go-to-
/// definition can see across the whole include graph, not just the one
/// open buffer, and go-to-definition knows which file to jump to when
/// that isn't this one.
///
/// <see cref="Diagnostics"/> is deliberately just <see cref="MainUnit"/>'s
/// own - an included file's syntax errors are never surfaced here. This
/// is a real, deliberate scope limit, not an oversight: <see cref="BcsDiagnostic"/>
/// has no file field, so attributing an included file's own error back
/// to the *including* file's diagnostics would be actively misleading,
/// and this pass only ever needs symbols out of an included file, not a
/// second opinion on whether it's well-formed.
/// </summary>
public sealed class BcsProgram
{
    public BcsCompilationUnit MainUnit { get; }
    public IReadOnlyList<(string Path, BcsCompilationUnit Unit)> IncludedUnits { get; }
    public List<BcsDiagnostic> Diagnostics { get; }

    public BcsProgram(BcsCompilationUnit mainUnit, IReadOnlyList<(string Path, BcsCompilationUnit Unit)> includedUnits, List<BcsDiagnostic> diagnostics)
    {
        MainUnit = mainUnit;
        IncludedUnits = includedUnits;
        Diagnostics = diagnostics;
    }

    /// <summary>Same as <see cref="BcsCompilationUnit.CollectSymbolsVisibleAt"/>, extended across the whole include graph.</summary>
    public IReadOnlyList<BcsSymbol> CollectSymbolsVisibleAt(int line)
    {
        var symbols = new List<BcsSymbol>(MainUnit.CollectSymbolsVisibleAt(line));
        foreach (var (path, unit) in IncludedUnits)
        {
            foreach (var symbol in unit.CollectFileScopeSymbols()) symbols.Add(symbol with { SourcePath = path });
        }
        return symbols;
    }

    /// <summary>
    /// Same as <see cref="BcsCompilationUnit.FindDeclaration"/>, extended
    /// across the whole include graph - the main file's own local-then-
    /// file-scope resolution always wins first (unchanged), falling back
    /// to the first included file (in <c>#include</c>/<c>#import</c>
    /// order) that declares a matching name at its own file scope.
    /// </summary>
    public BcsSymbol? FindDeclaration(string name, int line)
    {
        var local = MainUnit.FindDeclaration(name, line);
        if (local != null) return local;

        foreach (var (path, unit) in IncludedUnits)
        {
            var match = unit.CollectFileScopeSymbols()
                .Where(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
                .Select(s => (BcsSymbol?)(s with { SourcePath = path }))
                .FirstOrDefault();
            if (match != null) return match;
        }

        return null;
    }
}
