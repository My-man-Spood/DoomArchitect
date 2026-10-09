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

    /// <summary>
    /// Real, reported bug: a special entry's own required/optional
    /// params are separated by a ';' *inside* that entry's own parens
    /// (confirmed real - the exact same convention g_funcs[]'s own
    /// format strings use, e.g. zcommon.bcs's own real
    /// "Door_Close(int,int;int):int,"). Before this was fixed, the
    /// whole statement's own loop treated that inner ';' as if it were
    /// the statement's real terminator, stopping after the very first
    /// such entry and leaving everything after it to be mis-parsed as
    /// top-level declarations - confirmed live against the real
    /// zcommon.bcs (1104+ diagnostics, cascading from deep inside its
    /// own special list, down to near zero once this was fixed).
    /// </summary>
    [Fact]
    public void Parse_SpecialEntryWithOptionalParamsSeparator_DoesNotStopAtItsOwnInnerSemicolon()
    {
        var (unit, diagnostics) = BcsParser.Parse("special 1:Foo(int,int;int):int, 2:Bar(int):int;\n");

        Assert.Empty(diagnostics);
        var special = Assert.IsType<BcsSpecialDeclaration>(Assert.Single(unit.Members));
        Assert.Equal(new[] { "Foo", "Bar" }, special.Names.Select(s => s.Name));
    }

    /// <summary>
    /// Real, reported bug: `special`-declared functions (e.g. the real
    /// `zcommon.bcs`'s own `Thing_Activate(int):int,`/`Exit_Normal(int):int,`)
    /// had no hover/completion parameter info at all - only their bare
    /// name was ever extracted. Confirmed the real decode against
    /// `zt-bcc`'s own `src/parse/dec.c` (`read_special_param`/
    /// `read_special_return_type`) and verified live against the
    /// user's own real `zcommon.bcs`: 491 of 492 real special-declared
    /// functions now get a real signature (the lone holdout,
    /// `__EndOfList__(10);` with no `:` return type at all, uses the
    /// numeric min/max-count shorthand form with no real types to show -
    /// expected to fall back to a bare name, not a bug).
    /// </summary>
    [Fact]
    public void Parse_SpecialEntryWithTypedRequiredAndOptionalParams_BuildsARealSignature()
    {
        var (unit, diagnostics) = BcsParser.Parse("special 11:Door_Close(int,int;int):int,\n130:Thing_Activate(int):int;\n");

        Assert.Empty(diagnostics);
        var special = Assert.IsType<BcsSpecialDeclaration>(Assert.Single(unit.Members));
        // Parameter names come from this project's own researched BcsFunctionDocs
        // data (see its own remarks) - the real grammar itself has no names at all.
        Assert.Equal("function int Door_Close(int tag, int speed, [int lighttag])", special.Names[0].Describe());
        Assert.Equal("function int Thing_Activate(int tid)", special.Names[1].Describe());
    }

    [Fact]
    public void Parse_SpecialEntryWithNoParamsAndVoidReturn_BuildsARealSignature()
    {
        var (unit, diagnostics) = BcsParser.Parse("special 1:Foo():void;\n");

        Assert.Empty(diagnostics);
        var special = Assert.IsType<BcsSpecialDeclaration>(Assert.Single(unit.Members));
        Assert.Equal("function void Foo()", special.Names[0].Describe());
    }

    /// <summary>Confirmed real default from `dec.c`'s own `read_special` (`int return_spec = SPEC_RAW;` before its optional `read_special_return_type` call) - omitting the `:returntype` entirely defaults to `raw`, not `void`.</summary>
    [Fact]
    public void Parse_SpecialEntryWithNoReturnTypeAnnotation_DefaultsToRaw()
    {
        var (unit, diagnostics) = BcsParser.Parse("special 1:Foo(int);\n");

        Assert.Empty(diagnostics);
        var special = Assert.IsType<BcsSpecialDeclaration>(Assert.Single(unit.Members));
        Assert.Equal("function raw Foo(int)", special.Names[0].Describe());
    }

    /// <summary>Confirmed real grammar from `dec.c`'s own `read_special` - a `FUNC_ASPEC` entry (one with no leading `-`) can carry an extra trailing `:decimal` script-callable flag after its own return type, before the next entry's comma.</summary>
    [Fact]
    public void Parse_SpecialEntryWithTrailingScriptCallableFlag_StillBuildsASignatureAndKeepsParsing()
    {
        var (unit, diagnostics) = BcsParser.Parse("special 1:Foo(int):int:1, 2:Bar(int):int;\n");

        Assert.Empty(diagnostics);
        var special = Assert.IsType<BcsSpecialDeclaration>(Assert.Single(unit.Members));
        Assert.Equal(new[] { "Foo", "Bar" }, special.Names.Select(s => s.Name));
        Assert.Equal("function int Foo(int)", special.Names[0].Describe());
        Assert.Equal("function int Bar(int)", special.Names[1].Describe());
    }

    /// <summary>Confirmed real grammar: a leading `-` marks a `FUNC_EXT` entry (e.g. the real `zcommon.bcs`'s own internal `ScriptCall`) rather than a normal, script-callable `FUNC_ASPEC` one - still a real function with a real signature.</summary>
    [Fact]
    public void Parse_SpecialEntryWithLeadingMinus_IsStillParsedAndSigned()
    {
        var (unit, diagnostics) = BcsParser.Parse("special -1:ScriptCall(int):int;\n");

        Assert.Empty(diagnostics);
        var special = Assert.IsType<BcsSpecialDeclaration>(Assert.Single(unit.Members));
        Assert.Equal("function int ScriptCall(int)", special.Names[0].Describe());
    }

    /// <summary>
    /// Confirmed real grammar: `(10)`/`(2,5)` is a numeric min/max
    /// parameter-count shorthand (an untyped, "raw"-style special) - a
    /// real, confirmed form (the real `zcommon.bcs`'s own
    /// `-100000:__EndOfList__(10);` sentinel entry uses exactly this),
    /// not a malformed entry. There's no real per-parameter type to
    /// show for this form, so it still falls back to a bare name rather
    /// than fabricating one - same as before this signature work existed.
    /// </summary>
    [Fact]
    public void Parse_SpecialEntryWithNumericParamCountShorthand_FallsBackToABareName()
    {
        var (unit, diagnostics) = BcsParser.Parse("special -100000:EndOfList(10);\n");

        Assert.Empty(diagnostics);
        var special = Assert.IsType<BcsSpecialDeclaration>(Assert.Single(unit.Members));
        Assert.Equal("function EndOfList", special.Names[0].Describe());
    }

    /// <summary>
    /// Real zcommon.bcs `special` lists essentially never have their own
    /// leading doc comment, which used to mean every one of its ~500
    /// functions showed no description at all on hover. `Thing_Activate`
    /// is a real, researched entry in <see cref="BcsFunctionDocs"/> (see
    /// wiki-research/README.md) - this confirms <see cref="BcsCompilationUnit.CollectSymbols"/>
    /// actually falls back to it once a real file is parsed end to end,
    /// not just that the lookup itself works.
    /// </summary>
    [Fact]
    public void CollectSymbols_SpecialEntryWithNoLeadingComment_FallsBackToResearchedDocComment()
    {
        var (unit, diagnostics) = BcsParser.Parse("special 130:Thing_Activate(int):int;\n");

        Assert.Empty(diagnostics);
        var symbol = Assert.Single(unit.CollectSymbols());
        Assert.NotEmpty(symbol.DocComment);
        Assert.Equal(BcsFunctionDocs.Format("Thing_Activate"), symbol.DocComment);
    }

    /// <summary>A real, explicit leading doc comment is the user's own and always wins - never silently replaced by a researched one, even for a name this project happens to have research for.</summary>
    [Fact]
    public void CollectSymbols_SpecialEntryWithExplicitLeadingComment_KeepsItOverResearchedDocComment()
    {
        var (unit, diagnostics) = BcsParser.Parse("// My own note.\nspecial 130:Thing_Activate(int):int;\n");

        Assert.Empty(diagnostics);
        var symbol = Assert.Single(unit.CollectSymbols());
        Assert.Equal("My own note.", symbol.DocComment);
    }

    /// <summary>
    /// `enum : basetype { ... }` - confirmed real grammar from `dec.c`'s
    /// own `read_enum_base_type` (e.g. the real `zcommon.bcs`'s own
    /// "enum : fixed { ATTN_NONE = 0.0, ... }", for fixed-point rather
    /// than plain-int enum values). Not modeled (this pass doesn't
    /// type-check members against it) - just consumed so a real file
    /// using one doesn't report "expected '{', got ':'" and desync the
    /// rest of the parse the way this did before.
    /// </summary>
    [Fact]
    public void Parse_AnonymousEnumWithBaseType_ParsesItsMembers()
    {
        var (unit, diagnostics) = BcsParser.Parse("enum : fixed {\nATTN_NONE = 0.0,\nATTN_NORM = 1.0\n};\n");

        Assert.Empty(diagnostics);
        var decl = Assert.IsType<BcsEnumDeclaration>(Assert.Single(unit.Members));
        Assert.Equal(new[] { "ATTN_NONE", "ATTN_NORM" }, decl.MemberNames.Select(s => s.Name));
    }

    [Fact]
    public void Parse_NamedEnumWithBaseType_RecordsItsNameAndMembers()
    {
        var (unit, diagnostics) = BcsParser.Parse("enum Attenuation : fixed { None, Normal };\n");

        Assert.Empty(diagnostics);
        var decl = Assert.IsType<BcsEnumDeclaration>(Assert.Single(unit.Members));
        Assert.Equal("Attenuation", decl.Name);
        Assert.Equal(new[] { "None", "Normal" }, decl.MemberNames.Select(s => s.Name));
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

    /// <summary>
    /// A bare 'strict' with nothing after it isn't real grammar at all -
    /// `is_namespace` treats any bare `strict` as unconditionally starting
    /// a namespace (there's no other real bare use for it), so malformed
    /// input like this reports a real diagnostic now rather than silently
    /// tolerating it the way an earlier, incomplete version of this pass
    /// did (confirmed wrong once namespaces were actually modeled - see
    /// `Parse_AnonymousStrictNamespace_ParsesItsMembers` for the real,
    /// valid shape this guards against being confused with).
    /// </summary>
    [Fact]
    public void Parse_BareStrictWithNoNamespaceFollowing_ReportsExpectedNamespace()
    {
        var (_, diagnostics) = BcsParser.Parse("strict;\n");

        Assert.Contains(diagnostics, d => d.Message.Contains("expected 'namespace'"));
    }

    [Fact]
    public void Parse_AnonymousStrictNamespace_ParsesItsMembers()
    {
        // zcommon.bcs's own real shape, confirmed live.
        var (unit, diagnostics) = BcsParser.Parse("strict namespace {\nfunction int Foo() { }\n}\n");

        Assert.Empty(diagnostics);
        var ns = Assert.IsType<BcsNamespaceDeclaration>(Assert.Single(unit.Members));
        Assert.Equal("strict", ns.Qualifiers);
        Assert.Equal(string.Empty, ns.Name);
        var function = Assert.IsType<BcsFunctionDeclaration>(Assert.Single(ns.Members));
        Assert.Equal("Foo", function.Name);
    }

    [Fact]
    public void Parse_NamedNamespace_RecordsItsName()
    {
        var (unit, diagnostics) = BcsParser.Parse("namespace Foo {\nint x;\n}\n");

        Assert.Empty(diagnostics);
        var ns = Assert.IsType<BcsNamespaceDeclaration>(Assert.Single(unit.Members));
        Assert.Equal("Foo", ns.Name);
    }

    [Fact]
    public void Parse_DottedNamespaceName_JoinsEverySegment()
    {
        var (unit, diagnostics) = BcsParser.Parse("namespace Foo.Bar {\n}\n");

        Assert.Empty(diagnostics);
        var ns = Assert.IsType<BcsNamespaceDeclaration>(Assert.Single(unit.Members));
        Assert.Equal("Foo.Bar", ns.Name);
    }

    [Fact]
    public void Parse_NestedNamespace_FlattensThroughCollectSymbolsVisibleAt()
    {
        var (unit, diagnostics) = BcsParser.Parse("namespace Foo {\nnamespace Bar {\nint x;\n}\n}\n");

        Assert.Empty(diagnostics);
        var outer = Assert.IsType<BcsNamespaceDeclaration>(Assert.Single(unit.Members));
        var inner = Assert.IsType<BcsNamespaceDeclaration>(Assert.Single(outer.Members));
        Assert.Equal("Bar", inner.Name);
        Assert.Contains(unit.CollectSymbolsVisibleAt(5), s => s.Name == "x");
    }

    [Theory]
    [InlineData("private namespace {\n}\n")]
    [InlineData("internal strict namespace {\n}\n")]
    public void Parse_QualifiedNamespace_ParsesWithoutDiagnostics(string source)
    {
        var (unit, diagnostics) = BcsParser.Parse(source);

        Assert.Empty(diagnostics);
        Assert.IsType<BcsNamespaceDeclaration>(Assert.Single(unit.Members));
    }

    [Fact]
    public void Parse_PrivateVariableAtTopLevel_IsNoLongerUnexpected()
    {
        // The adjacent gap this same lookahead closes for free - 'private'/
        // 'internal' aren't namespace-exclusive (confirmed from dec.c's
        // own read_dec), they can also qualify a plain declaration.
        var (unit, diagnostics) = BcsParser.Parse("private int x;\n");

        Assert.Empty(diagnostics);
        var variable = Assert.IsType<BcsVariableDeclaration>(Assert.Single(unit.Members));
        Assert.Equal("int", variable.TypeKeyword);
    }

    [Fact]
    public void Parse_PrivateFunction_IsNotMistakenForANamespaceStart()
    {
        // 'private' followed by 'function' (not 'strict'/'namespace') -
        // IsNamespaceStart's own real disambiguation must say no here.
        var (unit, diagnostics) = BcsParser.Parse("private function int F() { }\n");

        Assert.Empty(diagnostics);
        var function = Assert.IsType<BcsFunctionDeclaration>(Assert.Single(unit.Members));
        Assert.Equal("F", function.Name);
    }

    [Fact]
    public void Parse_NamespaceWithNoOpeningBrace_ReportsDiagnosticAndTerminates()
    {
        var (_, diagnostics) = BcsParser.Parse("namespace Foo\n");

        Assert.Contains(diagnostics, d => d.Message.Contains("expected '{'"));
    }

    /// <summary>Same "must finish" scrutiny as <see cref="Parse_StrayClosingBraceAtTopLevel_TerminatesInsteadOfLoopingForever"/> - an unterminated namespace body running to EOF must report and stop, not hang.</summary>
    [Fact]
    public async Task Parse_UnterminatedNamespaceBody_TerminatesInsteadOfLoopingForever()
    {
        var task = Task.Run(() => BcsParser.Parse("namespace Foo {\nint x;\n"));
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5))) == task;

        Assert.True(completed, "Parse() hung instead of terminating on an unterminated namespace body.");
        var (_, diagnostics) = await task;
        Assert.Contains(diagnostics, d => d.Message.Contains("unexpected end of file inside a namespace"));
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

    /// <summary>
    /// Real bug, found live (a real `strict namespace { ... }` file this
    /// parser doesn't model as its own block construct left one extra,
    /// genuinely stray '}' at the top level): <see cref="BcsParser.Recover"/>
    /// deliberately leaves a depth-0 '}' unconsumed so an *enclosing*
    /// <c>SkipBracedBlock</c> can see it - correct inside a block, but
    /// <see cref="BcsParser.Parse"/>'s own top-level loop has no
    /// enclosing block to hand it to, so nothing ever advanced past it
    /// and the loop spun forever on the exact same token. The `Timeout`
    /// here is the actual regression guard - this test should fail by
    /// timing out, not by a wrong assertion, if that ever comes back.
    /// </summary>
    [Fact]
    public async Task Parse_StrayClosingBraceAtTopLevel_TerminatesInsteadOfLoopingForever()
    {
        var task = Task.Run(() => BcsParser.Parse("int x;\n}\nint y;\n"));
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5))) == task;

        Assert.True(completed, "Parse() hung instead of terminating - the exact regression this test guards against.");
        var (_, diagnostics) = await task;
        Assert.Contains(diagnostics, d => d.Message.Contains("unexpected token '}'"));
    }
}
