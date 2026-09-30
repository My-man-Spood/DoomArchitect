using System.Text;
using DoomArchitect.Core.ZDoom;

namespace DoomArchitect.Core.Tests.ZDoom;

public class ZScriptTokenizerTests
{
    private static ZScriptTokenizer Create(string source) =>
        new(new BinaryReader(new MemoryStream(Encoding.UTF8.GetBytes(source))));

    private static List<ZScriptToken> ReadAll(ZScriptTokenizer tokenizer)
    {
        var tokens = new List<ZScriptToken>();
        while (true)
        {
            var tok = tokenizer.ReadToken();
            if (tok == null) break;
            tokens.Add(tok);
        }
        return tokens;
    }

    [Fact]
    public void ReadToken_TokenizesAClassDeclaration()
    {
        var tokenizer = Create("class Foo : Actor\n{\n}\n");
        var tokens = ReadAll(tokenizer).Where(t => t.Type != ZScriptTokenType.Whitespace).ToList();

        Assert.Equal(ZScriptTokenType.Identifier, tokens[0].Type);
        Assert.Equal("class", tokens[0].Value);
        Assert.Equal(ZScriptTokenType.Identifier, tokens[1].Type);
        Assert.Equal("Foo", tokens[1].Value);
        Assert.Equal(ZScriptTokenType.Colon, tokens[2].Type);
        Assert.Equal(ZScriptTokenType.Identifier, tokens[3].Type);
        Assert.Equal("Actor", tokens[3].Value);
        Assert.Equal(ZScriptTokenType.Newline, tokens[4].Type);
        Assert.Equal(ZScriptTokenType.OpenCurly, tokens[5].Type);
        Assert.Equal(ZScriptTokenType.Newline, tokens[6].Type);
        Assert.Equal(ZScriptTokenType.CloseCurly, tokens[7].Type);
    }

    [Theory]
    [InlineData("123", 123)]
    [InlineData("0x1F", 31)]
    [InlineData("010", 8)] // leading zero -> octal
    public void ReadToken_ParsesIntegerLiteralsInEveryBase(string source, int expected)
    {
        var tok = Create(source).ReadToken();

        Assert.NotNull(tok);
        Assert.Equal(ZScriptTokenType.Integer, tok!.Type);
        Assert.Equal(expected, tok.ValueInt);
    }

    [Theory]
    [InlineData("1.5", 1.5)]
    [InlineData("1e-2", 0.01)]
    public void ReadToken_ParsesDoubleLiterals(string source, double expected)
    {
        var tok = Create(source).ReadToken();

        Assert.NotNull(tok);
        Assert.Equal(ZScriptTokenType.Double, tok!.Type);
        Assert.Equal(expected, tok.ValueDouble, 3);
    }

    [Fact]
    public void ReadToken_LeadingDotDouble_LosesTheDigitAfterTheDot()
    {
        // A genuine UDB quirk, faithfully ported rather than "fixed": the
        // lookahead that confirms a digit follows the dot (distinguishing
        // "1.5" from "x.field") consumes that digit without appending it,
        // so ".5" tokenizes as Double "." (value 0), not 0.5. Real ZScript
        // content always writes a leading digit ("0.5"), so this doesn't
        // matter in practice - this test just pins the real behavior.
        var tok = Create(".5").ReadToken();

        Assert.NotNull(tok);
        Assert.Equal(ZScriptTokenType.Double, tok!.Type);
        Assert.Equal(0, tok.ValueDouble);
    }

    [Fact]
    public void ReadToken_StringLiteral_TreatsEscapedCharacterVerbatimRatherThanInterpretingIt()
    {
        // Matches UDB's own known-incomplete escape handling: a backslash just
        // includes the next raw character instead of resolving \n/\t/\" etc.
        var tok = Create("\"a\\\"b\"").ReadToken();

        Assert.NotNull(tok);
        Assert.Equal(ZScriptTokenType.String, tok!.Type);
        Assert.Equal("a\"b", tok.Value);
    }

    [Fact]
    public void ReadToken_NameLiteral_UsesSingleQuotes()
    {
        var tok = Create("'Spawn'").ReadToken();

        Assert.NotNull(tok);
        Assert.Equal(ZScriptTokenType.Name, tok!.Type);
        Assert.Equal("Spawn", tok.Value);
    }

    [Fact]
    public void SkipWhitespace_SkipsSpacesNewlinesAndComments()
    {
        var tokenizer = Create("   \n// line comment\n/* block\ncomment */\nclass");
        tokenizer.SkipWhitespace();
        var tok = tokenizer.ReadToken();

        Assert.NotNull(tok);
        Assert.Equal(ZScriptTokenType.Identifier, tok!.Type);
        Assert.Equal("class", tok.Value);
    }

    [Fact]
    public void SkipWhitespace_SkipsRegionMarkers()
    {
        var tokenizer = Create("#region Monsters\nclass");
        tokenizer.SkipWhitespace();
        var tok = tokenizer.ReadToken();

        Assert.NotNull(tok);
        Assert.Equal("class", tok!.Value);
    }

    [Fact]
    public void ReadToken_BlockComment_DoesNotNest()
    {
        // UDB's own known behavior: the first "*/" closes the comment, even
        // if a "/*" appeared inside it.
        var tokenizer = Create("/* outer /* inner */ still_here");
        var tokens = ReadAll(tokenizer);

        Assert.Contains(tokens, t => t.Type == ZScriptTokenType.Identifier && t.Value == "still_here");
    }

    [Fact]
    public void TokensToString_ReconstructsOriginalText()
    {
        const string source = "class Foo : Actor { }";
        var tokens = ReadAll(Create(source));

        Assert.Equal(source, ZScriptTokenizer.TokensToString(tokens));
    }

    [Fact]
    public void PositionToLine_CountsNewlinesBeforeThePosition()
    {
        // Line boundaries are recorded as the stream position immediately
        // after each '\n', so a position is "on" the first line whose
        // recorded boundary it falls at-or-before.
        var tokenizer = Create("aaa\nbbb\nccc");

        Assert.Equal(1, tokenizer.PositionToLine(1)); // inside "aaa"
        Assert.Equal(2, tokenizer.PositionToLine(5)); // inside "bbb"
    }

    [Fact]
    public void ExpectToken_ReturnsInvalidTokenWhenExpectationIsNotMet()
    {
        var tokenizer = Create("123");

        var tok = tokenizer.ExpectToken(ZScriptTokenType.Identifier);

        Assert.NotNull(tok);
        Assert.False(tok!.IsValid);
    }
}
