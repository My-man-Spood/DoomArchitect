using DoomArchitect.Core.Configuration;

namespace DoomArchitect.Core.Tests.Configuration;

public class CfgLoaderTests
{
    [Fact]
    public void Load_NoIncludes_ResolvesAssignmentsAndNestedBlocks()
    {
        var loader = new CfgLoader(new InMemoryCfgFileSource(new Dictionary<string, string>
        {
            ["Main.cfg"] = "a = 1; group { b = 2; }",
        }));

        var document = loader.Load("Main.cfg");

        Assert.Equal(1L, document.Find("a")!.Value.AsLong());
        Assert.Equal(2L, document.FindBlock("group")!.Find("b")!.Value.AsLong());
    }

    [Fact]
    public void Load_Include_MergesTheIncludedFileIntoThisScope()
    {
        var loader = new CfgLoader(new InMemoryCfgFileSource(new Dictionary<string, string>
        {
            ["Main.cfg"] = "include(\"Shared.cfg\"); local = 1;",
            ["Shared.cfg"] = "shared = 2;",
        }));

        var document = loader.Load("Main.cfg");

        Assert.Equal(1L, document.Find("local")!.Value.AsLong());
        Assert.Equal(2L, document.Find("shared")!.Value.AsLong());
    }

    [Fact]
    public void Load_IncludeConflictingWithAPriorLeafAssignment_TheIncludedValueWins()
    {
        // Matches UDB's real Combine(cs, inc) semantics: the second
        // argument (the included content) wins a leaf conflict against
        // whatever the including scope already had at that point - not
        // "whichever was written later in a naive sense", specifically
        // "the include always wins over what came before it".
        var loader = new CfgLoader(new InMemoryCfgFileSource(new Dictionary<string, string>
        {
            ["Main.cfg"] = "value = 1; include(\"Shared.cfg\");",
            ["Shared.cfg"] = "value = 2;",
        }));

        var document = loader.Load("Main.cfg");

        Assert.Equal(2L, document.Find("value")!.Value.AsLong());
    }

    [Fact]
    public void Load_AssignmentAfterInclude_OverridesTheIncludedValue()
    {
        var loader = new CfgLoader(new InMemoryCfgFileSource(new Dictionary<string, string>
        {
            ["Main.cfg"] = "include(\"Shared.cfg\"); value = 3;",
            ["Shared.cfg"] = "value = 2;",
        }));

        var document = loader.Load("Main.cfg");

        Assert.Equal(3L, document.Find("value")!.Value.AsLong());
    }

    [Fact]
    public void Load_IncludeAddingToAnExistingBlock_MergesRecursivelyRatherThanReplacing()
    {
        // The real-world case this whole feature depends on: Doom2.cfg
        // layering its own exclusive monsters into the same "monsters"
        // category a shared include already defined some monsters in.
        var loader = new CfgLoader(new InMemoryCfgFileSource(new Dictionary<string, string>
        {
            ["Main.cfg"] = "thingtypes { monsters { 3004 { title = \"Zombieman\"; } } } include(\"Extra.cfg\");",
            ["Extra.cfg"] = "thingtypes { monsters { 64 { title = \"Arch-vile\"; } } }",
        }));

        var document = loader.Load("Main.cfg");

        var monsters = document.FindBlock("thingtypes")!.FindBlock("monsters")!;
        Assert.Equal("Zombieman", monsters.FindBlock("3004")!.Find("title")!.Value.AsString());
        Assert.Equal("Arch-vile", monsters.FindBlock("64")!.Find("title")!.Value.AsString());
    }

    [Fact]
    public void Load_IncludeWithSubPath_ExtractsOnlyThatNestedScope()
    {
        var loader = new CfgLoader(new InMemoryCfgFileSource(new Dictionary<string, string>
        {
            ["Main.cfg"] = "include(\"Shared.cfg\", \"thingtypes.monsters\");",
            ["Shared.cfg"] = "thingtypes { monsters { 3004 { title = \"Zombieman\"; } } linedeftypes_note = 1; }",
        }));

        var document = loader.Load("Main.cfg");

        Assert.Equal("Zombieman", document.FindBlock("3004")!.Find("title")!.Value.AsString());
        Assert.Null(document.Find("linedeftypes_note"));
        Assert.Null(document.FindBlock("thingtypes"));
    }

    [Fact]
    public void Load_IncludeWithMissingSubPath_Throws()
    {
        var loader = new CfgLoader(new InMemoryCfgFileSource(new Dictionary<string, string>
        {
            ["Main.cfg"] = "include(\"Shared.cfg\", \"nope\");",
            ["Shared.cfg"] = "a = 1;",
        }));

        Assert.Throws<InvalidOperationException>(() => loader.Load("Main.cfg"));
    }

    [Fact]
    public void Load_FileIncludingItselfByBareFilename_Throws()
    {
        var loader = new CfgLoader(new InMemoryCfgFileSource(new Dictionary<string, string>
        {
            ["Main.cfg"] = "include(\"Main.cfg\");",
        }));

        Assert.Throws<InvalidOperationException>(() => loader.Load("Main.cfg"));
    }

    [Fact]
    public void Load_CircularIncludeAcrossTwoFiles_ThrowsRatherThanOverflowing()
    {
        var loader = new CfgLoader(new InMemoryCfgFileSource(new Dictionary<string, string>
        {
            ["A.cfg"] = "include(\"B.cfg\");",
            ["B.cfg"] = "include(\"A.cfg\");",
        }));

        Assert.Throws<InvalidOperationException>(() => loader.Load("A.cfg"));
    }

    [Fact]
    public void Load_SameIncludeReferencedTwice_OnlyReadsTheFileOnce()
    {
        var source = new InMemoryCfgFileSource(new Dictionary<string, string>
        {
            ["Main.cfg"] = "include(\"Shared.cfg\"); nested { include(\"Shared.cfg\"); }",
            ["Shared.cfg"] = "shared = 1;",
        });
        var loader = new CfgLoader(source);

        loader.Load("Main.cfg");

        // Main.cfg + Shared.cfg (read once, then served from cache the second time).
        Assert.Equal(2, source.ReadCount);
    }
}
