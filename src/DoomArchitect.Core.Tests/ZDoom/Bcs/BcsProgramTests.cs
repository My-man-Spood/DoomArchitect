using DoomArchitect.Core.ZDoom.Bcs;

namespace DoomArchitect.Core.Tests.ZDoom.Bcs;

/// <summary>
/// Real temp files on real disk, read via plain <see cref="File.ReadAllText"/> -
/// no mocking of the <c>readFile</c> delegate, matching this project's
/// own "verify against real behavior" standard for everything else in
/// this BCS pass. xUnit gives each test method its own fresh instance,
/// so the constructor/<see cref="Dispose"/> pair is per-test setup/teardown,
/// not shared state.
///
/// Phase 5 rewrote this whole file against <see cref="BcsProgram"/>'s new
/// shape (<c>Unit</c>/<c>IncludedPaths</c> replacing <c>MainUnit</c>/
/// <c>IncludedUnits</c>) - every real behavior the old file protected is
/// still covered here, plus new tests for the actual point of Phase 5:
/// true cross-file macro visibility.
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

        Assert.Contains(program.IncludedPaths, p => string.Equals(p, includedPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ParseProgram_CircularIncludes_TerminatesAndDoesNotDuplicate()
    {
        // Confirmed real compiler behavior (zt-bcc's own source.c, p_load_included_source): a true cycle (this exact file still actively open up the current chain) is caught and reported - "file already being loaded" - rather than silently re-spliced into an infinite loop.
        var aPath = Path.Combine(_tempDir, "a.acs");
        var bPath = Path.Combine(_tempDir, "b.acs");
        File.WriteAllText(aPath, "#include \"b.acs\"\nfunction int FromA() { }");
        File.WriteAllText(bPath, "#include \"a.acs\"\nfunction int FromB() { }");

        var program = BcsParser.ParseProgram(File.ReadAllText(aPath), aPath, ReadFile);

        Assert.Single(program.IncludedPaths); // only b.acs - the cycle back to a.acs itself is caught, not re-spliced
        Assert.Contains(program.CollectSymbolsVisibleAt(1), s => s.Name == "FromB");
        Assert.Contains(program.CollectSymbolsVisibleAt(1), s => s.Name == "FromA");
        Assert.Contains(program.Diagnostics, d => d.Message.Contains("already being loaded"));
    }

    [Fact]
    public void ParseProgram_IncludingTheSameFileTwiceFromDifferentDirectives_ReallySplicesItTwice()
    {
        // Confirmed real compiler behavior (zt-bcc's own source.c): #include has NO general dedup at all - the same file genuinely gets re-spliced every time it's #included (a real shared file needs its own manual #ifndef/#define include guard to be safe - plain #include alone doesn't protect against this, by design).
        WriteFile("shared.acs", "int counter = 0;\n");
        var mainPath = WriteFile("main.acs", "#include \"shared.acs\"\n#include \"shared.acs\"\n");

        var program = BcsParser.ParseProgram(File.ReadAllText(mainPath), mainPath, ReadFile);

        Assert.Equal(2, program.CollectSymbolsVisibleAt(1).Count(s => s.Name == "counter"));
    }

    [Fact]
    public void ParseProgram_ImportingTheSameLibraryTwice_IsASilentNoOpTheSecondTime()
    {
        // Confirmed real compiler behavior (zt-bcc's own library.c, load_imported_lib: "Return the library if it is already loaded") - genuinely different from #include's own lack of dedup, by design, so many files can each #import a shared library without caring whether another one already did.
        WriteFile("lib.acs", "int counter = 0;\n");
        var mainPath = WriteFile("main.acs", "#import \"lib.acs\"\n#import \"lib.acs\"\n");

        var program = BcsParser.ParseProgram(File.ReadAllText(mainPath), mainPath, ReadFile);

        Assert.Single(program.CollectSymbolsVisibleAt(1), s => s.Name == "counter");
        Assert.Empty(program.Diagnostics);
    }

    [Fact]
    public void ParseProgram_FileImportingItself_ReportsTheRealDiagnostic()
    {
        var mainPath = WriteFile("main.acs", "#import \"main.acs\"\n");

        var program = BcsParser.ParseProgram(File.ReadAllText(mainPath), mainPath, ReadFile);

        Assert.Contains(program.Diagnostics, d => d.Message.Contains("attempting to import itself"));
    }

    [Fact]
    public void ParseProgram_MissingIncludedFile_ReportsAWarningAtTheDirectivesOwnPosition()
    {
        var mainPath = WriteFile("main.acs", "#include \"doesnotexist.acs\"\n");

        var program = BcsParser.ParseProgram(File.ReadAllText(mainPath), mainPath, ReadFile);

        Assert.Empty(program.IncludedPaths);
        var diagnostic = Assert.Single(program.Diagnostics);
        Assert.Equal(BcsDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("doesnotexist.acs", diagnostic.Message);
        Assert.Equal(1, diagnostic.Line); // the '#' itself, on line 1
        Assert.Equal("", diagnostic.SourcePath); // the main file's own problem
    }

    [Fact]
    public void ParseProgram_UnresolvableIncludeDeepInAnAlreadyIncludedFile_ReportsNothing()
    {
        // Not attributable to the EDITING buffer - it's a real problem now, just correctly scoped to the included file itself, which isn't what this diagnostic class reports on.
        WriteFile("shared.acs", "#include \"alsomissing.acs\"\nfunction int Helper() { }");
        var mainPath = WriteFile("main.acs", "#include \"shared.acs\"\n");

        var program = BcsParser.ParseProgram(File.ReadAllText(mainPath), mainPath, ReadFile);

        Assert.Single(program.IncludedPaths);
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

        Assert.Empty(program.IncludedPaths);
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

    [Fact]
    public void ParseProgram_MacroDefinedInAnIncludedFile_ExpandsInTheIncludingFile()
    {
        // The actual point of Phase 5 - true splicing, not post-hoc symbol merging. Decisive proof, same reasoning as every other macro-expansion test this session: unexpanded, "OPEN 1 + 2 CLOSE" leaves a diagnostic (expected ';'); correctly spliced and expanded, it reads as "( 1 + 2 )" - clean.
        WriteFile("shared.acs", "#define OPEN (\n#define CLOSE )\n");
        var mainPath = WriteFile("main.acs", "#include \"shared.acs\"\nint x = OPEN 1 + 2 CLOSE;\n");

        var program = BcsParser.ParseProgram(File.ReadAllText(mainPath), mainPath, ReadFile);

        Assert.Empty(program.Diagnostics);
    }

    [Fact]
    public void ParseProgram_MacroDefinedInTheMainFileBeforeTheInclude_ExpandsInsideTheIncludedFileToo()
    {
        // True splice symmetry: a macro defined ABOVE the #include is visible to the spliced-in content too, not just the other direction.
        WriteFile("shared.acs", "int x = OPEN 1 + 2 CLOSE;\n");
        var mainPath = WriteFile("main.acs", "#define OPEN (\n#define CLOSE )\n#include \"shared.acs\"\n");

        var program = BcsParser.ParseProgram(File.ReadAllText(mainPath), mainPath, ReadFile);

        Assert.Empty(program.Diagnostics);
    }

    [Fact]
    public void ParseProgram_UndefInsideAnIncludedFile_AffectsTheRestOfTheIncludingFile()
    {
        // Order-dependent correctness, not just "macros from both files exist somewhere" - the #undef happens AT a specific point in the splice and must take effect from there on, exactly as if it had been textually written inline.
        WriteFile("shared.acs", "#undef FEATURE\n");
        var mainPath = WriteFile("main.acs",
            "#define FEATURE\n#include \"shared.acs\"\n#ifdef FEATURE\nint excluded = 1;\n#else\nint included = 1;\n#endif\n");

        var program = BcsParser.ParseProgram(File.ReadAllText(mainPath), mainPath, ReadFile);

        Assert.Empty(program.Diagnostics);
        Assert.DoesNotContain(program.CollectSymbolsVisibleAt(1), s => s.Name == "excluded");
        Assert.Contains(program.CollectSymbolsVisibleAt(1), s => s.Name == "included");
    }

    [Fact]
    public void ParseProgram_SyntaxErrorInsideAnIncludedFile_NowSurfacesWithTheRightSourcePath()
    {
        // Fixes a previously "deliberately out of scope" limitation: BcsDiagnostic had no file field before Phase 5, so an included file's own real syntax errors could never be attributed correctly and were simply dropped. True splicing makes this correct for free.
        var includedPath = WriteFile("shared.acs", "int x = ;\n");
        var mainPath = WriteFile("main.acs", "#include \"shared.acs\"\n");

        var program = BcsParser.ParseProgram(File.ReadAllText(mainPath), mainPath, ReadFile);

        Assert.Contains(program.Diagnostics, d => d.SourcePath == includedPath);
    }

    [Fact]
    public void ParseProgram_UnclosedIfdefInsideAnIncludedFile_IsReportedOnceAgainstThatFile_WithoutCorruptingTheIncludingFile()
    {
        var includedPath = WriteFile("shared.acs", "#ifdef SOMETHING\nint insideUnclosedBlock = 1;\n");
        var mainPath = WriteFile("main.acs", "#include \"shared.acs\"\nint after = 1;\n");

        var program = BcsParser.ParseProgram(File.ReadAllText(mainPath), mainPath, ReadFile);

        Assert.Contains(program.Diagnostics, d => d.Message.Contains("missing #endif") && d.SourcePath == includedPath);
        Assert.DoesNotContain(program.CollectSymbolsVisibleAt(1), s => s.Name == "insideUnclosedBlock"); // SOMETHING is undefined - the block's own content is correctly skipped
        // The including file's own content after the #include must still parse correctly - not swallowed by SkipInactiveRegion incorrectly continuing to hunt for a sibling that no longer exists.
        Assert.Contains(program.CollectSymbolsVisibleAt(1), s => s.Name == "after");
    }
}
