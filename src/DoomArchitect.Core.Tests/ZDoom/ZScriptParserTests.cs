using System.Text;
using DoomArchitect.Core.Configuration;
using DoomArchitect.Core.ZDoom;

namespace DoomArchitect.Core.Tests.ZDoom;

public class ZScriptParserTests
{
    // ZScriptClassStructure.Process's own inheritance walk requires "Actor"
    // to be a genuinely declared class somewhere in the scan (its own
    // ClassName is checked literally, there's no built-in sentinel) - real
    // gzdoom.pk3 always declares it for real (`class Actor : Thinker
    // native`), so every test source prepends the same minimal stand-in
    // rather than assuming "Actor" is implicitly known.
    private const string ActorStub = "class Actor : Thinker native {}\n";

    private static bool ParseAndFinalize(ZScriptParser parser, string source, string sourceName = "zscript.txt") =>
        ParseAndFinalize(parser, source, true, sourceName);

    private static bool ParseAndFinalize(ZScriptParser parser, string source, bool withActorStub, string sourceName = "zscript.txt")
    {
        var full = withActorStub ? ActorStub + source : source;
        if (!parser.Parse(Encoding.ASCII.GetBytes(full), sourceName)) return false;
        return parser.CompleteParsing();
    }

    [Fact]
    public void Parse_SimpleActorClass_IsDiscoveredAsAnActor()
    {
        var parser = new ZScriptParser();
        var ok = ParseAndFinalize(parser, """
            class MyMonster : Actor
            {
                Default
                {
                    Health 100;
                }
            }
            """);

        Assert.True(ok);
        var actor = parser.GetActorByName("MyMonster");
        Assert.NotNull(actor);
        Assert.Equal("100", actor!.GetPropertyValueString("health", 0));
    }

    [Fact]
    public void Parse_ForwardReferencedInheritance_ResolvesCorrectly()
    {
        // ZScript allows a class to inherit a class declared *later* in the
        // same file - this only works because Parse() records every class
        // header first, and CompleteParsing() resolves bodies/inheritance
        // afterward, once every class in the scan is known.
        var parser = new ZScriptParser();
        var ok = ParseAndFinalize(parser, """
            class DerivedMonster : BaseMonster
            {
                Default { Health 200; }
            }
            class BaseMonster : Actor
            {
                Default { Radius 20; Health 100; }
            }
            """);

        Assert.True(ok);
        var derived = parser.GetActorByName("DerivedMonster");
        Assert.NotNull(derived);
        Assert.Equal("20", derived!.GetPropertyValueString("radius", 0)); // inherited
        Assert.Equal("200", derived.GetPropertyValueString("health", 0)); // overridden
    }

    [Fact]
    public void Parse_SelfInheritance_ReportsAFatalError()
    {
        var parser = new ZScriptParser();
        var ok = ParseAndFinalize(parser, "class Broken : Broken { }");

        Assert.False(ok);
        Assert.True(parser.HasError);
    }

    [Fact]
    public void Parse_UnknownParentClass_ReportsAFatalError()
    {
        var parser = new ZScriptParser();
        var ok = ParseAndFinalize(parser, "class Broken : DoesNotExist { }");

        Assert.False(ok);
        Assert.True(parser.HasError);
    }

    [Fact]
    public void Parse_Flags_SetViaDefaultBlock()
    {
        var parser = new ZScriptParser();
        ParseAndFinalize(parser, """
            class Wall : Actor
            {
                Default { +SOLID +SHOOTABLE -NOGRAVITY }
            }
            """);

        var actor = parser.GetActorByName("Wall");
        Assert.True(actor!.GetFlagValue("solid", false));
        Assert.True(actor.GetFlagValue("shootable", false));
        Assert.False(actor.GetFlagValue("nogravity", true));
    }

