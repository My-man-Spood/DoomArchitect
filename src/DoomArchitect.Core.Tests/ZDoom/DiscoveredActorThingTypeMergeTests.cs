using System.Text;
using DoomArchitect.Core.Configuration;
using DoomArchitect.Core.ZDoom;

namespace DoomArchitect.Core.Tests.ZDoom;

public class DiscoveredActorThingTypeMergeTests
{
    private const string ActorStub = "class Actor : Thinker native {}\n";

    private static ZScriptParser ParsedZScript(string source)
    {
        var parser = new ZScriptParser();
        Assert.True(parser.Parse(Encoding.ASCII.GetBytes(ActorStub + source), "zscript.txt"));
        Assert.True(parser.CompleteParsing());
        return parser;
    }

    private static DecorateParser ParsedDecorate(string source)
    {
        var parser = new DecorateParser();
        Assert.True(parser.Parse(Encoding.ASCII.GetBytes(source), "DECORATE"));
        return parser;
    }

    private static readonly IReadOnlyDictionary<int, ThingTypeInfo> EmptyStatic = new Dictionary<int, ThingTypeInfo>();

    [Fact]
    public void Merge_NewDecorateActor_IsAddedAtItsOwnDoomEdNum()
    {
        // Each property on its own line - see DecorateParserTests's own
        // note on why a property sharing a line with the closing "}"
        // silently never gets saved (a real, faithfully-ported UDB quirk).
        var decorate = ParsedDecorate("""
            actor MyMonster 5000
            {
                Radius 20
                Health 100
            }
            """);
        var zscript = ParsedZScript("");

        var merged = DiscoveredActorThingTypeMerge.Merge(EmptyStatic, decorate, zscript);

        Assert.True(merged.ContainsKey(5000));
        Assert.Equal("MyMonster", merged[5000].ClassName);
        Assert.Equal(20f, merged[5000].Radius);
    }

    [Fact]
    public void Merge_DecorateReplaces_UpdatesTheStaticThingInPlaceAtItsOwnDoomEdNum()
    {
        var staticThings = new Dictionary<int, ThingTypeInfo>
        {
            [3004] = new(3004, "Former Human", "POSSA1", 20f, 56f, false, true, 0, "monsters", "ZombieMan"),
        };

        var decorate = ParsedDecorate("""
            actor MyZombie : ZombieMan replaces ZombieMan
            {
                Radius 24
            }
            """);
        var zscript = ParsedZScript("");

        var merged = DiscoveredActorThingTypeMerge.Merge(staticThings, decorate, zscript);

        Assert.True(merged.ContainsKey(3004)); // same DoomEdNum as the original ZombieMan, not a new one
        Assert.Equal("MyZombie", merged[3004].ClassName);
        Assert.Equal(24f, merged[3004].Radius);
    }

    [Fact]
    public void Merge_ClassNameCollision_DecorateWinsOverZScript()
    {
        // Both formats declare a class named "MyMonster" - exercised via a
        // MAPINFO override that looks the name up in the merged-by-class
        // dictionary, which is exactly where the DECORATE-wins collision
        // rule actually applies (ZScript actors always have their own real
        // DoomEdNum resolved directly when they have one, so a same-number
        // collision there is a separate, later-write-wins concern, not
        // this one).
        var decorate = ParsedDecorate("""
            actor MyMonster 5000
            {
                Radius 10
            }
            """);
        var zscript = ParsedZScript("""
            class MyMonster : Actor
            {
                Default { Radius 99; }
            }
            """);
        var mapinfo = new Dictionary<int, string> { [6000] = "mymonster" };

        var merged = DiscoveredActorThingTypeMerge.Merge(EmptyStatic, decorate, zscript, mapinfo);

        Assert.Equal(10f, merged[6000].Radius); // the DECORATE MyMonster's own radius, not ZScript's
    }

