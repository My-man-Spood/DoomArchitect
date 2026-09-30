using System.Text;
using DoomArchitect.Core.Configuration;
using DoomArchitect.Core.ZDoom;

namespace DoomArchitect.Core.Tests.ZDoom;

public class DiscoveredActorGameConfigurationTests
{
    [Fact]
    public void Load_MergesDiscoveredActorsIntoTheBaseConfigurationsThingTypes()
    {
        var baseConfig = GameConfigurations.Get(GameConfigurationKind.Doom);

        var decorate = new DecorateParser();
        Assert.True(decorate.Parse(Encoding.ASCII.GetBytes("""
            actor MyMonster 25000
            {
                Radius 30
            }
            """), "DECORATE"));

        var zscript = new ZScriptParser();
        Assert.True(zscript.Parse(Encoding.ASCII.GetBytes("class Actor : Thinker native {}"), "zscript.txt"));
        Assert.True(zscript.CompleteParsing());

        var merged = DiscoveredActorGameConfiguration.Load(baseConfig, decorate, zscript);

        // The new actor is there...
        var discovered = merged.GetThingType(25000);
        Assert.NotNull(discovered);
        Assert.Equal("MyMonster", discovered!.ClassName);

        // ...and every static entry the base configuration already had is untouched.
        var formerHuman = merged.GetThingType(3004);
        Assert.NotNull(formerHuman);
        Assert.Equal("ZombieMan", formerHuman!.ClassName);
    }

    [Fact]
    public void Load_EverythingElseDelegatesToTheBaseConfigurationUnchanged()
    {
        var baseConfig = GameConfigurations.Get(GameConfigurationKind.Doom);
        var decorate = new DecorateParser();
        Assert.True(decorate.Parse(Encoding.ASCII.GetBytes(""), "DECORATE"));
        var zscript = new ZScriptParser();
        Assert.True(zscript.Parse(Encoding.ASCII.GetBytes(""), "zscript.txt"));
        Assert.True(zscript.CompleteParsing());

        var merged = DiscoveredActorGameConfiguration.Load(baseConfig, decorate, zscript);

        Assert.Equal(baseConfig.GetSkills().Count, merged.GetSkills().Count);
        Assert.Equal(baseConfig.TestParameters, merged.TestParameters);
        Assert.Equal(baseConfig.DecorateGames, merged.DecorateGames);
        Assert.Equal(baseConfig.GetActions().Count, merged.GetActions().Count);
    }
}
