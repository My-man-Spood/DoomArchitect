using System.Linq;
using DoomArchitect.Core.Input;

namespace DoomArchitect.Core.Tests.Input;

public class KeyBindingRegistryTests
{
    [Fact]
    public void All_EveryActionNameIsUnique()
    {
        var names = KeyBindingRegistry.All.Select(a => a.Name).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void All_EveryActionHasANonEmptyCategoryTitleAndDescription()
    {
        Assert.All(KeyBindingRegistry.All, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Category));
            Assert.False(string.IsNullOrWhiteSpace(a.Title));
            Assert.False(string.IsNullOrWhiteSpace(a.Description));
        });
    }

    [Fact]
    public void All_EveryDefaultBindingHasANonEmptyKeyName()
    {
        Assert.All(KeyBindingRegistry.All, a => Assert.False(string.IsNullOrWhiteSpace(a.Default.KeyName)));
    }

    [Fact]
    public void All_ContainsExactlyTheDesignedActionCount()
    {
        // 46, not 45: flip_linedef was added - UDB's own Flip Linedef
        // (LinedefsMode.FlipLinedefs, F by default) had no equivalent here
        // at all, a real requested gap, not a count anyone meant to change
        // casually.
        Assert.Equal(46, KeyBindingRegistry.All.Count);
    }
}
