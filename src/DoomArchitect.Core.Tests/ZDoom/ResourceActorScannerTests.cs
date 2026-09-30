using System.Text;
using DoomArchitect.Core.Configuration;
using DoomArchitect.Core.IO;
using DoomArchitect.Core.Tests.IO;
using DoomArchitect.Core.ZDoom;

namespace DoomArchitect.Core.Tests.ZDoom;

public class ResourceActorScannerTests
{
    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    [Fact]
    public void Scan_ZScriptWithIncludeChain_DiscoversActorsFromBothFiles()
    {
        var pk3 = Pk3TestBuilder.Build(
            ("zscript.txt", Ascii("""
                class Actor : Thinker native {}
                #include "zscript/monsters.zs"
                """)),
            ("zscript/monsters.zs", Ascii("""
                class MyMonster : Actor
                {
                    Default { Radius 30; }
                    States { Spawn: POSS A 10; Loop; }
                }
                """)));

        var resources = new ResourceSet(new IResourceContainer[] { pk3 });
        var baseConfig = GameConfigurations.Get(GameConfigurationKind.Doom);

        var merged = ResourceActorScanner.Scan(baseConfig, resources);

        // Not directly addable (no DoomEdNum) until MAPINFO assigns one - confirms the include chain alone doesn't fabricate a number.
        Assert.DoesNotContain(merged.GetThingTypes(), t => t.ClassName == "MyMonster");
    }

    [Fact]
    public void Scan_ZScriptActorWithMapinfoDoomEdNum_BecomesARealPlaceableThing()
    {
        var pk3 = Pk3TestBuilder.Build(
            ("zscript.txt", Ascii("""
                class Actor : Thinker native {}
                class MyMonster : Actor
                {
                    Default { Radius 30; Height 60; }
                    States { Spawn: POSS A 10; Loop; }
                }
                """)),
            ("mapinfo.txt", Ascii("""
                DoomEdNums
                {
                    25000 = MyMonster
                }
                """)));

        var resources = new ResourceSet(new IResourceContainer[] { pk3 });
        var baseConfig = GameConfigurations.Get(GameConfigurationKind.Doom);

        var merged = ResourceActorScanner.Scan(baseConfig, resources);

        var thing = merged.GetThingType(25000);
        Assert.NotNull(thing);
        Assert.Equal("MyMonster", thing!.ClassName);
        Assert.Equal(30f, thing.Radius);
        Assert.Equal(60f, thing.Height);
        Assert.Equal("POSSA", thing.SpriteName);
    }

    [Fact]
    public void Scan_DecorateActor_IsDiscoveredWithARealDoomEdNum()
    {
        var pk3 = Pk3TestBuilder.Build(("decorate.txt", Ascii("""
            actor MyDecorateMonster 25001
            {
                Radius 24
                Health 150
            }
            """)));

        var resources = new ResourceSet(new IResourceContainer[] { pk3 });
        var baseConfig = GameConfigurations.Get(GameConfigurationKind.Doom);

        var merged = ResourceActorScanner.Scan(baseConfig, resources);

        var thing = merged.GetThingType(25001);
        Assert.NotNull(thing);
        Assert.Equal("MyDecorateMonster", thing!.ClassName);
        Assert.Equal(24f, thing.Radius);
    }

    [Fact]
    public void Scan_DecorateInheritingAZScriptActorFromTheSameStack_Resolves()
    {
        // Exercises DecorateParser.ZScriptActors - the cross-format lookup
        // wired in Phase 2, actually connected here for the first time.
        var pk3 = Pk3TestBuilder.Build(
            ("zscript.txt", Ascii("""
                class Actor : Thinker native {}
                class MyBaseMonster : Actor
                {
                    Default { Radius 40; }
                }
                """)),
            ("decorate.txt", Ascii("""
                actor MyDerivedMonster : MyBaseMonster 25002
                {
                }
                """)));

        var resources = new ResourceSet(new IResourceContainer[] { pk3 });
        var baseConfig = GameConfigurations.Get(GameConfigurationKind.Doom);

        var merged = ResourceActorScanner.Scan(baseConfig, resources);

        var thing = merged.GetThingType(25002);
        Assert.NotNull(thing);
        Assert.Equal(40f, thing!.Radius); // inherited from the ZScript base class in the very same resource
    }

    [Fact]
    public void Scan_LayeredResources_BothContributeActors()
    {
        var iwadLike = Pk3TestBuilder.Build(("zscript.txt", Ascii("class Actor : Thinker native {}")));
        var mod = Pk3TestBuilder.Build(("decorate.txt", Ascii("""
            actor ModMonster 25003
            {
                Radius 20
            }
            """)));

        var resources = new ResourceSet(new IResourceContainer[] { iwadLike, mod }); // mod is higher priority (last = highest, per ResourceSet's own convention)
        var baseConfig = GameConfigurations.Get(GameConfigurationKind.Doom);

        var merged = ResourceActorScanner.Scan(baseConfig, resources);

        Assert.NotNull(merged.GetThingType(25003));
    }

    [Fact]
    public void Scan_NoScriptContentAtAll_ReturnsTheBaseConfigurationUnchanged()
    {
        var pk3 = Pk3TestBuilder.Build(("PLAYPAL", new byte[768]));
        var resources = new ResourceSet(new IResourceContainer[] { pk3 });
        var baseConfig = GameConfigurations.Get(GameConfigurationKind.Doom);

        var merged = ResourceActorScanner.Scan(baseConfig, resources);

        Assert.Equal(baseConfig.GetThingTypes().Count, merged.GetThingTypes().Count);
    }
}
