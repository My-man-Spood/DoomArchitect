using System.Text;
using DoomArchitect.Core.ZDoom;

namespace DoomArchitect.Core.Tests.ZDoom;

public class DecorateParserTests
{
    private static bool Parse(DecorateParser parser, string source, string sourceName = "DECORATE") =>
        parser.Parse(Encoding.ASCII.GetBytes(source), sourceName);

    [Fact]
    public void Parse_SimpleActor_ReadsClassNameAndDoomEdNum()
    {
        // A property's value list ends at "}" the same way it does at a
        // newline (see DecorateActorStructure's real default-case handling)
        // - but unlike the newline case, hitting "}" bails out of parsing
        // the whole actor immediately, *without* ever storing the values
        // read so far. This is a genuine, faithfully-ported quirk of real
        // DECORATE/UDB, not a porting bug - it's why every real DECORATE
        // file puts each property on its own line rather than minifying,
        // and why this test does too.
        var parser = new DecorateParser();
        var ok = Parse(parser, """
            actor MyMonster 5000
            {
                Health 100
            }
            """);

        Assert.True(ok);
        var actor = parser.GetActorByName("MyMonster");
        Assert.NotNull(actor);
        Assert.Equal(5000, actor!.DoomEdNum);
        Assert.Equal("100", actor.GetPropertyValueString("health", 0));
    }

    [Fact]
    public void Parse_Inheritance_ResolvesBaseClassAndInheritedProperties()
    {
        var parser = new DecorateParser();
        var ok = Parse(parser, """
            actor BaseMonster 5000
            {
                Radius 20
                Health 100
            }
            actor DerivedMonster : BaseMonster replaces BaseMonster 5001
            {
                Health 200
            }
            """);

        Assert.True(ok);
        var derived = parser.GetActorByName("DerivedMonster");
        Assert.NotNull(derived);
        Assert.Equal("BaseMonster", derived!.InheritsClass);
        Assert.Equal("20", derived.GetPropertyValueString("radius", 0)); // inherited
        Assert.Equal("200", derived.GetPropertyValueString("health", 0)); // overridden
    }

    [Fact]
    public void Parse_Flags_SetAndClearedCorrectly()
    {
        var parser = new DecorateParser();
        var ok = Parse(parser, "actor Wall 5002 { +SOLID +SHOOTABLE -NOGRAVITY }");

        Assert.True(ok);
        var actor = parser.GetActorByName("Wall");
        Assert.True(actor!.GetFlagValue("solid", false));
        Assert.True(actor.GetFlagValue("shootable", false));
        Assert.False(actor.GetFlagValue("nogravity", true));
    }

    [Fact]
    public void Parse_MonsterKeyword_SetsExpectedFlagBundle()
    {
        var parser = new DecorateParser();
        Parse(parser, "actor Zombie 5003 { Monster }");

        var actor = parser.GetActorByName("Zombie");
        Assert.True(actor!.GetFlagValue("shootable", false));
        Assert.True(actor.GetFlagValue("countkill", false));
        Assert.True(actor.GetFlagValue("ismonster", false));
    }

    [Fact]
    public void Parse_States_ResolvesSpawnSprite()
    {
        var parser = new DecorateParser();
        Parse(parser, """
            actor MyMonster 5000
            {
                States
                {
                Spawn:
                    POSS A 10
                    Loop
                }
            }
            """);

        var actor = parser.GetActorByName("MyMonster");
        Assert.True(actor!.HasState("spawn"));
        var sprite = actor.FindSuitableSprite();
        Assert.Equal("POSSA", sprite!.Sprite);
    }

    [Fact]
    public void Parse_DollarSpriteProperty_OverridesStateSprite()
    {
        var parser = new DecorateParser();
        Parse(parser, """
            actor MyMonster 5000
            {
                $sprite "CUSTA0"
                States { Spawn: POSS A 10 }
            }
            """);

        var actor = parser.GetActorByName("MyMonster");
        var sprite = actor!.FindSuitableSprite();
        Assert.Equal("CUSTA0", sprite!.Sprite);
    }

    [Fact]
    public void Parse_GameProperty_GatesCheckActorSupported()
    {
        // Same "}" -on-the-same-line trap as above - Game needs its own line too.
        var parser = new DecorateParser();
        Parse(parser, """
            actor Only_Heretic 5000
            {
                Game Heretic
            }
            """);

        // Not fetched via GetActorByName - that only returns actors already
        // supported by the *parser's own* configured game (none set here),
        // which "Only_Heretic" correctly isn't. AllActors holds every
        // parsed actor regardless of support, matching UDB's own real
        // distinction between "actors" (supported) and "archivedactors" (all).
        var actor = parser.AllActors.First(a => a.ClassName == "Only_Heretic");
        Assert.False(actor.CheckActorSupported("doom"));
        Assert.True(actor.CheckActorSupported("heretic"));
    }

    [Fact]
    public void Parse_DuplicateActor_ReportsError()
    {
        var parser = new DecorateParser();
        var ok = Parse(parser, "actor Dupe 5000 {} actor Dupe 5001 {}");

        Assert.False(ok);
        Assert.True(parser.HasError);
    }

