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
        Assert.Equal(36, KeyBindingRegistry.All.Count);
    }
}
