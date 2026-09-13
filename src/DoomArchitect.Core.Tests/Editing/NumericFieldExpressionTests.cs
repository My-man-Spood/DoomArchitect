using DoomArchitect.Core.Editing;

namespace DoomArchitect.Core.Tests.Editing;

public class NumericFieldExpressionTests
{
    [Fact]
    public void Resolve_BlankText_ReturnsNull()
    {
        Assert.Null(NumericFieldExpression.Resolve("", 42));
        Assert.Null(NumericFieldExpression.Resolve("   ", 42));
    }

    [Fact]
    public void Resolve_PlainAbsoluteNumber_ReturnsItVerbatim()
    {
        Assert.Equal(128, NumericFieldExpression.Resolve("128", 42));
    }

    [Fact]
    public void Resolve_NegativeAbsoluteNumber_IsNotTreatedAsRelative()
    {
        Assert.Equal(-50, NumericFieldExpression.Resolve("-50", 42));
    }

    [Fact]
    public void Resolve_DoublePlusPrefix_AddsToOriginal()
    {
        Assert.Equal(58, NumericFieldExpression.Resolve("++16", 42));
    }

    [Fact]
    public void Resolve_DoubleMinusPrefix_SubtractsFromOriginal()
    {
        Assert.Equal(26, NumericFieldExpression.Resolve("--16", 42));
    }

    [Fact]
    public void Resolve_AsteriskPrefix_MultipliesOriginal()
    {
        Assert.Equal(84, NumericFieldExpression.Resolve("*2", 42));
    }

    [Fact]
    public void Resolve_SlashPrefix_DividesOriginal()
    {
        Assert.Equal(21, NumericFieldExpression.Resolve("/2", 42));
    }

    [Fact]
    public void Resolve_SlashByZero_ReturnsOriginalUnchanged()
    {
        Assert.Equal(42, NumericFieldExpression.Resolve("/0", 42));
    }

    [Fact]
    public void Resolve_UnparsableNonBlankText_ReturnsNull()
    {
        Assert.Null(NumericFieldExpression.Resolve("not a number", 42));
    }

    [Fact]
    public void ResolveInteger_RoundsToNearestLong()
    {
        Assert.Equal(43L, NumericFieldExpression.ResolveInteger("*1.02", 42));
    }
}
