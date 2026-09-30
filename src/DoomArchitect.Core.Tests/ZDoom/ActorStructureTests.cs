using DoomArchitect.Core.ZDoom;

namespace DoomArchitect.Core.Tests.ZDoom;

public class ActorStructureTests
{
    private static ActorStructure MakeActor(string className, ActorStructure? baseClass = null)
    {
        var actor = new ActorStructure { ClassName = className, BaseClass = baseClass };
        return actor;
    }

    [Fact]
    public void GetPropertyValueString_FallsBackToBaseClassWhenNotSetLocally()
    {
        var parent = MakeActor("Parent");
        parent.Properties["radius"] = new List<string> { "20" };
        var child = MakeActor("Child", parent);

        Assert.Equal("20", child.GetPropertyValueString("radius", 0, false));
    }

    [Fact]
    public void GetPropertyValueString_LocalValueOverridesBaseClass()
    {
        var parent = MakeActor("Parent");
        parent.Properties["radius"] = new List<string> { "20" };
        var child = MakeActor("Child", parent);
        child.Properties["radius"] = new List<string> { "5" };

        Assert.Equal("5", child.GetPropertyValueString("radius", 0, false));
    }

    [Fact]
    public void SkipSuper_PreventsFallingBackToBaseClass()
    {
        var parent = MakeActor("Parent");
        parent.Properties["radius"] = new List<string> { "20" };
        var child = MakeActor("Child", parent);
        child.SkipSuper = true;

        Assert.False(child.HasProperty("radius"));
    }

    [Fact]
    public void GetPropertyValueInt_HandlesNegativeSplitAcrossTwoTokens()
    {
        // ZDoom's own tokenizer can split "-5" as two separate tokens
        // ("-" then "5") depending on context - ActorStructure recombines them.
        var actor = MakeActor("Thing");
        actor.Properties["height"] = new List<string> { "-", "5" };

        Assert.Equal(-5, actor.GetPropertyValueInt("height", 0));
    }

    [Fact]
    public void HasFlagValue_WalksBaseClassChain()
    {
        var grandparent = MakeActor("Grandparent");
        grandparent.Flags["shootable"] = true;
        var parent = MakeActor("Parent", grandparent);
        var child = MakeActor("Child", parent);

        Assert.True(child.HasFlagValue("shootable"));
        Assert.True(child.GetFlagValue("shootable", false));
        Assert.False(child.GetFlagValue("solid", false));
    }

    [Fact]
    public void HasState_OnlyActorItselfExposesSpawnOnly()
    {
        var actorClass = MakeActor("Actor");
        actorClass.States["spawn"] = new StateStructure();
        actorClass.States["see"] = new StateStructure();

        Assert.True(actorClass.HasState("spawn"));
        Assert.False(actorClass.HasState("see"));
    }

    [Fact]
    public void HasState_NonActorClassExposesEveryDeclaredState()
    {
        var monster = MakeActor("MyMonster");
        monster.States["see"] = new StateStructure();

        Assert.True(monster.HasState("see"));
    }

    [Fact]
    public void GetAllStates_MergesBaseClassStatesWithoutOverwritingLocal()
    {
        var parent = MakeActor("Parent");
        parent.States["spawn"] = new StateStructure("PARNA0");
        parent.States["death"] = new StateStructure("PARDA0");
        var child = MakeActor("Child", parent);
        child.States["spawn"] = new StateStructure("CHLDA0");

        var all = child.GetAllStates();

        Assert.Equal("CHLDA0", all["spawn"].GetSprite(0).Sprite);
        Assert.Equal("PARDA0", all["death"].GetSprite(0).Sprite);
    }

    [Theory]
    [InlineData("", true)] // no $game property at all -> always included
    [InlineData("doom", true)]
    [InlineData("heretic", false)]
    public void CheckActorSupported_MatchesAgainstTheGamesConfiguredDecorateGames(string gameProp, bool expected)
    {
        var actor = MakeActor("MyMonster");
        if (gameProp.Length > 0) actor.Properties["game"] = new List<string> { gameProp };

        Assert.Equal(expected, actor.CheckActorSupported("doom"));
    }

    [Fact]
    public void FindSuitableSprite_PrefersForcedDollarSpriteProperty()
    {
        var actor = MakeActor("MyMonster");
        actor.Properties["$sprite"] = new List<string> { "\"CUST\"" };
        actor.States["spawn"] = new StateStructure("SPWNA0");

        var sprite = actor.FindSuitableSprite();

        Assert.Equal("CUST", sprite!.Sprite);
    }

    [Fact]
    public void FindSuitableSprite_FallsBackToSpawnState()
    {
        var actor = MakeActor("MyMonster");
        actor.States["spawn"] = new StateStructure("SPWNA0") { };

        var sprite = actor.FindSuitableSprite();

        Assert.Equal("SPWNA0", sprite!.Sprite);
    }

    [Fact]
    public void ParseCustomArguments_ReadsUsedArgsAndInheritsUnsetOnes()
    {
        var parent = MakeActor("Parent");
        parent.Properties["$arg0"] = new List<string> { "\"Speed\"" };
        parent.ParseCustomArguments();

        var child = MakeActor("Child", parent);
        child.ParseCustomArguments(); // no $argN of its own

        var arg0 = child.GetArgumentInfo(0);
        Assert.NotNull(arg0);
        Assert.True(arg0!.Used);
        Assert.Equal("Speed", arg0.Title);
    }

    [Fact]
    public void GetArgumentInfo_ClearArgsStopsFallbackToBaseClassWhenNotParsedLocally()
    {
        // HasPropertyWithValue/GetPropertyAllValues both walk the base
        // class chain, so ParseCustomArguments() already re-derives an
        // inherited $argN directly onto every subclass that calls it -
        // $clearargs's own effect is narrower: it only matters when a
        // class's own args were never parsed at all (this test), stopping
        // GetArgumentInfo from reaching all the way up to the base class's
        // real args instead of stopping there with nothing.
        var parent = MakeActor("Parent");
        parent.Properties["$arg0"] = new List<string> { "\"Speed\"" };
        parent.ParseCustomArguments();

        var child = MakeActor("Child", parent);
        child.Properties["$clearargs"] = new List<string>();

        Assert.Null(child.GetArgumentInfo(0));
    }
}
