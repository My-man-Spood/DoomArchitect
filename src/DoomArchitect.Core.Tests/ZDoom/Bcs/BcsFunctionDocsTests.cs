using DoomArchitect.Core.ZDoom.Bcs;

namespace DoomArchitect.Core.Tests.ZDoom.Bcs;

/// <summary>
/// Doesn't re-verify the content itself (that's the ZDoom Wiki research
/// pass's own job, not something a unit test can meaningfully assert) -
/// just the lookup/formatting mechanics, anchored on a couple of real,
/// stable entries confirmed against the real zcommon.bcs/BcsBuiltinFunctions
/// (see wiki-research/README.md for how the data itself was produced).
/// </summary>
public class BcsFunctionDocsTests
{
    [Fact]
    public void TryGetDoc_RealActionSpecial_HasASummaryAndItsOneRealParameter()
    {
        var doc = BcsFunctionDocs.TryGetDoc("Thing_Activate");

        Assert.NotNull(doc);
        Assert.NotEmpty(doc!.Value.Summary);
        Assert.Equal(new[] { "tid" }, doc.Value.Parameters.Select(p => p.Name));
    }

    [Fact]
    public void TryGetDoc_IsCaseInsensitive_SameAsEveryOtherBcsLookup()
    {
        Assert.NotNull(BcsFunctionDocs.TryGetDoc("thing_activate"));
        Assert.NotNull(BcsFunctionDocs.TryGetDoc("THING_ACTIVATE"));
    }

    [Fact]
    public void TryGetDoc_NotARealFunction_ReturnsNull()
    {
        Assert.Null(BcsFunctionDocs.TryGetDoc("ThisIsNotARealAcsFunction"));
    }

    [Fact]
    public void Format_NotARealFunction_ReturnsNull()
    {
        Assert.Null(BcsFunctionDocs.Format("ThisIsNotARealAcsFunction"));
    }

    [Fact]
    public void Format_FunctionWithParameters_ListsEachAsABullet()
    {
        var formatted = BcsFunctionDocs.Format("Thing_Activate");

        Assert.NotNull(formatted);
        Assert.Contains("- tid:", formatted);
    }

    [Fact]
    public void Format_RealBuiltinFunction_ResolvesByItsG_FuncsSpelling()
    {
        // BcsBuiltinFunctions.AllNames exposes "Spawnspot" (its own mechanical,
        // not-guessed display casing) - the doc lookup must still resolve it,
        // since hover consults both by the same word.
        Assert.NotNull(BcsFunctionDocs.Format("Spawnspot"));
        Assert.NotNull(BcsFunctionDocs.Format("SpawnSpot"));
    }

    /// <summary>A zero-parameter entry's formatted text is just the summary - no trailing "\n\n" or empty bullet list.</summary>
    [Fact]
    public void Format_FunctionWithNoParameters_IsJustTheSummary()
    {
        var doc = BcsFunctionDocs.TryGetDoc("GetScreenWidth");
        Assert.NotNull(doc);
        Assert.Empty(doc!.Value.Parameters);

        var formatted = BcsFunctionDocs.Format("GetScreenWidth");
        Assert.Equal(doc.Value.Summary, formatted);
    }

    [Fact]
    public void ApplyParameterNames_RealEntryWithOneRequiredParam_InsertsItsName()
    {
        Assert.Equal("function int Thing_Activate(int tid)", BcsFunctionDocs.ApplyParameterNames("function int Thing_Activate(int)", "Thing_Activate"));
    }

    /// <summary>The optional-parameter `[type]` bracket notation keeps its brackets, moved to wrap the name too - "[int]" -> "[int angle]", not "[int] angle" or "[int angle)".</summary>
    [Fact]
    public void ApplyParameterNames_OptionalParameter_KeepsTheNameInsideTheBrackets()
    {
        var result = BcsFunctionDocs.ApplyParameterNames("int Spawnspot(str, int, [int], [int])", "Spawnspot");

        Assert.Equal("int Spawnspot(str classname, int spottid, [int tid], [int angle])", result);
    }

    [Fact]
    public void ApplyParameterNames_NoResearchedDocForThisName_ReturnsTheSignatureUnchanged()
    {
        const string signature = "function int Foo(int, int)";
        Assert.Equal(signature, BcsFunctionDocs.ApplyParameterNames(signature, "ThisIsNotARealAcsFunction"));
    }

    /// <summary>A real doc exists, but its own parameter count doesn't match this particular signature's - never guess a name onto the wrong position.</summary>
    [Fact]
    public void ApplyParameterNames_ParameterCountMismatch_ReturnsTheSignatureUnchanged()
    {
        const string signature = "function int Thing_Activate(int, int, int)"; // Thing_Activate really only takes one
        Assert.Equal(signature, BcsFunctionDocs.ApplyParameterNames(signature, "Thing_Activate"));
    }

    [Fact]
    public void ApplyParameterNames_ZeroParameterFunction_ReturnsTheSignatureUnchanged()
    {
        const string signature = "int GetScreenWidth()";
        Assert.Equal(signature, BcsFunctionDocs.ApplyParameterNames(signature, "GetScreenWidth"));
    }
}
