using System.Text;
using DoomArchitect.Core.ZDoom;

namespace DoomArchitect.Core.Tests.ZDoom;

public class ZDTextParserTests
{
    // ZDTextParser is abstract purely to force a real consumer to subclass
    // it (matching DecorateParser/ZScriptParser in Phase 2) - this fixture
    // exposes its protected members for direct testing.
    private sealed class TestParser : ZDTextParser
    {
        public void Begin(string source) => Parse(new MemoryStream(Encoding.ASCII.GetBytes(source)), "test.txt");
        public new bool SkipWhitespace(bool skipNewline) => base.SkipWhitespace(skipNewline);
        public new string ReadToken() => base.ReadToken();
        public new string ReadToken(bool multiline) => base.ReadToken(multiline);
        public new string? ReadToken(string specialTokens) => base.ReadToken(specialTokens);
        public new string? ReadLine() => base.ReadLine();
        public new void SkipStructure() => base.SkipStructure();
        public new void ReportError(string message) => base.ReportError(message);
    }

    [Fact]
    public void ReadToken_ReadsWhitespaceSeparatedWords()
    {
        var parser = new TestParser();
        parser.Begin("hello world");

        Assert.Equal("hello", parser.ReadToken());
        parser.SkipWhitespace(true);
        Assert.Equal("world", parser.ReadToken());
    }

    [Fact]
    public void ReadToken_SpecialTokenIsItsOwnToken()
    {
        var parser = new TestParser();
        parser.Begin("foo{bar}");

        Assert.Equal("foo", parser.ReadToken());
        Assert.Equal("{", parser.ReadToken());
        Assert.Equal("bar", parser.ReadToken());
        Assert.Equal("}", parser.ReadToken());
    }

    [Fact]
    public void ReadToken_QuotedStringIsOneToken()
    {
        var parser = new TestParser();
        parser.Begin("\"hello world\" next");

        Assert.Equal("\"hello world\"", parser.ReadToken());
        parser.SkipWhitespace(true);
        Assert.Equal("next", parser.ReadToken());
    }

    [Fact]
    public void SkipWhitespace_SkipsLineAndBlockComments()
    {
        var parser = new TestParser();
        parser.Begin("// a line comment\n/* a\nblock comment */ token");

        parser.SkipWhitespace(true);
        Assert.Equal("token", parser.ReadToken());
    }

    [Fact]
    public void SkipWhitespace_SkipsRegionMarkers()
    {
        var parser = new TestParser();
        parser.Begin("#region Monsters\ntoken\n#endregion");

        parser.SkipWhitespace(true);
        Assert.Equal("token", parser.ReadToken());
    }

    [Fact]
    public void ReadToken_NotMultiline_StopsAtCarriageReturn()
    {
        var parser = new TestParser();
        parser.Begin("foo\rbar");

        Assert.Equal("foo", parser.ReadToken(false));
    }

    [Fact]
    public void StripQuotes_RemovesLeadingAndTrailingQuoteOnly()
    {
        Assert.Equal("hello", ZDTextParser.StripQuotes("\"hello\""));
        Assert.Equal("hello", ZDTextParser.StripQuotes("hello"));
    }

    [Fact]
    public void NextTokenIs_RewindsOnMismatch()
    {
        var parser = new TestParser();
        parser.Begin("actual");

        Assert.False(parser.NextTokenIs("expected"));
        Assert.Equal("actual", parser.ReadToken()); // rewound, readable again
    }

    [Fact]
    public void NextTokenIs_IsCaseInsensitive()
    {
        var parser = new TestParser();
        parser.Begin("ACTOR");

        Assert.True(parser.NextTokenIs("actor"));
    }

    [Fact]
    public void SkipStructure_SkipsNestedBraces()
    {
        var parser = new TestParser();
        parser.Begin("{ inner { nested } more } after");

        parser.SkipStructure();
        parser.SkipWhitespace(true);
        Assert.Equal("after", parser.ReadToken());
    }

    [Fact]
    public void ReadLine_ReadsUntilNewlineAndTrims()
    {
        var parser = new TestParser();
        parser.Begin("  first line  \nsecond");

        Assert.Equal("first line", parser.ReadLine());
        Assert.Equal("second", parser.ReadLine());
    }

    [Fact]
    public void ReportError_SetsHasErrorAndDescription()
    {
        var parser = new TestParser();
        parser.Begin("token");

        Assert.False(parser.HasError);
        parser.ReportError("something broke");

        Assert.True(parser.HasError);
        Assert.Equal("something broke", parser.ErrorDescription);
    }
}
