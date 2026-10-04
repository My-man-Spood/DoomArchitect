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
    public void Parse_IfWithATrueConstantCondition_IncludesItsContent()
    {
        var (unit, diagnostics) = BcsParser.Parse("#if 1\nint included = 1;\n#endif\n");

        Assert.Empty(diagnostics);
        Assert.Contains(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "included"));
    }

    [Fact]
    public void Parse_IfWithAFalseConstantCondition_ExcludesItsContent()
    {
        var (unit, diagnostics) = BcsParser.Parse("#if 0\nint excluded = 1;\n#endif\nint after = 1;\n");

        Assert.Empty(diagnostics);
        Assert.DoesNotContain(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "excluded"));
        Assert.Contains(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "after"));
    }

    [Fact]
    public void Parse_IfWithAnArithmeticCondition_EvaluatesRealOperatorPrecedence()
    {
        // 1 + 2 * 3 == 7, not 9 - decisive proof real precedence (not naive left-to-right) is applied.
        var (unit, diagnostics) = BcsParser.Parse("#if 1 + 2 * 3 == 7\nint included = 1;\n#endif\n");

        Assert.Empty(diagnostics);
        Assert.Contains(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "included"));
    }

    [Fact]
    public void Parse_IfWithATernaryCondition_EvaluatesCorrectly()
    {
        var (unit, diagnostics) = BcsParser.Parse("#if 1 ? 0 : 1\nint excluded = 1;\n#endif\n");

        Assert.Empty(diagnostics);
        Assert.DoesNotContain(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "excluded"));
    }

    [Fact]
    public void Parse_IfWithDefined_IsTrueWhenTheMacroIsDefined()
    {
        var (unit, diagnostics) = BcsParser.Parse("#define FEATURE\n#if defined(FEATURE)\nint included = 1;\n#endif\n");

        Assert.Empty(diagnostics);
        Assert.Contains(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "included"));
    }

    [Fact]
    public void Parse_IfWithDefinedWithoutParens_IsSupported()
    {
        var (unit, diagnostics) = BcsParser.Parse("#define FEATURE\n#if defined FEATURE\nint included = 1;\n#endif\n");

        Assert.Empty(diagnostics);
        Assert.Contains(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "included"));
    }

    [Fact]
    public void Parse_IfWithDefined_IsFalseWhenTheMacroIsNotDefined()
    {
        var (unit, diagnostics) = BcsParser.Parse("#if defined(FEATURE)\nint excluded = 1;\n#endif\n");

        Assert.Empty(diagnostics);
        Assert.DoesNotContain(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "excluded"));
    }

    [Fact]
    public void Parse_IfWithDefined_NeverExpandsTheNameItself()
    {
        // Confirmed real semantics (eval_defined's own non-expanding reads): "defined" must see the raw name "FEATURE", not whatever it expands to - if it mistakenly macro-expanded the name first, this would misbehave (FEATURE's own body "1 1" isn't a valid single identifier for 'defined' to test at all).
        var (unit, diagnostics) = BcsParser.Parse("#define FEATURE 1 1\n#if defined(FEATURE)\nint included = 1;\n#endif\n");

        Assert.Empty(diagnostics);
        Assert.Contains(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "included"));
    }

    [Fact]
    public void Parse_IfConditionCanReferenceAMacro_AndItIsExpanded()
    {
        var (unit, diagnostics) = BcsParser.Parse("#define VERSION 2\n#if VERSION >= 2\nint included = 1;\n#endif\n");

        Assert.Empty(diagnostics);
        Assert.Contains(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "included"));
    }

    [Fact]
    public void Parse_IfWithAnUndefinedIdentifier_EvaluatesItAsZero()
    {
        // Confirmed real convention (eval_id): a plain identifier that isn't a macro evaluates to 0, same as the standard C-preprocessor behavior.
        var (unit, diagnostics) = BcsParser.Parse("#if SOME_UNDEFINED_NAME\nint excluded = 1;\n#else\nint included = 1;\n#endif\n");

        Assert.Empty(diagnostics);
        Assert.DoesNotContain(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "excluded"));
        Assert.Contains(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "included"));
    }

    [Fact]
    public void Parse_ElifChain_PicksTheFirstTrueBranchAmongSeveral()
    {
        // Regression coverage specifically for a chain with MORE than one #elif - the old Phase 2 behavior ("the first #elif reached while searching is always taken, unevaluated") would have picked the wrong branch here.
        var (unit, diagnostics) = BcsParser.Parse(
            "#if 0\n" +
            "int first = 1;\n" +
            "#elif 0\n" +
            "int second = 1;\n" +
            "#elif 1\n" +
            "int third = 1;\n" +
            "#else\n" +
            "int fourth = 1;\n" +
            "#endif\n");

        Assert.Empty(diagnostics);
        Assert.DoesNotContain(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "first"));
        Assert.DoesNotContain(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "second"));
        Assert.Contains(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "third"));
        Assert.DoesNotContain(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "fourth"));
    }

    [Fact]
    public void Parse_ElifChain_FallsThroughToElseWhenEveryElifIsFalse()
    {
        var (unit, diagnostics) = BcsParser.Parse("#if 0\nint first = 1;\n#elif 0\nint second = 1;\n#else\nint third = 1;\n#endif\n");

        Assert.Empty(diagnostics);
        Assert.DoesNotContain(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "first"));
        Assert.DoesNotContain(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "second"));
        Assert.Contains(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "third"));
    }

    [Fact]
    public void Parse_IfDivisionByZero_ReportsADiagnostic()
    {
        var (_, diagnostics) = BcsParser.Parse("#if 1 / 0\nint x = 1;\n#endif\n");
        Assert.Contains(diagnostics, d => d.Message.Contains("division by zero"));
    }

    [Fact]
    public void Parse_IfWithAnInvalidExpression_ReportsADiagnostic()
    {
        // ')' is never a valid way to start a primary expression, confirmed real (eval_primary's own switch has no case for it).
        var (_, diagnostics) = BcsParser.Parse("#if )\nint x = 1;\n#endif\n");
        Assert.Contains(diagnostics, d => d.Message.Contains("invalid expression"));
    }

    [Fact]
    public void Parse_IfWithAMissingExpression_ReportsADiagnostic()
    {
        var (_, diagnostics) = BcsParser.Parse("#if\nint x = 1;\n#endif\n");
        Assert.Contains(diagnostics, d => d.Message.Contains("missing expression"));
    }

    [Fact]
    public void Parse_IfConditionDoesNotSwallowTheFollowingLine()
    {
        // Decisive regression coverage for the exact pushback bug found live: the condition's own trailing lookahead read must not consume the line's terminating newline without pushing it back, or everything on the NEXT line gets mistaken for trailing garbage on the #if line.
        var (unit, diagnostics) = BcsParser.Parse("#if 1\nint a = 1;\nint b = 2;\n#endif\n");

        Assert.Empty(diagnostics);
        Assert.Contains(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "a"));
        Assert.Contains(unit.Members, m => m is BcsVariableDeclaration v && v.DeclaratorNames.Any(d => d.Name == "b"));
    }

    [Fact]
    public void Parse_Stringize_TurnsTheRawArgumentIntoASingleStringLiteral()
    {
        // Decisive proof, same reasoning as the Phase 1 OPEN/CLOSE tests: if '#x' were NOT turned into one string literal, the three raw argument tokens "a b c" would pass straight through as three separate identifiers, and "str s = a b c;" is a real diagnostic (expected ';'); correctly stringized, it reads as "str s = \"a b c\";" - one clean string-literal initializer.
        // (Macro named STRINGIFY, not STR - "str" is itself a real reserved word, confirmed from user.c's own keyword table, so it can never be a macro name either, same as in the real compiler.)
        var (_, diagnostics) = BcsParser.Parse("#define STRINGIFY(x) #x\nstr s = STRINGIFY(a b c);\n");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Parse_TokenPasting_JoinsTwoIdentifiersIntoOne()
    {
        // Decisive proof: unpasted, "CAT(x, y)" would leave "x y" as two separate identifiers - "int z = x y;" is a real diagnostic (expected ';'); correctly pasted into the single identifier "xy", it's clean.
        var (_, diagnostics) = BcsParser.Parse("#define CAT(a, b) a ## b\nint xy = 1;\nint z = CAT(x, y);\n");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Parse_TokenPasting_CanJoinAnIdentifierAndADigitIntoOneNewIdentifier()
    {
        var (_, diagnostics) = BcsParser.Parse("#define MAKE(n) item ## n\nint item1 = 1;\nint w = MAKE(1);\n");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Parse_ChainedTokenPasting_ResolvesLeftToRight()
    {
        // The middle operand ('b') is shared between both '##'s - a real regression risk if pass 2 treats the two pastes as independent non-overlapping pairs instead of folding the chain.
        var (_, diagnostics) = BcsParser.Parse("#define CAT3(a, b, c) a ## b ## c\nint xyz = 1;\nint w = CAT3(x, y, z);\n");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Parse_TokenPasting_WithAnEmptyArgumentOnOneSide_LeavesTheOtherSideStandingAlone()
    {
        // Confirmed real "placemarker" behavior: an empty argument adjacent to '##' contributes nothing, and the paste simply doesn't happen - the surviving side passes through unchanged rather than erroring.
        var (_, diagnostics) = BcsParser.Parse("#define CAT(a, b) a ## b\nint x = 1;\nint w = CAT(x, );\n");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Parse_TokenPasting_WithEmptyArgumentsOnBothSides_ProducesNothing()
    {
        // Decisive proof nothing leaks through: if any stray token survived between "1" and "+ 2", two primaries in a row with no operator between them would be a real diagnostic.
        var (_, diagnostics) = BcsParser.Parse("#define CAT(a, b) a ## b\nint x = 1 CAT(,) + 2;\n");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Parse_TokenPasting_ThatProducesAnInvalidToken_ReportsADiagnostic()
    {
        // Pasting a decimal literal directly against an identifier ("1" + "x" = "1x") doesn't re-lex as one single token - a real, reported divergence from the real compiler's own hand-built compatibility table (see Paste's own remarks), not a silent no-op.
        var (_, diagnostics) = BcsParser.Parse("#define BAD(a, b) a ## b\nint z = BAD(1, x);\n");
        Assert.Contains(diagnostics, d => d.Message.Contains("invalid token"));
    }

    [Fact]
    public void Parse_HashHashAtBeginningOfMacroBody_ReportsADiagnostic()
    {
        var (_, diagnostics) = BcsParser.Parse("#define BAD(x) ## x\n");
        Assert.Contains(diagnostics, d => d.Message.Contains("beginning of macro body"));
    }

    [Fact]
    public void Parse_HashHashAtEndOfMacroBody_ReportsADiagnostic()
    {
        var (_, diagnostics) = BcsParser.Parse("#define BAD(x) x ##\n");
        Assert.Contains(diagnostics, d => d.Message.Contains("end of macro body"));
    }

    [Fact]
    public void Parse_HashNotFollowedByAParameter_ReportsADiagnostic()
    {
        var (_, diagnostics) = BcsParser.Parse("#define BAD(x) #y\n");
        Assert.Contains(diagnostics, d => d.Message.Contains("not a parameter"));
    }

    [Fact]
    public void Parse_HashInAnObjectLikeMacro_IsJustALiteralHash_NotValidated()
    {
        // Confirmed real semantics (TK_PROCESSEDHASH): '#' only means "stringize" inside a FUNCTION-like macro's body - in an object-like one it's simply a literal token, never checked against anything.
        var (_, diagnostics) = BcsParser.Parse("#define OBJ #foo\n");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Parse_ObjectLikeMacro_HoverDescribeShowsItsRealValue()
    {
        var (unit, _) = BcsParser.Parse("#define MAX_HEALTH 100\n");

        var declaration = unit.FindDeclaration("MAX_HEALTH", 1);
        Assert.NotNull(declaration);
        Assert.Equal("#define MAX_HEALTH 100", declaration!.Value.Describe());
    }

    [Fact]
    public void Parse_FunctionLikeMacro_HoverDescribeShowsItsRealParametersAndBody()
    {
        var (unit, _) = BcsParser.Parse("#define MAX(a, b) (a > b ? a : b)\n");

        var declaration = unit.FindDeclaration("MAX", 1);
        Assert.NotNull(declaration);
        Assert.Equal("#define MAX(a, b) (a > b ? a : b)", declaration!.Value.Describe());
    }

    [Fact]
    public void Parse_VariadicMacro_HoverDescribeShowsEllipsisNotTheInternalParameterName()
    {
        // __VA_ARGS__ is the real internal name (confirmed real grammar, dirc.c's own read_param_list) but not what the user actually typed - the hover text should show '...', matching the real source.
        var (unit, _) = BcsParser.Parse("#define LOG(fmt, ...) fmt\n");

        var declaration = unit.FindDeclaration("LOG", 1);
        Assert.NotNull(declaration);
        Assert.Equal("#define LOG(fmt, ...) fmt", declaration!.Value.Describe());
    }

    [Fact]
    public void Parse_MacroWithNoValueAtAll_HoverDescribeShowsJustTheName()
    {
        var (unit, _) = BcsParser.Parse("#define FEATURE\n");

        var declaration = unit.FindDeclaration("FEATURE", 1);
        Assert.NotNull(declaration);
        Assert.Equal("#define FEATURE", declaration!.Value.Describe());
    }
}
