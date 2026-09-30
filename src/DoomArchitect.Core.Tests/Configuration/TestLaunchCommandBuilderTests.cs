using DoomArchitect.Core.Configuration;

namespace DoomArchitect.Core.Tests.Configuration;

public class TestLaunchCommandBuilderTests
{
    [Theory]
    [InlineData("MAP01", "1", "")]
    [InlineData("MAP12", "12", "")]
    [InlineData("E1M2", "1", "2")]
    [InlineData("E3M9", "3", "9")]
    [InlineData("NOMAPNAME", "", "")]
    // A third digit run doesn't get its own slot - it just overwrites L2
    // again, matching UDB's own real "if(first) l1 else l2" loop with no
    // early exit after two runs (Launcher.cs).
    [InlineData("MAP1EXTRA2MORE3", "1", "3")]
    public void SplitDigitRuns_MatchesUdbsRealDigitRunScanner(string mapName, string expectedL1, string expectedL2)
    {
        var (l1, l2) = TestLaunchCommandBuilder.SplitDigitRuns(mapName);

        Assert.Equal(expectedL1, l1);
        Assert.Equal(expectedL2, l2);
    }

    private static readonly string[] NoResources = Array.Empty<string>();

    [Fact]
    public void Build_Doom2StyleTemplate_ConcatenatesL1AndL2WithNoSpace()
    {
        var args = TestLaunchCommandBuilder.Build(
            "-iwad \"%WP\" -skill \"%S\" -file \"%AP\" \"%F\" -warp %L1%L2 %NM",
            tempWadPath: "/tmp/test.wad", iwadPath: "/iwads/doom2.wad", additionalResourcePaths: NoResources,
            mapName: "MAP01", skill: 3, noMonsters: false);

        Assert.Equal(new[] { "-iwad", "/iwads/doom2.wad", "-skill", "3", "-file", "/tmp/test.wad", "-warp", "1" }, args);
    }

    [Fact]
    public void Build_DoomStyleTemplate_KeepsL1AndL2AsSeparateArguments()
    {
        var args = TestLaunchCommandBuilder.Build(
            "-iwad \"%WP\" -skill \"%S\" -file \"%AP\" \"%F\" -warp %L1 %L2 %NM",
            tempWadPath: "/tmp/test.wad", iwadPath: "/iwads/doom.wad", additionalResourcePaths: NoResources,
            mapName: "E1M2", skill: 4, noMonsters: false);

        Assert.Equal(new[] { "-iwad", "/iwads/doom.wad", "-skill", "4", "-file", "/tmp/test.wad", "-warp", "1", "2" }, args);
    }

    [Fact]
    public void Build_ModernTemplate_UsesMapCommandWithFullMapName()
    {
        var args = TestLaunchCommandBuilder.Build(
            "-iwad \"%WP\" -skill \"%S\" -file \"%AP\" \"%F\" +map %L %NM",
            tempWadPath: "/tmp/test.wad", iwadPath: "/iwads/doom2.wad", additionalResourcePaths: NoResources,
            mapName: "MAP01", skill: 3, noMonsters: false);

        Assert.Equal(new[] { "-iwad", "/iwads/doom2.wad", "-skill", "3", "-file", "/tmp/test.wad", "+map", "MAP01" }, args);
    }

    [Fact]
    public void Build_NoMonsters_AppendsTheFlag()
    {
        var args = TestLaunchCommandBuilder.Build(
            "-iwad \"%WP\" -skill \"%S\" -file \"%AP\" \"%F\" +map %L %NM",
            tempWadPath: "/tmp/test.wad", iwadPath: "/iwads/doom2.wad", additionalResourcePaths: NoResources,
            mapName: "MAP01", skill: 1, noMonsters: true);

        Assert.Equal("-nomonsters", args[^1]);
    }

    /// <summary>Each resource lands as its own real argument-array entry - the whole reason this doesn't need UDB's own per-file quoting.</summary>
    [Fact]
    public void Build_MultipleAdditionalResources_EachBecomesItsOwnArgument()
    {
        var args = TestLaunchCommandBuilder.Build(
            "-iwad \"%WP\" -file \"%AP\" \"%F\"",
            tempWadPath: "/tmp/test.wad", iwadPath: "/iwads/doom2.wad",
            additionalResourcePaths: new[] { "/res/textures.pk3", "/res/with a space.pk3" },
            mapName: "MAP01", skill: 3, noMonsters: false);

        Assert.Equal(new[] { "-iwad", "/iwads/doom2.wad", "-file", "/res/textures.pk3", "/res/with a space.pk3", "/tmp/test.wad" }, args);
    }

    /// <summary>Zero additional resources contributes zero arguments - "-file" naturally continues directly against the temp WAD's own path, matching what the template actually intends (every "-file" argument, including the map itself, is meant to load together).</summary>
    [Fact]
    public void Build_NoAdditionalResources_FileFlagAppliesDirectlyToTheTempWad()
    {
        var args = TestLaunchCommandBuilder.Build(
            "-iwad \"%WP\" -file \"%AP\" \"%F\"",
            tempWadPath: "/tmp/test.wad", iwadPath: "/iwads/doom2.wad", additionalResourcePaths: NoResources,
            mapName: "MAP01", skill: 3, noMonsters: false);

        Assert.Equal(new[] { "-iwad", "/iwads/doom2.wad", "-file", "/tmp/test.wad" }, args);
    }

    [Fact]
    public void Build_WfPlaceholder_ResolvesToTheIwadFileNameOnly()
    {
        var args = TestLaunchCommandBuilder.Build(
            "-iwad %WF",
            tempWadPath: "/tmp/test.wad", iwadPath: "/some/dir/doom2.wad", additionalResourcePaths: NoResources,
            mapName: "MAP01", skill: 3, noMonsters: false);

        Assert.Equal(new[] { "-iwad", "doom2.wad" }, args);
    }
}