    [Fact]
    public void Parse_States_ResolvesSpawnSprite()
    {
        var parser = new ZScriptParser();
        ParseAndFinalize(parser, """
            class MyMonster : Actor
            {
                States
                {
                Spawn:
                    POSS A 10;
                    Loop;
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
        var parser = new ZScriptParser();
        ParseAndFinalize(parser, """
            class MyMonster : Actor
            {
                // $sprite forced properties are only ever read from inside
                // a Default block (ParseDefaultBlock's own comment
                // handling) - a GZDB comment anywhere else in the class
                // body is only ever collected for the (dropped) uservars
                // feature, never applied to the actor's own properties.
                Default
                {
                    // $sprite "CUSTA0"
                }
                States { Spawn: POSS A 10; }
            }
            """);

        var actor = parser.GetActorByName("MyMonster");
        var sprite = actor!.FindSuitableSprite();
        Assert.Equal("CUSTA0", sprite!.Sprite);
    }

    [Fact]
    public void Parse_FieldsAndMethods_AreSkippedWithoutAffectingParsing()
    {
        // Exercises the generic member-skip path (modifiers, generics,
        // array fields, a method with a body) - none of it should leak
        // into the actor's own properties, and parsing should continue
        // cleanly afterward.
        var parser = new ZScriptParser();
        var ok = ParseAndFinalize(parser, """
            class MyMonster : Actor
            {
                int myField;
                private Array<int> myArray;
                int[] myOtherArray;

                virtual void Tick()
                {
                    super.Tick();
                    if (health > 0) { health -= 1; }
                }

                Default { Health 100; }
            }
            """);

        Assert.True(ok);
        var actor = parser.GetActorByName("MyMonster");
        Assert.NotNull(actor);
        Assert.Equal("100", actor!.GetPropertyValueString("health", 0));
    }

    [Fact]
    public void Parse_Include_ResolvesViaOnIncludeDelegate()
    {
        var parser = new ZScriptParser
        {
            OnInclude = filename => filename == "monsters.zs" ? Encoding.ASCII.GetBytes("class Included : Actor {}") : null,
        };

        var ok = ParseAndFinalize(parser, "#include \"monsters.zs\"");

        Assert.True(ok);
        Assert.NotNull(parser.GetActorByName("Included"));
    }

    [Fact]
    public void Parse_ExtendClass_AddsPropertiesToTheOriginalClass()
    {
        var parser = new ZScriptParser();
        var ok = ParseAndFinalize(parser, """
            class MyMonster : Actor
            {
                Default { Radius 20; }
            }
            extend class MyMonster
            {
                Default { Height 64; }
            }
            """);

        Assert.True(ok);
        var actor = parser.GetActorByName("MyMonster");
        Assert.Equal("20", actor!.GetPropertyValueString("radius", 0));
        Assert.Equal("64", actor.GetPropertyValueString("height", 0));
    }

    [Fact]
    public void Parse_Mixin_AppliesPropertiesToTheIncludingClass()
    {
        var parser = new ZScriptParser();
        var ok = ParseAndFinalize(parser, """
            mixin class MyMixin
            {
                Default { Radius 42; }
            }
            class MyMonster : Actor
            {
                mixin MyMixin;
            }
            """);

        Assert.True(ok);
        var actor = parser.GetActorByName("MyMonster");
        Assert.Equal("42", actor!.GetPropertyValueString("radius", 0));
    }

    [Fact]
    public void Parse_RegionAsCategory_AttachesCategoryInfoToActorsInsideIt()
    {
        var parser = new ZScriptParser();
        ParseAndFinalize(parser, """
            #region Monsters
            class MyMonster : Actor {}
            #endregion
            """);

        // A genuine, faithfully-ported UDB quirk: the #region name is read
        // token-by-token with no leading-whitespace skip, so the space
        // right after "#region" ends up as part of the category string
        // itself (it isn't a "\"/"/" path separator, so splitting doesn't
        // remove it either). Real UDB has this exact behavior too.
        var actor = parser.GetActorByName("MyMonster");
        Assert.NotNull(actor!.CategoryInfo);
        Assert.Contains(" Monsters", actor.CategoryInfo!.Category);
    }

    [Fact]
    public void InheritFromStaticGameConfiguration_FillsInSpriteWhenParentMatchesAThingType()
    {
        // Unlike DECORATE (whose own inheritance tolerates a completely
        // undeclared parent name, see DecorateParserTests's equivalent),
        // ZScriptClassStructure.Process fatally rejects any class whose
        // parent chain doesn't resolve to a genuinely declared class - so
        // this only ever fires for a class whose real ZScript parent
        // *also* happens to match a static .cfg entry by name. The sprite/
        // state fallback still applies unconditionally in that case; the
        // radius/height/flags fallback does not (it's gated on the actor
        // having no real ZScript base class at all, which this one does).
        var config = new FakeGameConfiguration(new Dictionary<int, ThingTypeInfo>
        {
            [1] = new(1, "Actor", "POSSA1", 20f, 56f, false, true, 0, "monsters", "Actor"),
        });

        var parser = new ZScriptParser { GameConfiguration = config };
        var ok = ParseAndFinalize(parser, "class MyZombie : Actor {}");

        Assert.True(ok);
        var actor = parser.GetActorByName("MyZombie");
        Assert.NotNull(actor);
        var sprite = actor!.FindSuitableSprite();
        Assert.Equal("POSSA", sprite!.Sprite);
    }

    private sealed class FakeGameConfiguration : IGameConfiguration
    {
        private readonly Dictionary<int, ThingTypeInfo> _thingTypes;
        public FakeGameConfiguration(Dictionary<int, ThingTypeInfo> thingTypes) => _thingTypes = thingTypes;

        public ThingTypeInfo? GetThingType(int doomEdNum) => _thingTypes.GetValueOrDefault(doomEdNum);
        public IReadOnlyList<ThingTypeInfo> GetThingTypes() => _thingTypes.Values.ToList();
        public ActionInfo? GetAction(int special) => null;
        public IReadOnlyList<ActionInfo> GetActions() => Array.Empty<ActionInfo>();
        public SectorSpecialInfo? GetSectorSpecial(int type) => null;
        public IReadOnlyList<SectorSpecialInfo> GetSectorSpecials() => Array.Empty<SectorSpecialInfo>();
        public IReadOnlyList<SectorFlagInfo> GetSectorFlags() => Array.Empty<SectorFlagInfo>();
        public IReadOnlyList<SectorFlagInfo> GetLinedefFlags() => Array.Empty<SectorFlagInfo>();
        public IReadOnlyList<SectorFlagInfo> GetLinedefActivations() => Array.Empty<SectorFlagInfo>();
        public IReadOnlyList<SectorFlagInfo> GetThingFlags() => Array.Empty<SectorFlagInfo>();
        public IReadOnlyList<string> GetDamageTypes() => Array.Empty<string>();
        public bool MixTexturesAndFlats => false;
        public IReadOnlyList<SkillInfo> GetSkills() => Array.Empty<SkillInfo>();
        public string TestParameters => "";
        public bool TestShortPaths => false;
        public string DecorateGames => "";
        public IReadOnlyList<DoomArchitect.Core.Configuration.RequiredArchive> GetRequiredArchives() => Array.Empty<DoomArchitect.Core.Configuration.RequiredArchive>();
    }
}
