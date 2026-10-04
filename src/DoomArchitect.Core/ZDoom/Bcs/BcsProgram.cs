namespace DoomArchitect.Core.ZDoom.Bcs;

/// <summary>
/// The result of <see cref="BcsParser.ParseProgram"/> - one continuous
/// parse across the whole `#include`/`#import` graph (true textual
/// splicing, confirmed real architecture - see <see cref="BcsPreprocessor"/>'s
/// own remarks), not a separate per-file parse merged afterward the way
/// this type used to work. <see cref="Unit"/>'s own declarations each
/// carry their real origin file via <see cref="BcsNode.SourcePath"/>/
/// <see cref="BcsSymbol.SourcePath"/> (empty for the main file, a real
/// resolved path for anything spliced in), which is what makes
/// <see cref="CollectSymbolsVisibleAt"/>/<see cref="FindDeclaration"/>
/// still correct per-file even though everything now lives in one
/// shared <see cref="BcsCompilationUnit.Members"/> list.
/// </summary>
public sealed class BcsProgram
{
    public BcsCompilationUnit Unit { get; }
    public List<BcsDiagnostic> Diagnostics { get; }

    /// <summary>Every resolved path actually spliced in via `#include`/`#import`, in encounter order.</summary>
    public IReadOnlyList<string> IncludedPaths { get; }

    public BcsProgram(BcsCompilationUnit unit, List<BcsDiagnostic> diagnostics, IReadOnlyList<string> includedPaths)
    {
        Unit = unit;
        Diagnostics = diagnostics;
        IncludedPaths = includedPaths;
    }

    /// <summary>Same as <see cref="BcsCompilationUnit.CollectSymbolsVisibleAt"/> - <paramref name="atPath"/> defaults to the main file, which is what every real caller (confirmed via exploration: both LSP handlers and the in-app <c>ScriptDocument</c>) always means.</summary>
    public IReadOnlyList<BcsSymbol> CollectSymbolsVisibleAt(int line, string atPath = "") => Unit.CollectSymbolsVisibleAt(line, atPath);

    /// <summary>Same as <see cref="BcsCompilationUnit.FindDeclaration"/> - same <paramref name="atPath"/> default/reasoning.</summary>
    public BcsSymbol? FindDeclaration(string name, int line, string atPath = "") => Unit.FindDeclaration(name, line, atPath);
}
