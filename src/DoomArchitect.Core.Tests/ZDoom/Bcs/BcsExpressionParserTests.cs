using DoomArchitect.Core.ZDoom.Bcs;

namespace DoomArchitect.Core.Tests.ZDoom.Bcs;

/// <summary>
/// The expression grammar itself (<c>BcsParser.Expressions.cs</c>) has
/// no public surface of its own - it's exercised entirely through
/// declaration initializers, the one place it's wired in. Every case
/// here is deliberately within the grammar's own documented, bounded
/// scope (see that file's own remarks) - these aren't testing fidelity
/// to the full real compiler grammar, just that this pass's own stated
/// scope works and a real structural problem is actually caught.
/// </summary>
public class BcsExpressionParserTests
{
    [Theory]
    [InlineData("int x = 5;")]
    [InlineData("int x = a;")]
    [InlineData("int x = a + b * c;")]
    [InlineData("int x = (a + b) * c;")]
    [InlineData("int x = Foo(a, b);")]
    [InlineData("int x = a ? b : c;")]
    [InlineData("int x = a ?: b;")] // the real "Elvis" ternary form - optional middle operand
    [InlineData("int x = -a;")]
    [InlineData("int x = !flag;")]
    [InlineData("int x = ~a;")]
    [InlineData("int x = a[0];")]
    [InlineData("int x = a.b;")]
    [InlineData("int x = a++;")]
    [InlineData("int x = ++a;")]
    [InlineData("int x = a = b;")] // chained assignment as an expression - rare but real
    [InlineData("int x = int(fixedVal);")] // type-conversion call
    [InlineData("int x = print(s:\"hi\", d:value);")] // the format-cast-tag call-argument allowance
    [InlineData("int x = a << 2 | b & c;")] // confirms real precedence ordering, not just "it parses"
    [InlineData("int x = a == b && c != d;")]
    [InlineData("str x = \"hello\";")]
    [InlineData("int x = true;")]
    [InlineData("int x = null;")]
    public void Parse_ValidInitializerExpression_ProducesNoDiagnostics(string source)
    {
        var (_, diagnostics) = BcsParser.Parse(source);
        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("int x = ;")] // the original motivating case
    [InlineData("int x = 1 +;")]
    [InlineData("int x = (1 + 2;")]
    [InlineData("int x = Foo(1, ;")]
    [InlineData("int x = ?;")]
    public void Parse_MalformedInitializerExpression_ReportsADiagnostic(string source)
    {
        var (_, diagnostics) = BcsParser.Parse(source);
        Assert.NotEmpty(diagnostics);
    }

    [Fact]
    public void Parse_LocalDeclarationInsideScriptBody_ValidInitializer_ProducesNoDiagnostics()
    {
        var (_, diagnostics) = BcsParser.Parse("script 1 open\n{\n    int x = 5;\n}\n");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Parse_LocalDeclarationInsideScriptBody_MalformedInitializer_ReportsADiagnostic()
    {
        var (_, diagnostics) = BcsParser.Parse("script 1 open\n{\n    int x = ;\n}\n");
        Assert.NotEmpty(diagnostics);
    }

    [Fact]
    public void Parse_ConversionCallInsideABodyStatement_IsNotMisdetectedAsANewLocalDeclaration()
    {
        // The specific regression this design exists to prevent: the second
        // 'int' is a type-conversion call (expr.c's read_conversion), not a
        // new declaration - it must never fire the local-declaration
        // hand-off, since it arrives mid-statement (after '='), not at a
        // real statement boundary.
        var (unit, diagnostics) = BcsParser.Parse("script 1 open\n{\n    int x;\n    x = int(1.5);\n}\n");

        Assert.Empty(diagnostics);
        var script = Assert.IsType<BcsScriptDeclaration>(Assert.Single(unit.Members));
        var local = Assert.Single(script.BodyLocals); // only "x" - not a phantom second declaration from the conversion call
        Assert.Equal("x", local.Name);
    }

    [Fact]
    public void Parse_LocalDeclarationInsideNestedBlock_IsStillDetected()
    {
        // atStatementStart becomes true again right after a nested block's own opening '{', not just after ';'/a nested '}'.
        var (unit, diagnostics) = BcsParser.Parse("script 1 open\n{\n    { int nested = 1; }\n}\n");

        Assert.Empty(diagnostics);
        var script = Assert.IsType<BcsScriptDeclaration>(Assert.Single(unit.Members));
        Assert.Contains(script.BodyLocals, s => s.Name == "nested");
    }

    [Fact]
    public void Parse_BadInitializerFollowedByMoreDeclarators_RecoversAndStillCollectsTheRest()
    {
        var (unit, diagnostics) = BcsParser.Parse("int x = , y = 5;\n");

        Assert.NotEmpty(diagnostics);
        var variable = Assert.IsType<BcsVariableDeclaration>(Assert.Single(unit.Members));
        Assert.Contains(variable.DeclaratorNames, d => d.Name == "x");
        Assert.Contains(variable.DeclaratorNames, d => d.Name == "y");
    }

    [Fact]
    public void Parse_BadInitializerFollowedByMoreStatements_RecoversAndStillParsesTheRestOfTheFile()
    {
        var (unit, diagnostics) = BcsParser.Parse("int x = ;\nfunction int Add() { }\n");

        Assert.NotEmpty(diagnostics);
        Assert.Contains(unit.Members, m => m is BcsFunctionDeclaration f && f.Name == "Add");
    }

    [Fact]
    public void Parse_ArrayDeclaratorWithSizeExpression_ProducesNoDiagnosticsAndCollectsTheName()
    {
        var (unit, diagnostics) = BcsParser.Parse("int arr[10];\n");

        Assert.Empty(diagnostics);
        var variable = Assert.IsType<BcsVariableDeclaration>(Assert.Single(unit.Members));
        Assert.Equal("arr", Assert.Single(variable.DeclaratorNames).Name);
    }

    [Fact]
    public void Parse_GlobalModifierPrefixedDeclaration_UsesTheRealTypeNotTheModifierAsEachDeclaratorsType()
    {
        // Confirmed real grammar from dec.c's read_storage/read_object: the modifier and the real type are two separate tokens, and only the real type belongs in a declarator's own Type (shown in hover).
        var (unit, diagnostics) = BcsParser.Parse("global int score;\n");

        Assert.Empty(diagnostics);
        var variable = Assert.IsType<BcsVariableDeclaration>(Assert.Single(unit.Members));
        Assert.Equal("int", variable.TypeKeyword);
        Assert.Equal("int", Assert.Single(variable.DeclaratorNames).Type);
    }
}
