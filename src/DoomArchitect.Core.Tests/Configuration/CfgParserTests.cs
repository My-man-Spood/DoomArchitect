using DoomArchitect.Core.Configuration;

namespace DoomArchitect.Core.Tests.Configuration;

public class CfgParserTests
{
    [Fact]
    public void Parse_SimpleAssignments_ReadsEachValueKind()
    {
        var statements = CfgParser.Parse(
            "intkey = 5; doublekey = 1.5; floatkey = 2.5f; strkey = \"hello\"; boolkey = true;");

        var byKey = statements.Cast<CfgAssignStatement>().ToDictionary(s => s.Key, s => s.Value);

        Assert.Equal(5L, byKey["intkey"]!.Value.AsLong());
        Assert.Equal(1.5, byKey["doublekey"]!.Value.AsDouble());
        Assert.Equal(2.5, byKey["floatkey"]!.Value.AsDouble(), 3);
        Assert.Equal("hello", byKey["strkey"]!.Value.AsString());
        Assert.True(byKey["boolkey"]!.Value.AsBool());
    }

    [Fact]
    public void Parse_BareKeyOrExplicitNull_BothProduceANullValue()
    {
        var statements = CfgParser.Parse("somekey; otherkey = null;");

        var byKey = statements.Cast<CfgAssignStatement>().ToDictionary(s => s.Key, s => s.Value);

        Assert.Null(byKey["somekey"]);
        Assert.Null(byKey["otherkey"]);
    }

    [Fact]
    public void Parse_NestedBlockWithIntegerKey_IsAValidBlockKey()
    {
        var statements = CfgParser.Parse("monsters { 3004 { title = \"Zombieman\"; } }");

        var monsters = Assert.IsType<CfgBlockStatement>(Assert.Single(statements));
        Assert.Equal("monsters", monsters.Key);
        var entry = Assert.IsType<CfgBlockStatement>(Assert.Single(monsters.Body));
        Assert.Equal("3004", entry.Key);
        var title = Assert.IsType<CfgAssignStatement>(Assert.Single(entry.Body));
        Assert.Equal("title", title.Key);
        Assert.Equal("Zombieman", title.Value!.Value.AsString());
    }

    [Fact]
    public void Parse_LineAndBlockComments_AreIgnored()
    {
        var statements = CfgParser.Parse(
            "// a line comment\na = 1; /* a block\ncomment */ b = 2;");

        var byKey = statements.Cast<CfgAssignStatement>().ToDictionary(s => s.Key, s => s.Value!.Value.AsLong());
        Assert.Equal(1L, byKey["a"]);
        Assert.Equal(2L, byKey["b"]);
    }

    [Fact]
    public void Parse_IncludeWithOneArgument_ProducesAnIncludeStatementWithNoSubPath()
    {
        var statements = CfgParser.Parse("include(\"Includes/Foo.cfg\");");

        var include = Assert.IsType<CfgIncludeStatement>(Assert.Single(statements));
        Assert.Equal("Includes/Foo.cfg", include.Path);
        Assert.Null(include.SubPath);
    }

    [Fact]
    public void Parse_IncludeWithTwoArguments_CapturesTheSubPath()
    {
        var statements = CfgParser.Parse("include(\"Foo.cfg\", \"thingtypes.monsters\");");

        var include = Assert.IsType<CfgIncludeStatement>(Assert.Single(statements));
        Assert.Equal("Foo.cfg", include.Path);
        Assert.Equal("thingtypes.monsters", include.SubPath);
    }

    [Fact]
    public void Parse_UnknownFunctionName_Throws()
    {
        Assert.Throws<CfgParseException>(() => CfgParser.Parse("frobnicate(\"x\");"));
    }

    [Fact]
    public void Parse_MissingClosingBrace_Throws()
    {
        Assert.Throws<CfgParseException>(() => CfgParser.Parse("monsters { 1 { title = \"x\"; }"));
    }

    [Fact]
    public void Parse_UnexpectedClosingBraceAtRoot_Throws()
    {
        Assert.Throws<CfgParseException>(() => CfgParser.Parse("}"));
    }
}
