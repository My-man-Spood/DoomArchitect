using DoomArchitect.Core.ZDoom.Bcs;

namespace DoomArchitect.Core.Tests.ZDoom.Bcs;

/// <summary>
/// Real temp files on real disk, read via plain <see cref="File.ReadAllText"/> -
/// no mocking of the <c>readFile</c> delegate, matching this project's
/// own "verify against real behavior" standard for everything else in
/// this BCS pass. xUnit gives each test method its own fresh instance,
/// so the constructor/<see cref="Dispose"/> pair is per-test setup/teardown,
/// not shared state.
/// </summary>
public class BcsProgramTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("bcs-program-tests-").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string WriteFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    private static string? ReadFile(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

    [Fact]
    public void ParseProgram_IncludedFunction_IsVisibleInCollectSymbolsVisibleAt()
    {
        var includedPath = WriteFile("shared.acs", "function int Helper() { }");
        var mainPath = WriteFile("main.acs", "#include \"shared.acs\"\n");

        var program = BcsParser.ParseProgram(File.ReadAllText(mainPath), mainPath, ReadFile);

        Assert.Contains(program.CollectSymbolsVisibleAt(1), s => s.Name == "Helper" && s.SourcePath == includedPath);
    }

    [Fact]
    public void ParseProgram_FindDeclaration_ResolvesToIncludedFileWithItsOwnSourcePath()
    {
        var includedPath = WriteFile("shared.acs", "function int Helper() { }");
        var mainPath = WriteFile("main.acs", "#include \"shared.acs\"\n");

        var program = BcsParser.ParseProgram(File.ReadAllText(mainPath), mainPath, ReadFile);

        var declaration = program.FindDeclaration("Helper", 1);
        Assert.NotNull(declaration);
        Assert.Equal(includedPath, declaration!.Value.SourcePath);
    }

    [Fact]
    public void ParseProgram_RelativeIncludePath_ResolvesAgainstIncludingFilesOwnDirectory()
    {
        var includedPath = WriteFile(Path.Combine("sub", "shared.acs"), "function int Helper() { }");
        var mainPath = WriteFile(Path.Combine("sub", "main.acs"), "#include \"shared.acs\"\n");

        var program = BcsParser.ParseProgram(File.ReadAllText(mainPath), mainPath, ReadFile);

        Assert.Contains(program.IncludedUnits, u => string.Equals(u.Path, includedPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ParseProgram_CircularIncludes_TerminatesAndDoesNotDuplicate()
    {
        // Confirmed real compiler behavior (zt-bcc's own task.c): files are deduped by resolved identity, so a cycle is parsed at most once per file, never infinitely.
        var aPath = Path.Combine(_tempDir, "a.acs");
        var bPath = Path.Combine(_tempDir, "b.acs");
        File.WriteAllText(aPath, "#include \"b.acs\"\nfunction int FromA() { }");
        File.WriteAllText(bPath, "#include \"a.acs\"\nfunction int FromB() { }");

        var program = BcsParser.ParseProgram(File.ReadAllText(aPath), aPath, ReadFile);

        Assert.Single(program.IncludedUnits); // only b.acs - the cycle back to a.acs itself is caught, not re-added
        Assert.Contains(program.CollectSymbolsVisibleAt(1), s => s.Name == "FromB");
    }

    [Fact]
    public void ParseProgram_MissingIncludedFile_ReportsAWarningAtTheDirectivesOwnPosition()
    {
        var mainPath = WriteFile("main.acs", "#include \"doesnotexist.acs\"\n");

        var program = BcsParser.ParseProgram(File.ReadAllText(mainPath), mainPath, ReadFile);

        Assert.Empty(program.IncludedUnits);
        var diagnostic = Assert.Single(program.Diagnostics);
        Assert.Equal(BcsDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("doesnotexist.acs", diagnostic.Message);
        Assert.Equal(1, diagnostic.Line); // the '#' itself, on line 1
    }

    [Fact]
    public void ParseProgram_UnresolvableIncludeDeepInAnAlreadyIncludedFile_ReportsNothing()
    {
        // Not actionable from here - that line number belongs to a different file entirely, and BcsDiagnostic has no file field to say which one.
        WriteFile("shared.acs", "#include \"alsomissing.acs\"\nfunction int Helper() { }");
        var mainPath = WriteFile("main.acs", "#include \"shared.acs\"\n");

        var program = BcsParser.ParseProgram(File.ReadAllText(mainPath), mainPath, ReadFile);

        Assert.Single(program.IncludedUnits);
        Assert.Empty(program.Diagnostics);
    }

    [Fact]
    public void ParseProgram_ImportDirective_SurfacesSymbolsTheSameWayIncludeDoes()
    {
        var importedPath = WriteFile("lib.acs", "function int LibFunc() { }");
        var mainPath = WriteFile("main.acs", "#import \"lib.acs\"\n");

        var program = BcsParser.ParseProgram(File.ReadAllText(mainPath), mainPath, ReadFile);

        Assert.Contains(program.CollectSymbolsVisibleAt(1), s => s.Name == "LibFunc" && s.SourcePath == importedPath);
    }

    [Fact]
    public void ParseProgram_UnsavedBufferWithNoSourcePath_SkipsRelativeIncludesAndReportsWhy()
    {
        var program = BcsParser.ParseProgram("#include \"shared.acs\"\n", null, ReadFile);

        Assert.Empty(program.IncludedUnits);
        var diagnostic = Assert.Single(program.Diagnostics);
        Assert.Equal(BcsDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("save this file first", diagnostic.Message);
    }

    [Fact]
    public void ParseProgram_MainFilesOwnSymbol_HasEmptySourcePath()
    {
        var mainPath = WriteFile("main.acs", "function int Local() { }\n");

        var program = BcsParser.ParseProgram(File.ReadAllText(mainPath), mainPath, ReadFile);

        Assert.Contains(program.CollectSymbolsVisibleAt(1), s => s.Name == "Local" && s.SourcePath == "");
    }
}
