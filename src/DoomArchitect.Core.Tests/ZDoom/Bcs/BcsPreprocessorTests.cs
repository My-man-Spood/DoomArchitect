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

    [Fact]
    public void Parse_IfdefWithDefinedMacro_IncludesItsContent()
    {
        var (unit, diagnostics) = BcsParser.Parse("#define FEATURE\n#ifdef FEATURE\nint included = 1;\n#endif\n");

        Assert.Empty(diagnostics);
        Assert.Contains(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "included"));
    }

    [Fact]
    public void Parse_IfdefWithUndefinedMacro_ExcludesItsContent()
    {
        var (unit, diagnostics) = BcsParser.Parse("#ifdef FEATURE\nint excluded = 1;\n#endif\n");

        Assert.Empty(diagnostics);
        Assert.DoesNotContain(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "excluded"));
    }

    [Fact]
    public void Parse_IfndefWithUndefinedMacro_IncludesItsContent()
    {
        var (unit, diagnostics) = BcsParser.Parse("#ifndef FEATURE\nint included = 1;\n#endif\n");

        Assert.Empty(diagnostics);
        Assert.Contains(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "included"));
    }

    [Fact]
    public void Parse_IfndefWithDefinedMacro_ExcludesItsContent()
    {
        var (unit, diagnostics) = BcsParser.Parse("#define FEATURE\n#ifndef FEATURE\nint excluded = 1;\n#endif\n");

        Assert.Empty(diagnostics);
        Assert.DoesNotContain(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "excluded"));
    }

    [Fact]
    public void Parse_ElseBranch_TakenWhenIfdefConditionIsFalse()
    {
        var (unit, diagnostics) = BcsParser.Parse("#ifdef FEATURE\nint ifBranch = 1;\n#else\nint elseBranch = 1;\n#endif\n");

        Assert.Empty(diagnostics);
        Assert.DoesNotContain(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "ifBranch"));
        Assert.Contains(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "elseBranch"));
    }

    [Fact]
    public void Parse_ElseBranch_SkippedWhenIfdefConditionIsTrue()
    {
        var (unit, diagnostics) = BcsParser.Parse("#define FEATURE\n#ifdef FEATURE\nint ifBranch = 1;\n#else\nint elseBranch = 1;\n#endif\n");

        Assert.Empty(diagnostics);
        Assert.Contains(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "ifBranch"));
        Assert.DoesNotContain(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "elseBranch"));
    }

    [Fact]
    public void Parse_NestedIfdefInsideSkippedRegion_DoesNotStopAtTheNestedEndif()
    {
        // The whole outer block is skipped - a nested #ifdef/#endif pair inside it must not be mistaken for the outer block's own #endif (depth tracking in SkipInactiveRegion).
        var (unit, diagnostics) = BcsParser.Parse(
            "#ifdef OUTER\n" +
            "#ifdef INNER\n" +
            "int nested = 1;\n" +
            "#endif\n" +
            "int afterNested = 1;\n" +
            "#endif\n" +
            "int afterOuter = 1;\n");

        Assert.Empty(diagnostics);
        Assert.DoesNotContain(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "nested"));
        Assert.DoesNotContain(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "afterNested"));
        Assert.Contains(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "afterOuter"));
    }

    [Fact]
    public void Parse_ElseWithNoOpenIf_ReportsADiagnostic()
    {
        var (_, diagnostics) = BcsParser.Parse("#else\nint x = 1;\n");
        Assert.Contains(diagnostics, d => d.Message.Contains("with no open"));
    }

    [Fact]
    public void Parse_EndifWithNoOpenIf_ReportsADiagnostic()
    {
        var (_, diagnostics) = BcsParser.Parse("#endif\nint x = 1;\n");
        Assert.Contains(diagnostics, d => d.Message.Contains("with no open"));
    }

    [Fact]
    public void Parse_UnclosedIfdef_ReportsADiagnosticAtEndOfFile()
    {
        var (_, diagnostics) = BcsParser.Parse("#ifdef FEATURE\nint x = 1;\n");
        Assert.Contains(diagnostics, d => d.Message.Contains("missing #endif"));
    }

    [Fact]
    public void Parse_BareIf_IsToleratedAsAlwaysTrue()
    {
        // No real constant-expression evaluator yet (deferred) - a bare #if's own condition tokens are discarded and the branch is simply taken.
        var (unit, diagnostics) = BcsParser.Parse("#if 1\nint included = 1;\n#endif\n");

        Assert.Empty(diagnostics);
        Assert.Contains(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "included"));
    }
}