    [Fact]
    public void Parse_Include_ResolvesViaOnIncludeDelegate()
    {
        var parser = new DecorateParser
        {
            OnInclude = filename => filename == "monsters.txt" ? Encoding.ASCII.GetBytes("actor Included 5000 {}") : null,
        };

        var ok = Parse(parser, "#include \"monsters.txt\"");

        Assert.True(ok);
        Assert.NotNull(parser.GetActorByName("Included"));
    }

    [Fact]
    public void Parse_UnresolvableInclude_IsToleratedNotFatal()
    {
        var parser = new DecorateParser { OnInclude = _ => null };

        var ok = Parse(parser, "#include \"missing.txt\"\nactor StillParsed 5000 {}");

        Assert.True(ok);
        Assert.NotNull(parser.GetActorByName("StillParsed"));
    }

    [Fact]
    public void Parse_RegionAsCategory_AttachesCategoryInfoToActorsInsideIt()
    {
        var parser = new DecorateParser();
        Parse(parser, """
            #region Monsters
            actor MyMonster 5000 {}
            #endregion
            """);

        var actor = parser.GetActorByName("MyMonster");
        Assert.NotNull(actor!.CategoryInfo);
        Assert.Contains("Monsters", actor.CategoryInfo!.Category);
    }

    [Fact]
    public void Parse_UnknownTopLevelStructure_IsSkippedWithoutError()
    {
        var parser = new DecorateParser();
        var ok = Parse(parser, """
            enum { FOO = 1 };
            actor MyMonster 5000 {}
            """);

        Assert.True(ok);
        Assert.NotNull(parser.GetActorByName("MyMonster"));
    }

    [Fact]
    public void InheritFromStaticGameConfiguration_FillsInSpriteAndRadiusFromTheThingType()
    {
        var config = new FakeGameConfiguration(new Dictionary<int, DoomArchitect.Core.Configuration.ThingTypeInfo>
        {
            [3004] = new(3004, "Former Human", "POSSA1", 20f, 56f, false, true, 0, "monsters", "ZombieMan"),
        });

        var parser = new DecorateParser { GameConfiguration = config };
        Parse(parser, "actor MyZombie : ZombieMan 5000 {}");

        var actor = parser.GetActorByName("MyZombie");
        Assert.Equal("20", actor!.GetPropertyValueString("radius", 0));
        var sprite = actor.FindSuitableSprite();
        Assert.Equal("POSSA", sprite!.Sprite);
    }

    private sealed class FakeGameConfiguration : DoomArchitect.Core.Configuration.IGameConfiguration
    {
        private readonly Dictionary<int, DoomArchitect.Core.Configuration.ThingTypeInfo> _thingTypes;
        public FakeGameConfiguration(Dictionary<int, DoomArchitect.Core.Configuration.ThingTypeInfo> thingTypes) => _thingTypes = thingTypes;

        public DoomArchitect.Core.Configuration.ThingTypeInfo? GetThingType(int doomEdNum) => _thingTypes.GetValueOrDefault(doomEdNum);
        public IReadOnlyList<DoomArchitect.Core.Configuration.ThingTypeInfo> GetThingTypes() => _thingTypes.Values.ToList();
        public DoomArchitect.Core.Configuration.ActionInfo? GetAction(int special) => null;
        public IReadOnlyList<DoomArchitect.Core.Configuration.ActionInfo> GetActions() => Array.Empty<DoomArchitect.Core.Configuration.ActionInfo>();
        public DoomArchitect.Core.Configuration.SectorSpecialInfo? GetSectorSpecial(int type) => null;
        public IReadOnlyList<DoomArchitect.Core.Configuration.SectorSpecialInfo> GetSectorSpecials() => Array.Empty<DoomArchitect.Core.Configuration.SectorSpecialInfo>();
        public IReadOnlyList<DoomArchitect.Core.Configuration.SectorFlagInfo> GetSectorFlags() => Array.Empty<DoomArchitect.Core.Configuration.SectorFlagInfo>();
        public IReadOnlyList<DoomArchitect.Core.Configuration.SectorFlagInfo> GetLinedefFlags() => Array.Empty<DoomArchitect.Core.Configuration.SectorFlagInfo>();
        public IReadOnlyList<DoomArchitect.Core.Configuration.SectorFlagInfo> GetLinedefActivations() => Array.Empty<DoomArchitect.Core.Configuration.SectorFlagInfo>();
        public IReadOnlyList<DoomArchitect.Core.Configuration.SectorFlagInfo> GetThingFlags() => Array.Empty<DoomArchitect.Core.Configuration.SectorFlagInfo>();
        public IReadOnlyList<string> GetDamageTypes() => Array.Empty<string>();
        public bool MixTexturesAndFlats => false;
        public IReadOnlyList<DoomArchitect.Core.Configuration.SkillInfo> GetSkills() => Array.Empty<DoomArchitect.Core.Configuration.SkillInfo>();
        public string TestParameters => "";
        public bool TestShortPaths => false;
        public string DecorateGames => "";
        public IReadOnlyList<DoomArchitect.Core.Configuration.RequiredArchive> GetRequiredArchives() => Array.Empty<DoomArchitect.Core.Configuration.RequiredArchive>();
    }
}
