using DoomArchitect.Core.Configuration;

namespace DoomArchitect.Core.Tests.Configuration;

public class CfgBlockTests
{
    [Fact]
    public void WithAssignment_NewKey_AddsItWithoutDisturbingOthers()
    {
        var block = CfgBlock.Empty().WithAssignment("a", CfgValue.OfInt(1));

        var updated = block.WithAssignment("b", CfgValue.OfInt(2));

        Assert.Equal(1L, updated.Find("a")!.Value.AsLong());
        Assert.Equal(2L, updated.Find("b")!.Value.AsLong());
    }

    [Fact]
    public void WithAssignment_ExistingKey_ReplacesOnlyThatValue()
    {
        var block = CfgBlock.Empty()
            .WithAssignment("a", CfgValue.OfInt(1))
            .WithAssignment("b", CfgValue.OfInt(2));

        var updated = block.WithAssignment("a", CfgValue.OfInt(99));

        Assert.Equal(99L, updated.Find("a")!.Value.AsLong());
        Assert.Equal(2L, updated.Find("b")!.Value.AsLong());
    }

    [Fact]
    public void WithBlock_ExistingKey_ReplacesOnlyThatBlockAndKeepsOtherBlocks()
    {
        var block = CfgBlock.Empty()
            .WithBlock("keep", CfgBlock.Empty("keep").WithAssignment("x", CfgValue.OfInt(1)))
            .WithBlock("replace", CfgBlock.Empty("replace").WithAssignment("x", CfgValue.OfInt(1)));

        var updated = block.WithBlock("replace", CfgBlock.Empty("replace").WithAssignment("x", CfgValue.OfInt(2)));

        Assert.Equal(1L, updated.FindBlock("keep")!.Find("x")!.Value.AsLong());
        Assert.Equal(2L, updated.FindBlock("replace")!.Find("x")!.Value.AsLong());
    }

    [Fact]
    public void WithBlock_ChildKeyDiffersFromArgument_IsRetargetedToTheGivenKey()
    {
        var child = CfgBlock.Empty("originalname").WithAssignment("x", CfgValue.OfInt(1));

        var updated = CfgBlock.Empty().WithBlock("storedas", child);

        Assert.Equal("storedas", updated.FindBlock("storedas")!.Key);
    }
}