    [Fact]
    public void Merge_ZScriptActorWithNoDoomEdNum_IsNotAddedUntilMapinfoAssignsOne()
    {
        var zscript = ParsedZScript("class MyZScriptActor : Actor { Default { Radius 30; } }");
        var decorate = ParsedDecorate("");

        var mergedWithoutMapinfo = DiscoveredActorThingTypeMerge.Merge(EmptyStatic, decorate, zscript);
        Assert.Empty(mergedWithoutMapinfo);

        var mapinfo = new Dictionary<int, string> { [5000] = "myzscriptactor" };
        var mergedWithMapinfo = DiscoveredActorThingTypeMerge.Merge(EmptyStatic, decorate, zscript, mapinfo);

        Assert.True(mergedWithMapinfo.ContainsKey(5000));
        Assert.Equal("MyZScriptActor", mergedWithMapinfo[5000].ClassName);
        Assert.Equal(30f, mergedWithMapinfo[5000].Radius);
    }

    [Fact]
    public void Merge_MapinfoNoneOverride_RemovesTheEntry()
    {
        var decorate = ParsedDecorate("actor MyMonster 5000 { }");
        var zscript = ParsedZScript("");
        var mapinfo = new Dictionary<int, string> { [5000] = "none" };

        var merged = DiscoveredActorThingTypeMerge.Merge(EmptyStatic, decorate, zscript, mapinfo);

        Assert.False(merged.ContainsKey(5000));
    }

    [Fact]
    public void Merge_MapinfoOverrideMatchingAStaticClassName_ClonesItToTheNewDoomEdNum()
    {
        var staticThings = new Dictionary<int, ThingTypeInfo>
        {
            [3004] = new(3004, "Former Human", "POSSA1", 20f, 56f, false, true, 0, "monsters", "ZombieMan"),
        };
        var decorate = ParsedDecorate("");
        var zscript = ParsedZScript("");
        var mapinfo = new Dictionary<int, string> { [9000] = "ZombieMan" };

        var merged = DiscoveredActorThingTypeMerge.Merge(staticThings, decorate, zscript, mapinfo);

        Assert.True(merged.ContainsKey(9000));
        Assert.Equal("ZombieMan", merged[9000].ClassName);
        Assert.Equal(20f, merged[9000].Radius);
        Assert.True(merged.ContainsKey(3004)); // the original is untouched
    }

    [Fact]
    public void Merge_DollarTitleAndCategoryProperties_OverrideTheDefaults()
    {
        var decorate = ParsedDecorate("""
            actor MyMonster 5000
            {
                $title "My Custom Monster"
                $category "Custom/Monsters"
            }
            """);
        var zscript = ParsedZScript("");

        var merged = DiscoveredActorThingTypeMerge.Merge(EmptyStatic, decorate, zscript);

        Assert.Equal("My Custom Monster", merged[5000].Title);
        Assert.Equal("Custom/Monsters", merged[5000].Category);
    }

    [Fact]
    public void Merge_NoTitleOrCategoryGiven_FallsBackToClassNameAndUserDefined()
    {
        var decorate = ParsedDecorate("actor MyMonster 5000 { }");
        var zscript = ParsedZScript("");

        var merged = DiscoveredActorThingTypeMerge.Merge(EmptyStatic, decorate, zscript);

        Assert.Equal("MyMonster", merged[5000].Title);
        Assert.Equal("User-defined", merged[5000].Category);
    }

    [Fact]
    public void Merge_InheritsFromAStaticActor_FillsInDefaultsForANewDoomEdNum()
    {
        var staticThings = new Dictionary<int, ThingTypeInfo>
        {
            [3004] = new(3004, "Former Human", "POSSA1", 20f, 56f, true, true, 5, "monsters", "ZombieMan"),
        };
        var decorate = ParsedDecorate("actor MyZombie : ZombieMan 5010 { }"); // no radius/height of its own
        var zscript = ParsedZScript("");

        var merged = DiscoveredActorThingTypeMerge.Merge(staticThings, decorate, zscript);

        Assert.True(merged.ContainsKey(5010));
        Assert.Equal(20f, merged[5010].Radius); // inherited from ZombieMan
        Assert.Equal(56f, merged[5010].Height);
        Assert.True(merged[5010].Hangs);
    }
}
