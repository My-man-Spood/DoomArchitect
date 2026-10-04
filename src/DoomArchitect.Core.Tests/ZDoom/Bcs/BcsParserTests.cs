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
        Assert.Equal("Colors", decl.Name); // original casing preserved for a declared name, unlike BcsToken.Value - see BcsTokenizer.RawValue's own remarks
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

    [Fact]
    public void Parse_FunctionWithParameters_ExtractsNameAndParameterNames()
    {
        var (unit, diagnostics) = BcsParser.Parse("function int Add(int a, int b) { }");

        Assert.Empty(diagnostics);
        var function = Assert.IsType<BcsFunctionDeclaration>(Assert.Single(unit.Members));
        Assert.Equal("Add", function.Name); // original casing preserved - see BcsTokenizer.RawValue's own remarks
        Assert.Equal(new[] { "a", "b" }, function.ParameterNames.Select(s => s.Name));
    }

    [Fact]
    public void Parse_ScriptWithParameterShapedParenGroup_ExtractsParameterNames()
    {
        var (unit, diagnostics) = BcsParser.Parse("script 1 (int a, int b)\n{\n}\n");

        Assert.Empty(diagnostics);
        var script = Assert.IsType<BcsScriptDeclaration>(Assert.Single(unit.Members));
        Assert.Equal(new[] { "a", "b" }, script.ParameterNames.Select(s => s.Name));
    }

    [Fact]
    public void Parse_ScriptWithBareFlagParenGroup_ExtractsNoParameterNames()
    {
        // The exact ambiguity this project's own ParseScript doc comment calls out - sidestepped, not resolved: "open" is preceded by '(', not a type keyword, so nothing matches.
        var (unit, diagnostics) = BcsParser.Parse("script 1 (open)\n{\n}\n");

        Assert.Empty(diagnostics);
        var script = Assert.IsType<BcsScriptDeclaration>(Assert.Single(unit.Members));
        Assert.Empty(script.ParameterNames);
    }

    [Fact]
    public void Parse_SpecialWithMultipleCommaSeparatedEntries_ExtractsAllNames()
    {
        // One "special" statement can declare several, comma-separated - confirmed from zt-bcc's own src/parse/dec.c (p_read_special_list).
        var (unit, diagnostics) = BcsParser.Parse("special 1:Foo(1), 2:Bar(2);");

        Assert.Empty(diagnostics);
        var special = Assert.IsType<BcsSpecialDeclaration>(Assert.Single(unit.Members));
        Assert.Equal(new[] { "Foo", "Bar" }, special.Names.Select(s => s.Name)); // original casing preserved - see BcsTokenizer.RawValue's own remarks
    }

    [Fact]
    public void Parse_LocalVariableInsideScriptBody_IsCollectedAsABodyLocal()
    {
        // The whole point of this feature: most of a real file's declared names live inside bodies, which this parser used to skip 100% opaquely.
        var (unit, diagnostics) = BcsParser.Parse("script 1 open\n{\n    int x;\n}\n");

        Assert.Empty(diagnostics);
        var script = Assert.IsType<BcsScriptDeclaration>(Assert.Single(unit.Members));
        Assert.Contains(script.BodyLocals, s => s.Name == "x" && s.Kind == BcsSymbolKind.Variable);
    }

    [Fact]
    public void Parse_MultipleCommaSeparatedLocals_CollectsAllNames()
    {
        var (unit, diagnostics) = BcsParser.Parse("script 1 open\n{\n    int a, b, c;\n}\n");

        Assert.Empty(diagnostics);
        var script = Assert.IsType<BcsScriptDeclaration>(Assert.Single(unit.Members));
        Assert.Equal(
            new[] { ("a", BcsSymbolKind.Variable), ("b", BcsSymbolKind.Variable), ("c", BcsSymbolKind.Variable) },
            script.BodyLocals.Select(s => (s.Name, s.Kind)));
    }

    [Fact]
    public void Parse_IndexedGlobalDeclaratorList_CollectsAllNames()
    {
        // The real indexed form - confirmed from dec.c's read_instance_list/read_storage_index.
        var (unit, diagnostics) = BcsParser.Parse("global int 0:a, 1:b;\n");

        Assert.Empty(diagnostics);
        var variable = Assert.IsType<BcsVariableDeclaration>(Assert.Single(unit.Members));
        Assert.Equal(new[] { "a", "b" }, variable.DeclaratorNames.Select(s => s.Name));
    }

    [Fact]
    public void Parse_FunctionCallInsideADeclarationInitializer_DoesNotTreatCallArgumentsAsDeclared()
    {
        // The depth-equality guard's whole reason to exist: Foo(a, b) is a call, not a declaration - "a"/"b" must not be collected just because they followed a comma at some depth.
        var (unit, diagnostics) = BcsParser.Parse("script 1 open\n{\n    int x = Foo(a, b), y;\n}\n");

        Assert.Empty(diagnostics);
        var script = Assert.IsType<BcsScriptDeclaration>(Assert.Single(unit.Members));
        Assert.Equal(
            new[] { ("x", BcsSymbolKind.Variable), ("y", BcsSymbolKind.Variable) },
            script.BodyLocals.Select(s => (s.Name, s.Kind)));
    }

    [Fact]
    public void Parse_EnumWithMembers_CollectsMemberNames()
    {
        var (unit, diagnostics) = BcsParser.Parse("enum Colors { Red, Green, Blue };\n");

        Assert.Empty(diagnostics);
        var decl = Assert.IsType<BcsEnumDeclaration>(Assert.Single(unit.Members));
        Assert.Equal(new[] { "Red", "Green", "Blue" }, decl.MemberNames.Select(s => s.Name)); // original casing preserved - see BcsTokenizer.RawValue's own remarks
    }

    [Fact]
    public void Parse_DefineDirective_ExtractsTheMacroName()
    {
        // Only the name is extracted - the value/parameter list still isn't modeled at all, no macro-expansion support.
        var (unit, diagnostics) = BcsParser.Parse("#define MAX_HEALTH 100\n");

        Assert.Empty(diagnostics);
        var define = Assert.IsType<BcsDefineDirective>(Assert.Single(unit.Members));
        Assert.Equal("MAX_HEALTH", define.Name); // original casing preserved - see BcsTokenizer.RawValue's own remarks
    }

    [Fact]
    public void CollectSymbols_IncludesDefinedMacroNames()
    {
        var (unit, diagnostics) = BcsParser.Parse("#define MAX_HEALTH 100\n");

        Assert.Empty(diagnostics);
        Assert.Contains(unit.CollectSymbols(), s => s.Name == "MAX_HEALTH" && s.Kind == BcsSymbolKind.Macro);
    }

    [Fact]
    public void CollectSymbols_SmallMultiConstructSource_ReturnsFlattenedDeduplicatedSet()
    {
        var (unit, diagnostics) = BcsParser.Parse(
            "function int add(int a, int b)\n{\n    int sum;\n}\n" +
            "script 1 open\n{\n    int x;\n}\n" +
            "enum Colors { Red, Green };\n" +
            "global int 0:score;\n");

        Assert.Empty(diagnostics);
        // Position is now part of BcsSymbol equality - compare (Name, Kind) only, since this test is about which names/kinds were collected, not where.
        var symbols = unit.CollectSymbols().Select(s => (s.Name, s.Kind)).ToHashSet();

        Assert.Equal(
            new HashSet<(string Name, BcsSymbolKind Kind)>
            {
                ("add", BcsSymbolKind.Function),
                ("a", BcsSymbolKind.Variable),
                ("b", BcsSymbolKind.Variable),
                ("sum", BcsSymbolKind.Variable),
                ("x", BcsSymbolKind.Variable),
                ("Colors", BcsSymbolKind.EnumType), // original casing preserved - see BcsTokenizer.RawValue's own remarks
                ("Red", BcsSymbolKind.EnumMember),
                ("Green", BcsSymbolKind.EnumMember),
                ("score", BcsSymbolKind.Variable),
            },
            symbols);
    }

    [Fact]
    public void Parse_LocalVariableInsideScriptBody_RecordsItsOwnDeclarationPosition()
    {
        var (unit, diagnostics) = BcsParser.Parse("script 1 open\n{\n    int x;\n}\n");

        Assert.Empty(diagnostics);
        var script = Assert.IsType<BcsScriptDeclaration>(Assert.Single(unit.Members));
        var local = Assert.Single(script.BodyLocals);
        Assert.Equal(3, local.Line); // "int x;" is the third line (1-based)
        Assert.Equal(9, local.Column); // the column "x" itself starts at
    }

    [Fact]
    public void Parse_FunctionWithMultiLineBody_RecordsBodyStartAndEndLines()
    {
        var (unit, diagnostics) = BcsParser.Parse("function int Add(int a, int b)\n{\n    int sum;\n    return sum;\n}\n");

        Assert.Empty(diagnostics);
        var function = Assert.IsType<BcsFunctionDeclaration>(Assert.Single(unit.Members));
        Assert.Equal(2, function.BodyLine); // the opening '{'
        Assert.Equal(5, function.BodyEndLine); // the closing '}'
    }

    [Fact]
    public void Parse_ScriptWithMultiLineBody_RecordsBodyStartAndEndLines()
    {
        var (unit, diagnostics) = BcsParser.Parse("script 1 open\n{\n    int x;\n    int y;\n}\n");

        Assert.Empty(diagnostics);
        var script = Assert.IsType<BcsScriptDeclaration>(Assert.Single(unit.Members));
        Assert.Equal(2, script.BodyLine);
        Assert.Equal(5, script.BodyEndLine);
    }

    [Fact]
    public void CollectSymbolsVisibleAt_LocalInOneScriptIsNotVisibleFromAnotherScript()
    {
        var (unit, diagnostics) = BcsParser.Parse(
            "script 1 open\n{\n    int onlyInScriptOne;\n}\n" + // lines 1-4
            "script 2 open\n{\n    int onlyInScriptTwo;\n}\n"); // lines 5-8

        Assert.Empty(diagnostics);

        var visibleInScriptOne = unit.CollectSymbolsVisibleAt(3).Select(s => s.Name).ToList();
        var visibleInScriptTwo = unit.CollectSymbolsVisibleAt(7).Select(s => s.Name).ToList();

        Assert.Contains("onlyInScriptOne", visibleInScriptOne);
        Assert.DoesNotContain("onlyInScriptTwo", visibleInScriptOne);

        Assert.Contains("onlyInScriptTwo", visibleInScriptTwo);
        Assert.DoesNotContain("onlyInScriptOne", visibleInScriptTwo);
    }

    [Fact]
    public void CollectSymbolsVisibleAt_FileScopeNamesAreVisibleFromInsideAnyBody()
    {
        var (unit, diagnostics) = BcsParser.Parse(
            "global int 0:sharedGlobal;\n" +
            "script 1 open\n{\n    int local;\n}\n");

        Assert.Empty(diagnostics);

        var visible = unit.CollectSymbolsVisibleAt(3).Select(s => s.Name).ToList(); // the "int local;" line
        Assert.Contains("sharedGlobal", visible);
        Assert.Contains("local", visible);

        var visibleOutsideAnyBody = unit.CollectSymbolsVisibleAt(1).Select(s => s.Name).ToList(); // the global declaration's own line
        Assert.Contains("sharedGlobal", visibleOutsideAnyBody);
        Assert.DoesNotContain("local", visibleOutsideAnyBody);
    }

    [Fact]
    public void FindDeclaration_LocalShadowingAGlobal_ResolvesToTheLocalFromInsideItsOwnScope()
    {
        var (unit, diagnostics) = BcsParser.Parse(
            "global int 0:value;\n" + // line 1
            "script 1 open\n{\n    int value;\n}\n"); // "int value;" is line 4

        Assert.Empty(diagnostics);

        var fromInsideScript = unit.FindDeclaration("value", 4);
        Assert.NotNull(fromInsideScript);
        Assert.Equal(4, fromInsideScript!.Value.Line); // the local, not the global on line 1

        var fromOutsideAnyBody = unit.FindDeclaration("value", 1);
        Assert.NotNull(fromOutsideAnyBody);
        Assert.Equal(1, fromOutsideAnyBody!.Value.Line); // the global - no enclosing body at line 1
    }

    [Fact]
    public void FindDeclaration_MatchesCaseInsensitively()
    {
        // BCS itself is case-insensitive - a use-site spelled differently from the declaration must still resolve.
        var (unit, diagnostics) = BcsParser.Parse("global int 0:MyVar;\n");

        Assert.Empty(diagnostics);
        Assert.NotNull(unit.FindDeclaration("myvar", 1));
    }

    [Fact]
    public void FindDeclaration_UnknownName_ReturnsNull()
    {
        var (unit, diagnostics) = BcsParser.Parse("global int 0:value;\n");

        Assert.Empty(diagnostics);
        Assert.Null(unit.FindDeclaration("doesNotExist", 1));
    }

    [Theory]
    [InlineData(BcsSymbolKind.Function, "add", "function add")]
    [InlineData(BcsSymbolKind.Variable, "x", "variable x")]
    [InlineData(BcsSymbolKind.EnumType, "Colors", "enum Colors")]
    [InlineData(BcsSymbolKind.EnumMember, "Red", "enum member Red")]
    [InlineData(BcsSymbolKind.Macro, "MAX_HEALTH", "macro MAX_HEALTH")]
    public void BcsSymbol_Describe_ProducesTheExpectedHoverText(BcsSymbolKind kind, string name, string expected)
    {
        Assert.Equal(expected, new BcsSymbol(name, kind, 1, 1).Describe());
    }

    [Fact]
    public void Parse_HashLibraryWithNoName_IsValidNotADiagnostic()
    {
        // Confirmed from zt-bcc's own library.c (read_library): the name is optional - a bare #library just uses the default name.
        var (unit, diagnostics) = BcsParser.Parse("#library\n");

        Assert.Empty(diagnostics);
        var library = Assert.IsType<BcsLibraryDirective>(Assert.Single(unit.Members));
        Assert.Equal(string.Empty, library.Name);
    }

    [Fact]
    public void Parse_HashLibraryWithName_ExtractsTheName()
    {
        var (unit, diagnostics) = BcsParser.Parse("#library \"mylib\"\n");

        Assert.Empty(diagnostics);
        var library = Assert.IsType<BcsLibraryDirective>(Assert.Single(unit.Members));
        Assert.Equal("mylib", library.Name);
    }

    [Fact]
    public void Parse_HashLibDefine_SharesDefinesGrammarAndExtractsTheMacroName()
    {
        // Confirmed from library.c: #libdefine goes through the exact same read_define as #define.
        var (unit, diagnostics) = BcsParser.Parse("#libdefine MAX_HEALTH 100\n");

        Assert.Empty(diagnostics);
        var define = Assert.IsType<BcsDefineDirective>(Assert.Single(unit.Members));
        Assert.Equal("MAX_HEALTH", define.Name);
        Assert.Contains(unit.CollectSymbols(), s => s.Name == "MAX_HEALTH" && s.Kind == BcsSymbolKind.Macro);
    }

    [Theory]
    [InlineData("#encryptstrings\n")]
    [InlineData("#nocompact\n")]
    [InlineData("#wadauthor\n")]
    [InlineData("#nowadauthor\n")]
    public void Parse_NoArgumentPragmaDirectives_AreRecognizedNotUnknown(string source)
    {
        // Confirmed from library.c: none of these take any argument at all - the directive word itself is the whole thing.
        var (unit, diagnostics) = BcsParser.Parse(source);

        Assert.Empty(diagnostics);
        Assert.Empty(unit.Members); // tolerated, not modeled as a node - same posture as #region/#endregion
    }

    [Fact]
    public void Parse_HashLinkLibraryWithName_IsRecognizedNotUnknown()
    {
        var (unit, diagnostics) = BcsParser.Parse("#linklibrary \"other\"\n");

        Assert.Empty(diagnostics);
        Assert.Empty(unit.Members);
    }

    [Fact]
    public void Parse_HashLinkLibraryMissingName_ReportsADiagnostic()
    {
        // Unlike #library, #linklibrary's string argument is required (confirmed from library.c's read_linklibrary).
        var (_, diagnostics) = BcsParser.Parse("#linklibrary\n");

        Assert.NotEmpty(diagnostics);
    }

    [Theory]
    [InlineData("library \"mylib\";\n")]
    [InlineData("wadauthor;\n")]
    [InlineData("nowadauthor;\n")]
    [InlineData("nocompact;\n")]
    [InlineData("encryptstrings;\n")]
    public void Parse_BarePragmaKeyword_IsNowAnUnexpectedTokenNotASilentlyAcceptedDirective(string source)
    {
        // Confirmed from library.c's read_module_item: these are ALWAYS #-prefixed in the real grammar - no bare form exists at module scope.
        var (_, diagnostics) = BcsParser.Parse(source);

        Assert.Contains(diagnostics, d => d.Message.Contains("unexpected token"));
    }

    [Fact]
    public void Parse_BareStrictKeyword_IsStillToleratedNotADiagnostic()
    {
        // Unlike the pragma family above, bare 'strict' is real grammar - a namespace qualifier, not a pragma - confirmed from library.c's is_namespace.
        var (unit, diagnostics) = BcsParser.Parse("strict;\n");

        Assert.Empty(diagnostics);
        Assert.Empty(unit.Members);
    }

    [Fact]
    public void Parse_FunctionParameter_RecordsItsOwnType()
    {
        var (unit, diagnostics) = BcsParser.Parse("function int Add(int a, str b) { }");

        Assert.Empty(diagnostics);
        var function = Assert.IsType<BcsFunctionDeclaration>(Assert.Single(unit.Members));
        Assert.Equal(new[] { ("a", "int"), ("b", "str") }, function.ParameterNames.Select(p => (p.Name, p.Type)));
    }

    [Fact]
    public void FindDeclaration_Function_DescribesItsFullSignature()
    {
        var (unit, diagnostics) = BcsParser.Parse("function int Add(int a, int b) { }");

        Assert.Empty(diagnostics);
        var declaration = unit.FindDeclaration("Add", 1);
        Assert.NotNull(declaration);
        Assert.Equal("function int Add(int a, int b)", declaration!.Value.Describe());
    }

    [Fact]
    public void FindDeclaration_FunctionParameterUse_DescribesItsOwnType()
    {
        var (unit, diagnostics) = BcsParser.Parse("function int Add(int a, int b)\n{\n    return a + b;\n}\n");

        Assert.Empty(diagnostics);
        Assert.Equal("int a", unit.FindDeclaration("a", 3)!.Value.Describe());
    }

    [Fact]
    public void FindDeclaration_GlobalVariable_DescribesItsOwnType()
    {
        var (unit, diagnostics) = BcsParser.Parse("global int 0:score;\n");

        Assert.Empty(diagnostics);
        Assert.Equal("int score", unit.FindDeclaration("score", 1)!.Value.Describe());
    }

    [Fact]
    public void Parse_CommaSeparatedDeclarators_AllShareTheSameLeadingType()
    {
        // Confirmed real grammar: a plain declarator list shares one type across commas, unlike parameters (which restate it every time).
        var (unit, diagnostics) = BcsParser.Parse("int a, b, c;\n");

        Assert.Empty(diagnostics);
        var variable = Assert.IsType<BcsVariableDeclaration>(Assert.Single(unit.Members));
        Assert.Equal(new[] { "int", "int", "int" }, variable.DeclaratorNames.Select(d => d.Type));
    }

    [Fact]
    public void FindDeclaration_NamedScriptWithParameters_DescribesItsSignature()
    {
        var (unit, diagnostics) = BcsParser.Parse("script \"main\" (int a, int b) open\n{\n}\n");

        Assert.Empty(diagnostics);
        var declaration = unit.FindDeclaration("main", 1);
        Assert.NotNull(declaration);
        Assert.Equal("script main(int a, int b)", declaration!.Value.Describe());
    }

    [Fact]
    public void Parse_SingleLineCommentDirectlyAboveFunction_IsCapturedAsDocComment()
    {
        var (unit, diagnostics) = BcsParser.Parse("// Adds two numbers.\nfunction int Add(int a, int b) { }");

        Assert.Empty(diagnostics);
        var function = Assert.IsType<BcsFunctionDeclaration>(Assert.Single(unit.Members));
        Assert.Equal("Adds two numbers.", function.DocComment);
    }

    [Fact]
    public void Parse_MultipleConsecutiveLineCommentsNoBlankLine_AreJoinedAsDocComment()
    {
        var (unit, diagnostics) = BcsParser.Parse("// Line one.\n// Line two.\nfunction int Add() { }");

        Assert.Empty(diagnostics);
        var function = Assert.IsType<BcsFunctionDeclaration>(Assert.Single(unit.Members));
        Assert.Equal("Line one.\nLine two.", function.DocComment);
    }

    [Fact]
    public void Parse_LineCommentsWithBlankLineBetweenThem_OnlyKeepsTheRunContiguousWithTheDeclaration()
    {
        var (unit, diagnostics) = BcsParser.Parse("// Unrelated, separated by a blank line.\n\n// Directly above.\nfunction int Add() { }");

        Assert.Empty(diagnostics);
        var function = Assert.IsType<BcsFunctionDeclaration>(Assert.Single(unit.Members));
        Assert.Equal("Directly above.", function.DocComment);
    }

    [Fact]
    public void Parse_BlockCommentDirectlyAboveFunction_IsCapturedAsDocComment()
    {
        var (unit, diagnostics) = BcsParser.Parse("/* Adds two numbers. */\nfunction int Add() { }");

        Assert.Empty(diagnostics);
        var function = Assert.IsType<BcsFunctionDeclaration>(Assert.Single(unit.Members));
        Assert.Equal("Adds two numbers.", function.DocComment);
    }

    [Fact]
    public void Parse_BlankLineBetweenCommentAndDeclaration_DocCommentIsEmpty()
    {
        var (unit, diagnostics) = BcsParser.Parse("// Not directly above - a blank line separates it.\n\nfunction int Add() { }");

        Assert.Empty(diagnostics);
        var function = Assert.IsType<BcsFunctionDeclaration>(Assert.Single(unit.Members));
        Assert.Equal(string.Empty, function.DocComment);
    }

    [Fact]
    public void Parse_NoCommentAboveDeclaration_DocCommentIsEmpty()
    {
        var (unit, diagnostics) = BcsParser.Parse("function int Add() { }");

        Assert.Empty(diagnostics);
        var function = Assert.IsType<BcsFunctionDeclaration>(Assert.Single(unit.Members));
        Assert.Equal(string.Empty, function.DocComment);
    }

    [Fact]
    public void Parse_DocCommentOnVariableDeclaration_IsSharedAcrossAllDeclarators()
    {
        var (unit, diagnostics) = BcsParser.Parse("// Two related globals.\nint a, b;\n");

        Assert.Empty(diagnostics);
        var symbols = unit.CollectSymbols();
        Assert.Equal("Two related globals.", symbols.Single(s => s.Name == "a").DocComment);
        Assert.Equal("Two related globals.", symbols.Single(s => s.Name == "b").DocComment);
    }

    [Fact]
    public void FindDeclaration_FunctionDocComment_IsSeparateFromItsSignature()
    {
        // DocComment and Describe() stay independent - callers combine them, Describe() never folds prose into the re-tokenized/colorized signature text.
        var (unit, diagnostics) = BcsParser.Parse("// Adds two numbers.\nfunction int Add(int a, int b) { }");

        Assert.Empty(diagnostics);
        var declaration = unit.FindDeclaration("Add", 1);
        Assert.NotNull(declaration);
        Assert.Equal("Adds two numbers.", declaration!.Value.DocComment);
        Assert.Equal("function int Add(int a, int b)", declaration!.Value.Describe());
    }
}
