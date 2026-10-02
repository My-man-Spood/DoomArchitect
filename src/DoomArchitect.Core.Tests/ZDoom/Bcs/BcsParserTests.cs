using DoomArchitect.Core.ZDoom.Bcs;

namespace DoomArchitect.Core.Tests.ZDoom.Bcs;

public class BcsParserTests
{
    [Fact]
    public void Parse_ScriptOpenFlag_IsAPlainIdentifierNotADedicatedToken()
    {
        // Regression guard for BcsTokenType's tiered split: "open" is absent from the real compiler's reserved-word table, so it must only ever reach the parser as a plain Identifier - never its own token type.
        var (unit, diagnostics) = BcsParser.Parse("script 1 open\n{\n}\n");

        Assert.Empty(diagnostics);
        var script = Assert.IsType<BcsScriptDeclaration>(Assert.Single(unit.Members));
        Assert.Equal("1", script.Number);
        Assert.Equal(new[] { "open" }, script.FlagTokens);
    }

    [Fact]
    public void Parse_ScriptWithTypeAndMultipleFlags_ParsesFlagsInOrder()
    {
        var (unit, diagnostics) = BcsParser.Parse("script \"main\" (void) net clientside\n{\n}\n");

        Assert.Empty(diagnostics);
        var script = Assert.IsType<BcsScriptDeclaration>(Assert.Single(unit.Members));
        Assert.Equal("main", script.Number);
        Assert.Equal(new[] { "net", "clientside" }, script.FlagTokens);
    }

    [Fact]
    public void Parse_MultipleSyntaxErrors_ReturnsADiagnosticPerError()
    {
        // Deliberate divergence from ZDTextParser's halt-on-first-error house style - an LSP needs every diagnostic in the file, not just the first.
        var (_, diagnostics) = BcsParser.Parse("@@@;\n###;\n");

        Assert.True(diagnostics.Count >= 2, $"expected at least 2 diagnostics, got {diagnostics.Count}: {string.Join(", ", diagnostics)}");
    }

    [Fact]
    public void Parse_IncludeDirective_RecordsThePath()
    {
        var (unit, diagnostics) = BcsParser.Parse("#include \"zcommon.acs\"\n");

        Assert.Empty(diagnostics);
        var include = Assert.IsType<BcsIncludeDirective>(Assert.Single(unit.Members));
        Assert.Equal("zcommon.acs", include.Path);
    }

    [Fact]
    public void Parse_ImportDirective_RecordsThePath()
    {
        var (unit, diagnostics) = BcsParser.Parse("#import \"shared.bcs\"\n");

        Assert.Empty(diagnostics);
        var import = Assert.IsType<BcsImportDirective>(Assert.Single(unit.Members));
        Assert.Equal("shared.bcs", import.Path);
    }

    [Fact]
    public void Parse_GlobalVariableDeclaration_CapturesDeclaratorTokens()
    {
        var (unit, diagnostics) = BcsParser.Parse("int 0:myvar;\n");

        Assert.Empty(diagnostics);
        var variable = Assert.IsType<BcsVariableDeclaration>(Assert.Single(unit.Members));
        Assert.Equal("int", variable.TypeKeyword);
        Assert.Equal(new[] { "0", ":", "myvar" }, variable.DeclaratorTokens);
    }

    [Fact]
    public void Parse_EnumDeclaration_ParsesNameAndSkipsBody()
    {
        var (unit, diagnostics) = BcsParser.Parse("enum Colors { Red, Green, Blue };\n");

        Assert.Empty(diagnostics);
        var decl = Assert.IsType<BcsEnumDeclaration>(Assert.Single(unit.Members));
        Assert.Equal("colors", decl.Name); // case-folded, same as every other identifier - see BcsTokenizer's own remarks
    }

    [Fact]
    public void Parse_UnclosedScriptBody_ReportsADiagnosticInsteadOfHanging()
    {
        var (_, diagnostics) = BcsParser.Parse("script 1 open\n{\n");

        Assert.Contains(diagnostics, d => d.Message == "unexpected end of file inside a block");
    }

    [Fact]
    public void Parse_BadDeclarationFollowedByAGoodOne_RecoversAndStillParsesTheSecond()
    {
        var (unit, diagnostics) = BcsParser.Parse("@@@;\nscript 1 open\n{\n}\n");

        Assert.NotEmpty(diagnostics);
        Assert.Contains(unit.Members, m => m is BcsScriptDeclaration);
    }

    [Fact]
    public void Parse_EmptySource_ProducesNoMembersAndNoDiagnostics()
    {
        var (unit, diagnostics) = BcsParser.Parse("");

        Assert.Empty(unit.Members);
        Assert.Empty(diagnostics);
    }
}
