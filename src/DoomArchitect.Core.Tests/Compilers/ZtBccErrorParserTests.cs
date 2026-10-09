using DoomArchitect.Core.Compilers;

namespace DoomArchitect.Core.Tests.Compilers;

public class ZtBccErrorParserTests
{
    [Fact]
    public void Parse_SingleWellFormedLine_ReturnsOneError()
    {
        var errors = ZtBccErrorParser.Parse("/tmp/SCRIPTS.bcs:5:3: error: unknown identifier `foo`\n");

        Assert.Single(errors);
        Assert.Equal("/tmp/SCRIPTS.bcs", errors[0].FilePath);
        Assert.Equal(5, errors[0].Line);
        Assert.Equal("error: unknown identifier `foo`", errors[0].Message);
    }

    [Fact]
    public void Parse_MultipleWellFormedLines_ReturnsAllOfThem()
    {
        var stderr = "SCRIPTS.bcs:5:3: error: unknown identifier `foo`\n" +
                     "SCRIPTS.bcs:9:1: error: expected `;`\n";

        var errors = ZtBccErrorParser.Parse(stderr);

        Assert.Equal(2, errors.Count);
        Assert.Equal(5, errors[0].Line);
        Assert.Equal(9, errors[1].Line);
    }

    [Fact]
    public void Parse_MessageContainingItsOwnColons_KeepsThemInTheMessage()
    {
        var errors = ZtBccErrorParser.Parse("SCRIPTS.bcs:5:3: error: expected `:` or `;`\n");

        Assert.Single(errors);
        Assert.Equal("error: expected `:` or `;`", errors[0].Message);
    }

    [Fact]
    public void Parse_NothingMatchesTheExpectedShape_FallsBackToOneWholeStderrError()
    {
        var errors = ZtBccErrorParser.Parse("some unstructured failure, no positions at all\n");

        Assert.Single(errors);
        Assert.Null(errors[0].FilePath);
        Assert.Equal("some unstructured failure, no positions at all", errors[0].Message);
    }

    [Fact]
    public void Parse_EmptyStderr_ReturnsNoErrors()
    {
        Assert.Empty(ZtBccErrorParser.Parse(string.Empty));
    }

    [Fact]
    public void Parse_BlankLinesBetweenRealOnes_AreIgnored()
    {
        var errors = ZtBccErrorParser.Parse("SCRIPTS.bcs:5:3: error: bad\n\n\n");

        Assert.Single(errors);
    }
}
