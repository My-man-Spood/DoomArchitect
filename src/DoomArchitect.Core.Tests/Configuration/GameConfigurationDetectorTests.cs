using System.Numerics;
using DoomArchitect.Core.Configuration;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Tests.Configuration;

public class GameConfigurationDetectorTests
{
    [Fact]
    public void Detect_ExMyMapName_SuggestsDoom()
    {
        var map = new MapData();

        var result = GameConfigurationDetector.Detect(map, "E1M1", wadFileName: "DOOM2.WAD");

        // The map-name pattern outranks even a contradicting filename hint.
        Assert.Equal(GameConfigurationKind.Doom, result);
    }

    [Fact]
    public void Detect_MapxyMapName_SuggestsDoom2()
    {
        var map = new MapData();

        var result = GameConfigurationDetector.Detect(map, "MAP01", wadFileName: "DOOM.WAD");

        Assert.Equal(GameConfigurationKind.Doom2, result);
    }

    [Fact]
    public void Detect_AmbiguousNameWithADoom2ExclusiveThing_SuggestsDoom2()
    {
        var map = new MapData();
        map.CreateThing(Vector2.Zero, type: 64); // Arch-vile - only defined in Doom2's table

        var result = GameConfigurationDetector.Detect(map, "MYMAP", wadFileName: null);

        Assert.Equal(GameConfigurationKind.Doom2, result);
    }

    [Fact]
    public void Detect_AmbiguousNameWithOnlySharedThings_FallsBackToFilenameHint()
    {
        var map = new MapData();
        map.CreateThing(Vector2.Zero, type: 3004); // Zombieman - defined in both

        var result = GameConfigurationDetector.Detect(map, "MYMAP", wadFileName: "somelevel2.wad");

        Assert.Equal(GameConfigurationKind.Doom2, result);
    }

    [Fact]
    public void Detect_NoSignalsAtAll_DefaultsToDoom()
    {
        var map = new MapData();

        var result = GameConfigurationDetector.Detect(map, "MYMAP", wadFileName: "mymap.wad");

        Assert.Equal(GameConfigurationKind.Doom, result);
    }
}
