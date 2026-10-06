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
        // 45, not 44: save_map was added - map saving had no keyboard
        // shortcut at all before (only save_document, scoped to script
        // tabs), a real reported gap, not a count anyone meant to change
        // casually.
        Assert.Equal(45, KeyBindingRegistry.All.Count);
    }
}
