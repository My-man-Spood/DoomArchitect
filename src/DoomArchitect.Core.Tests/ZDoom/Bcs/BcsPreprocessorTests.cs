using DoomArchitect.Core.ZDoom.Bcs;

namespace DoomArchitect.Core.Tests.ZDoom.Bcs;

/// <summary>
/// <see cref="BcsPreprocessor"/> has no public surface of its own - it's
/// exercised entirely through <see cref="BcsParser.Parse"/>, the same
/// way <see cref="BcsExpressionParserTests"/> exercises the expression
/// grammar. Several cases deliberately construct a macro whose own
/// *shape* only parses cleanly once substituted (rather than just
/// checking "no crash") - our expression grammar never checks whether
/// a name is actually declared, so a bare, unexpanded macro name is
/// otherwise already syntactically tolerated on its own, same as any
/// other identifier; a flat "no diagnostics" assertion alone wouldn't
/// prove expansion actually happened.
/// </summary>
public class BcsPreprocessorTests
{
    [Fact]
    public void Parse_ObjectLikeMacro_ReallyExpands()
    {
        // Unexpanded, x's initializer is just the bare identifier OPEN, leaving "1 + 2 CLOSE" dangling (expected ';'); expanded, it reads as "( 1 + 2 )" - a clean parenthesized expression.
        var (_, diagnostics) = BcsParser.Parse("#define OPEN (\n#define CLOSE )\nint x = OPEN 1 + 2 CLOSE;\n");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Parse_FunctionLikeMacro_SubstitutesItsParameter()
    {
        // Unexpanded, "OPEN_CALL(1)" is a complete call expression on its own, leaving "2);" dangling (expected ';'); expanded with x=1, OPEN_CALL's body "(x +" becomes "(1 +", and the whole line reads as "(1 + 2);" - clean.
        var (_, diagnostics) = BcsParser.Parse("#define OPEN_CALL(x) (x +\nint y = OPEN_CALL(1) 2);\n");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Parse_FunctionLikeMacroNameNotFollowedByParen_IsNotInvoked()
    {
        // Confirmed real semantics: a function-like macro name not immediately followed by '(' is just an ordinary identifier, not an invocation - and must not disturb anything that follows it either.
        var (unit, diagnostics) = BcsParser.Parse("#define FOO(x) x\nint y = FOO;\nint z = 5;\n");

        Assert.Empty(diagnostics);
        Assert.Contains(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "z"));
    }

    [Fact]
    public void Parse_VariadicMacro_AcceptsMoreArgumentsThanNamedParameters()
    {
        var (_, diagnostics) = BcsParser.Parse("#define LOG(fmt, ...) fmt\nint x = LOG(1, 2, 3);\n");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Parse_FunctionLikeMacroCalledWithWrongArgumentCount_ReportsAWarning()
    {
        var (_, diagnostics) = BcsParser.Parse("#define ADD(a, b) a\nint x = ADD(1);\n");
        Assert.Contains(diagnostics, d => d.Severity == BcsDiagnosticSeverity.Warning && d.Message.Contains("expects 2 argument"));
    }

    [Fact]
    public void Parse_SelfReferencingMacro_TerminatesInsteadOfRecursingForever()
    {
        // Confirmed real behavior (dirc.c's own TK_MACRONAME marking): a macro's own name inside its own body is never re-expanded. A broken guard here would hang this test, not just fail an assertion.
        var (_, diagnostics) = BcsParser.Parse("#define COUNT COUNT + 1\nint x = COUNT;\n");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Parse_IndirectlySelfReferencingMacros_TerminateInsteadOfRecursingForever()
    {
        // The guard is a single set shared across the whole nested-expansion chain, not scoped per-macro - confirms mutual (A -> B -> A) self-reference is caught too, not just direct self-reference.
        var (_, diagnostics) = BcsParser.Parse("#define A B + 1\n#define B A + 1\nint x = A;\n");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Parse_NestedMacroCallAsArgument_ExpandsBeforeSubstitution()
    {
        // The argument "DOUBLE(1)" must be fully expanded to "(1 + 1)" before being substituted into OPEN_CALL's own body - otherwise the result wouldn't balance into a clean expression.
        var (_, diagnostics) = BcsParser.Parse("#define DOUBLE(x) (x + x)\n#define OPEN_CALL(x) (x +\nint y = OPEN_CALL(DOUBLE(1)) 2);\n");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Parse_Undef_RemovesTheMacroSoALaterUseIsNoLongerExpanded()
    {
        // If #undef were a no-op, "OPEN" would still expand to "(" and the whole line would parse cleanly with no diagnostic - this only fails to produce one if #undef is broken.
        var (_, diagnostics) = BcsParser.Parse("#define OPEN (\n#undef OPEN\nint x = OPEN 1 + 2;\n");
        Assert.NotEmpty(diagnostics);
    }

    [Fact]
    public void Parse_DocCommentAboveDefine_StillWorksThroughThePreprocessor()
    {
        var (unit, diagnostics) = BcsParser.Parse("// Max player health.\n#define MAX_HEALTH 100\n");

        Assert.Empty(diagnostics);
        var define = Assert.IsType<BcsDefineDirective>(Assert.Single(unit.Members));
        Assert.Equal("Max player health.", define.DocComment);
    }

    [Fact]
    public void Parse_FunctionLikeMacro_StillGetsItsOwnDefineDirectiveForCompletion()
    {
        var (unit, diagnostics) = BcsParser.Parse("#define ADD(a, b) a + b\nint x = ADD(1, 2) + 3;\n");

        Assert.Empty(diagnostics);
        Assert.Contains(unit.Members, m => m is BcsDefineDirective d && d.Name == "ADD");
    }

    [Fact]
    public void Parse_LibDefine_ExpandsTheSameWayDefineDoes()
    {
        var (_, diagnostics) = BcsParser.Parse("#libdefine OPEN (\n#libdefine CLOSE )\nint x = OPEN 1 + 2 CLOSE;\n");
        Assert.Empty(diagnostics);
    }
}
