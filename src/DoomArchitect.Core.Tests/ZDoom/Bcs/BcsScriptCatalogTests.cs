using DoomArchitect.Core.ZDoom.Bcs;

namespace DoomArchitect.Core.Tests.ZDoom.Bcs;

public class BcsScriptCatalogTests
{
    [Fact]
    public void Build_NumberedScript_IsIncluded()
    {
        var (unit, _) = BcsParser.Parse("script 1 open\n{\n}\n");

        var entry = Assert.Single(BcsScriptCatalog.Build(unit));

        Assert.Equal("1", entry.Number);
        Assert.False(entry.IsNamedScript);
        Assert.Empty(entry.ParameterNames);
        Assert.Equal(string.Empty, entry.SourcePath);
        Assert.Equal(1, entry.Line);
    }

    [Fact]
    public void Build_ScriptDeclaredViaInclude_CarriesTheIncludedFilesOwnSourcePath()
    {
        var program = BcsParser.ParseProgram(
            "#include \"lib.acs\"\n",
            sourcePath: null,
            readFile: path => path == "lib.acs" ? "script 1 open\n{\n}\n" : null);

        var entry = Assert.Single(BcsScriptCatalog.Build(program.Unit));

        Assert.Equal("lib.acs", entry.SourcePath);
    }

    [Fact]
    public void Build_NamedScript_IsIncluded()
    {
        var (unit, _) = BcsParser.Parse("script \"main\" open\n{\n}\n");

        var entry = Assert.Single(BcsScriptCatalog.Build(unit));

        Assert.Equal("main", entry.Number);
        Assert.True(entry.IsNamedScript);
    }

    [Fact]
    public void Build_DeclaredParameters_AreSurfacedByName()
    {
        var (unit, _) = BcsParser.Parse("script 1 (int tid, int speed)\n{\n}\n");

        var entry = Assert.Single(BcsScriptCatalog.Build(unit));

        Assert.Equal(new[] { "tid", "speed" }, entry.ParameterNames);
    }

    [Fact]
    public void Build_MixOfNumberedAndNamed_SortsNumberedByIndexThenNamedAlphabetically()
    {
        var (unit, _) = BcsParser.Parse(
            "script 10 open\n{\n}\n" +
            "script \"zeta\" open\n{\n}\n" +
            "script 2 open\n{\n}\n" +
            "script \"alpha\" open\n{\n}\n");

        var result = BcsScriptCatalog.Build(unit);

        Assert.Equal(
            new[] { "2", "10", "alpha", "zeta" },
            result.Select(e => e.Number).ToList());
    }

    [Fact]
    public void Build_ScriptDeclaredInsideNamespace_IsStillFound()
    {
        var (unit, _) = BcsParser.Parse("namespace Foo {\nscript 1 open\n{\n}\n}\n");

        var entry = Assert.Single(BcsScriptCatalog.Build(unit));

        Assert.Equal("1", entry.Number);
    }

    [Fact]
    public void Build_NoScripts_ReturnsEmpty()
    {
        var (unit, _) = BcsParser.Parse("function int Foo() { }\n");

        Assert.Empty(BcsScriptCatalog.Build(unit));
    }
}
