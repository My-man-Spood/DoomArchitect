using DoomArchitect.Core.IO;

namespace DoomArchitect.Core.Tests.IO;

public class UdmfTreeParserTests
{
    private static UdmfBlock Parse(string text) => UdmfTreeParser.Parse(text, new List<string>());

    [Fact]
    public void Parse_SimpleAssignment_ReadsKeyAndValue()
    {
        var root = Parse("x = 5;");

        Assert.Equal(5L, root.Find("x")!.Value.AsLong());
    }

    [Fact]
    public void Parse_KeyIsCaseInsensitive_LoweredOnStorage()
    {
        var root = Parse("MyField = 1;");

        Assert.Equal(1L, root.Find("myfield")!.Value.AsLong());
    }

    [Fact]
    public void Parse_Block_NestsAssignmentsUnderThatBlock()
    {
        var root = Parse("vertex { x = 1.0; y = 2.0; }");

        var vertex = Assert.Single(root.Blocks);
        Assert.Equal("vertex", vertex.Name);
        Assert.Equal(1.0, vertex.Find("x")!.Value.AsDouble());
        Assert.Equal(2.0, vertex.Find("y")!.Value.AsDouble());
    }

    [Fact]
    public void Parse_LineComment_IsSkipped()
    {
        var root = Parse("x = 5; // trailing comment\ny = 6;");

        Assert.Equal(5L, root.Find("x")!.Value.AsLong());
        Assert.Equal(6L, root.Find("y")!.Value.AsLong());
    }

    [Fact]
    public void Parse_BlockComment_SpanningLines_IsSkipped()
    {
        var root = Parse("x = 5; /* this\nis a\ncomment */ y = 6;");

        Assert.Equal(5L, root.Find("x")!.Value.AsLong());
        Assert.Equal(6L, root.Find("y")!.Value.AsLong());
    }

    [Fact]
    public void Parse_HexInteger_ParsesAsInt()
    {
        var root = Parse("x = 0x1F;");

        Assert.Equal(31L, root.Find("x")!.Value.AsLong());
    }

    [Fact]
    public void Parse_IntegerOverflowingInt32_EscalatesToLong()
    {
        var root = Parse("x = 5000000000;");

        Assert.Equal(5000000000L, root.Find("x")!.Value.AsLong());
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("1E-06")]
    public void Parse_DecimalPointOrNegativeExponent_ParsesAsDouble(string literal)
    {
        var root = Parse($"x = {literal};");

        Assert.Equal(UdmfValueKind.Double, root.Find("x")!.Value.Kind);
    }

    [Theory]
    [InlineData("1E+06")]
    [InlineData("1E06")]
    public void Parse_PositiveOrMissingExponentSign_DoesNotParseAsDouble(string literal)
    {
        // Matches UDB's own (not general scientific-notation) heuristic:
        // only a literal '.' or the substring "e-" selects the double
        // path. Neither of these forms contains either, so they fall
        // through to plain integer parsing and fail.
        Assert.Throws<UdmfParseException>(() => Parse($"x = {literal};"));
    }

    [Fact]
    public void Parse_NanKeyword_IsDroppedWithWarning()
    {
        var warnings = new List<string>();

        var root = UdmfTreeParser.Parse("x = nan;", warnings);

        Assert.Null(root.Find("x"));
        Assert.Single(warnings);
        Assert.Contains("NaN", warnings[0]);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    public void Parse_BooleanKeyword_ParsesCaseInsensitively(string literal, bool expected)
    {
        var root = Parse($"x = {literal};");

        Assert.Equal(expected, root.Find("x")!.Value.AsBool());
    }

    [Fact]
    public void Parse_UnrecognizedKeyword_Throws()
    {
        Assert.Throws<UdmfParseException>(() => Parse("x = bogus;"));
    }

    [Fact]
    public void Parse_StringEscapes_AreDecoded()
    {
        var root = Parse(@"x = ""a\\b\nc\td\r\""e"";");

        Assert.Equal("a\\b\nc\td\r\"e", root.Find("x")!.Value.AsString());
    }

    [Fact]
    public void Parse_NumericStringEscape_DecodesAllThreeDigitsAndAdvancesPastThem()
    {
        // \065 -> 'A'. If the cursor didn't advance past all 3 digits,
        // "65" would leak into the string as literal text afterward.
        var root = Parse(@"x = ""\065ok"";");

        Assert.Equal("Aok", root.Find("x")!.Value.AsString());
    }

    [Fact]
    public void Find_DuplicateKey_LastAssignmentWins()
    {
        var root = Parse("x = 1; x = 2;");

        Assert.Equal(2L, root.Find("x")!.Value.AsLong());
    }

    [Fact]
    public void Parse_UnterminatedString_ThrowsWithLineNumber()
    {
        var ex = Assert.Throws<UdmfParseException>(() => Parse("x = \"unterminated"));

        Assert.Equal(1, ex.Line);
    }

    [Fact]
    public void Parse_MissingSemicolon_Throws()
    {
        Assert.Throws<UdmfParseException>(() => Parse("x = 5"));
    }

    [Fact]
    public void Parse_MismatchedClosingBrace_Throws()
    {
        Assert.Throws<UdmfParseException>(() => Parse("}"));
    }

    [Fact]
    public void Parse_UnclosedBlock_Throws()
    {
        Assert.Throws<UdmfParseException>(() => Parse("vertex { x = 1;"));
    }

    [Fact]
    public void Parse_ErrorLineNumber_CountsNewlinesSeen()
    {
        var ex = Assert.Throws<UdmfParseException>(() => Parse("x = 1;\ny = bogus;"));

        Assert.Equal(2, ex.Line);
    }
}
