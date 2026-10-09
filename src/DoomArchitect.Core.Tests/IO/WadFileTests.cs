using DoomArchitect.Core.IO;

namespace DoomArchitect.Core.Tests.IO;

public class WadFileTests
{
    [Fact]
    public void Read_ValidWad_ParsesLumpsInOrderWithCorrectData()
    {
        var bytes = WadTestBuilder.Build(
            ("MAP01", Array.Empty<byte>()),
            ("TEXTMAP", WadTestBuilder.TextLump("namespace = \"doom\";")),
            ("ENDMAP", Array.Empty<byte>()));

        var wad = WadFile.Read(new MemoryStream(bytes));

        Assert.Equal(3, wad.Lumps.Count);
        Assert.Equal("MAP01", wad.Lumps[0].Name);
        Assert.Equal("TEXTMAP", wad.Lumps[1].Name);
        Assert.Equal("namespace = \"doom\";", System.Text.Encoding.ASCII.GetString(wad.Lumps[1].Data));
        Assert.Equal("ENDMAP", wad.Lumps[2].Name);
    }

    [Fact]
    public void FindByPath_BareLumpName_DelegatesToFindLump()
    {
        // A WAD has no real path hierarchy - a ZScript #include inside one
        // just references another lump directly by name.
        var bytes = WadTestBuilder.Build(("ZSCRIPT2", WadTestBuilder.TextLump("class Actor {}")));
        var wad = WadFile.Read(new MemoryStream(bytes));

        var found = wad.FindByPath("ZSCRIPT2");

        Assert.Equal("class Actor {}", System.Text.Encoding.ASCII.GetString(found!));
    }

    [Fact]
    public void FindByPath_PathLikeIncludeString_StripsToTheBareLumpName()
    {
        var bytes = WadTestBuilder.Build(("ZSCRIPT2", WadTestBuilder.TextLump("class Actor {}")));
        var wad = WadFile.Read(new MemoryStream(bytes));

        // A nested path makes no sense for a WAD, but shouldn't crash -
        // just strip it down to a bare title and try that.
        var found = wad.FindByPath("some/path/ZSCRIPT2.txt");

        Assert.Equal("class Actor {}", System.Text.Encoding.ASCII.GetString(found!));
    }

    [Fact]
    public void Read_InvalidIdentification_Throws()
    {
        var bytes = new byte[] { (byte)'X', (byte)'X', (byte)'X', (byte)'X', 0, 0, 0, 0, 0, 0, 0, 0 };

        Assert.Throws<InvalidDataException>(() => WadFile.Read(new MemoryStream(bytes)));
    }

    [Fact]
    public void FindLump_MatchesCaseInsensitively()
    {
        var bytes = WadTestBuilder.Build(("VERTEXES", new byte[] { 1, 2, 3 }));

        var wad = WadFile.Read(new MemoryStream(bytes));

        Assert.NotNull(wad.FindLump("vertexes"));
    }

    [Fact]
    public void FindLump_Missing_ReturnsNull()
    {
        var bytes = WadTestBuilder.Build(("VERTEXES", Array.Empty<byte>()));

        var wad = WadFile.Read(new MemoryStream(bytes));

        Assert.Null(wad.FindLump("SECTORS"));
    }

    [Fact]
    public void ReadMapTextMap_TextMapImmediatelyAfterMarker_ReturnsItsText()
    {
        var bytes = WadTestBuilder.Build(
            ("MAP01", Array.Empty<byte>()),
            ("TEXTMAP", WadTestBuilder.TextLump("namespace = \"doom\";")),
            ("ENDMAP", Array.Empty<byte>()));

        var wad = WadFile.Read(new MemoryStream(bytes));

        Assert.Equal("namespace = \"doom\";", wad.ReadMapTextMap("MAP01"));
    }

    [Fact]
    public void ReadMapTextMap_MapNotFound_Throws()
    {
        var bytes = WadTestBuilder.Build(("MAP01", Array.Empty<byte>()));

        var wad = WadFile.Read(new MemoryStream(bytes));

        Assert.Throws<KeyNotFoundException>(() => wad.ReadMapTextMap("MAP02"));
    }

    [Fact]
    public void FindUdmfMapNames_ReturnsOnlyMapsFollowedByTextMap()
    {
        var bytes = WadTestBuilder.Build(
            ("MAP01", Array.Empty<byte>()),
            ("TEXTMAP", WadTestBuilder.TextLump("namespace = \"doom\";")),
            ("ENDMAP", Array.Empty<byte>()),
            ("E1M1", Array.Empty<byte>()),
            ("THINGS", Array.Empty<byte>()));

        var wad = WadFile.Read(new MemoryStream(bytes));

        Assert.Equal(new[] { "MAP01" }, wad.FindUdmfMapNames());
    }

    [Fact]
    public void FindLumpsBetweenMarkers_ReturnsEverythingStrictlyBetweenTheMarkers()
    {
        var bytes = WadTestBuilder.Build(
            ("OTHER", Array.Empty<byte>()),
            ("S_START", Array.Empty<byte>()),
            ("POSSA1", new byte[] { 1 }),
            ("TROOA1", new byte[] { 2 }),
            ("S_END", Array.Empty<byte>()),
            ("AFTER", Array.Empty<byte>()));

        var wad = WadFile.Read(new MemoryStream(bytes));

        var sprites = wad.FindLumpsBetweenMarkers("S_START", "S_END");

        Assert.Equal(new[] { "POSSA1", "TROOA1" }, sprites.Select(l => l.Name));
    }

    [Fact]
    public void FindLumpsBetweenMarkers_MissingStartMarker_ReturnsEmpty()
    {
        var bytes = WadTestBuilder.Build(("POSSA1", Array.Empty<byte>()), ("S_END", Array.Empty<byte>()));

        var wad = WadFile.Read(new MemoryStream(bytes));

        Assert.Empty(wad.FindLumpsBetweenMarkers("S_START", "S_END"));
    }

    [Fact]
    public void FindLumpsBetweenMarkers_MissingEndMarker_ReturnsEmpty()
    {
        var bytes = WadTestBuilder.Build(("S_START", Array.Empty<byte>()), ("POSSA1", Array.Empty<byte>()));

        var wad = WadFile.Read(new MemoryStream(bytes));

        Assert.Empty(wad.FindLumpsBetweenMarkers("S_START", "S_END"));
    }

    [Fact]
    public void FindLumpsBetweenMarkers_AdjacentMarkersWithNothingBetween_ReturnsEmpty()
    {
        var bytes = WadTestBuilder.Build(("S_START", Array.Empty<byte>()), ("S_END", Array.Empty<byte>()));

        var wad = WadFile.Read(new MemoryStream(bytes));

        Assert.Empty(wad.FindLumpsBetweenMarkers("S_START", "S_END"));
    }

    [Fact]
    public void ReadMapTextMap_ClassicBinaryFormatMap_ThrowsNotSupported()
    {
        // A pre-UDMF map's marker is immediately followed by THINGS, not
        // TEXTMAP - exactly what real id Software WADs look like.
        var bytes = WadTestBuilder.Build(
            ("E1M1", Array.Empty<byte>()),
            ("THINGS", new byte[] { 1, 2, 3 }),
            ("LINEDEFS", Array.Empty<byte>()));

        var wad = WadFile.Read(new MemoryStream(bytes));

        Assert.Throws<NotSupportedException>(() => wad.ReadMapTextMap("E1M1"));
    }

    [Fact]
    public void FindMapLumpGroups_UdmfMap_StopsRightAfterEndmap()
    {
        var bytes = WadTestBuilder.Build(
            ("MAP01", Array.Empty<byte>()),
            ("TEXTMAP", WadTestBuilder.TextLump("namespace = \"doom\";")),
            ("BEHAVIOR", new byte[] { 1 }),
            ("ENDMAP", Array.Empty<byte>()),
            ("AFTER", Array.Empty<byte>()));

        var wad = WadFile.Read(new MemoryStream(bytes));
        var groups = wad.FindMapLumpGroups();

        var group = Assert.Single(groups);
        Assert.Equal("MAP01", group.MarkerName);
        Assert.True(group.IsUdmf);
        Assert.Equal(new[] { "TEXTMAP", "BEHAVIOR", "ENDMAP" }, group.Lumps.Select(l => l.Name));
    }

    [Fact]
    public void FindMapLumpGroups_ClassicMap_StopsAtFirstUnrecognizedLump()
    {
        var bytes = WadTestBuilder.Build(
            ("E1M1", Array.Empty<byte>()),
            ("THINGS", new byte[] { 1 }),
            ("LINEDEFS", new byte[] { 2 }),
            ("VERTEXES", new byte[] { 3 }),
            ("AFTER", Array.Empty<byte>()));

        var wad = WadFile.Read(new MemoryStream(bytes));
        var groups = wad.FindMapLumpGroups();

        var group = Assert.Single(groups);
        Assert.Equal("E1M1", group.MarkerName);
        Assert.False(group.IsUdmf);
        Assert.Equal(new[] { "THINGS", "LINEDEFS", "VERTEXES" }, group.Lumps.Select(l => l.Name));
    }

    [Fact]
    public void FindMapLumpGroups_MultipleMaps_EachGroupCorrectlyBounded()
    {
        // Decisive proof the classic group's own scan is bounded, not an unbounded search for known names across the whole WAD - E1M2's own THINGS must not leak into E1M1's group.
        var bytes = WadTestBuilder.Build(
            ("E1M1", Array.Empty<byte>()),
            ("THINGS", new byte[] { 1 }),
            ("E1M2", Array.Empty<byte>()),
            ("THINGS", new byte[] { 2 }),
            ("LINEDEFS", new byte[] { 3 }));

        var wad = WadFile.Read(new MemoryStream(bytes));
        var groups = wad.FindMapLumpGroups();

        Assert.Equal(2, groups.Count);
        Assert.Equal("E1M1", groups[0].MarkerName);
        Assert.Equal(new[] { "THINGS" }, groups[0].Lumps.Select(l => l.Name));
        Assert.Equal("E1M2", groups[1].MarkerName);
        Assert.Equal(new[] { "THINGS", "LINEDEFS" }, groups[1].Lumps.Select(l => l.Name));
    }

    [Fact]
    public void FindMapLumpGroups_LumpsNotPartOfAnyMap_AreSkippedNotMisidentified()
    {
        var bytes = WadTestBuilder.Build(
            ("PLAYPAL", new byte[] { 1 }),
            ("MAP01", Array.Empty<byte>()),
            ("TEXTMAP", WadTestBuilder.TextLump("namespace = \"doom\";")),
            ("ENDMAP", Array.Empty<byte>()));

        var wad = WadFile.Read(new MemoryStream(bytes));
        var groups = wad.FindMapLumpGroups();

        var group = Assert.Single(groups);
        Assert.Equal("MAP01", group.MarkerName);
    }

    [Fact]
    public void FindMapLumpGroups_NoMaps_ReturnsEmpty()
    {
        var bytes = WadTestBuilder.Build(("PLAYPAL", new byte[] { 1 }), ("COLORMAP", new byte[] { 2 }));

        var wad = WadFile.Read(new MemoryStream(bytes));

        Assert.Empty(wad.FindMapLumpGroups());
    }

    [Fact]
    public void BuildTree_MapsBecomeGroupsAndLooseLumpsStayFlat_InOriginalOrder()
    {
        var bytes = WadTestBuilder.Build(
            ("PLAYPAL", new byte[] { 1 }),
            ("MAP01", Array.Empty<byte>()),
            ("TEXTMAP", WadTestBuilder.TextLump("namespace = \"doom\";")),
            ("ENDMAP", Array.Empty<byte>()),
            ("COLORMAP", new byte[] { 2 }));

        var wad = WadFile.Read(new MemoryStream(bytes));
        var tree = wad.BuildTree("my.wad");

        Assert.Equal("my.wad", tree.DisplayName);
        Assert.Equal(ResourceTreeNodeKind.WadContainer, tree.Kind);
        Assert.Equal(new[] { "PLAYPAL", "MAP01", "COLORMAP" }, tree.Children.Select(c => c.DisplayName));
        Assert.Equal(ResourceTreeNodeKind.Lump, tree.Children[0].Kind);

        var mapNode = tree.Children[1];
        Assert.Equal(ResourceTreeNodeKind.MapGroup, mapNode.Kind);
        Assert.Equal(new[] { "TEXTMAP", "ENDMAP" }, mapNode.Children.Select(c => c.DisplayName));
        Assert.All(mapNode.Children, c => Assert.Equal(ResourceTreeNodeKind.Lump, c.Kind));
    }

    [Fact]
    public void BuildTree_LumpIndex_MatchesEachNodesRealPositionInLumps()
    {
        var bytes = WadTestBuilder.Build(
            ("PLAYPAL", new byte[] { 1 }),
            ("MAP01", Array.Empty<byte>()),
            ("TEXTMAP", WadTestBuilder.TextLump("namespace = \"doom\";")),
            ("ENDMAP", Array.Empty<byte>()),
            ("COLORMAP", new byte[] { 2 }));

        var wad = WadFile.Read(new MemoryStream(bytes));
        var tree = wad.BuildTree("my.wad");

        Assert.Equal(0, tree.Children[0].LumpIndex); // PLAYPAL
        Assert.Equal(1, tree.Children[1].LumpIndex); // MAP01 marker
        Assert.Equal(2, tree.Children[1].Children[0].LumpIndex); // TEXTMAP
        Assert.Equal(3, tree.Children[1].Children[1].LumpIndex); // ENDMAP
        Assert.Equal(4, tree.Children[2].LumpIndex); // COLORMAP
    }

    [Fact]
    public void WithReplacedLumpData_ReplacesOnlyTheTargetIndex()
    {
        var bytes = WadTestBuilder.Build(
            ("SCRIPTS", new byte[] { 1 }),
            ("BEHAVIOR", new byte[] { 2 }));
        var wad = WadFile.Read(new MemoryStream(bytes));

        var result = WadFile.WithReplacedLumpData(wad.Lumps, 0, new byte[] { 9, 9 });

        Assert.Equal(2, result.Count);
        Assert.Equal("SCRIPTS", result[0].Name);
        Assert.Equal(new byte[] { 9, 9 }, result[0].Data);
        Assert.Equal("BEHAVIOR", result[1].Name);
        Assert.Equal(new byte[] { 2 }, result[1].Data);
    }

    /// <summary>The whole reason lump identity needs an index, not just a name - a Hexen-format WAD can have more than one map, each with its own SCRIPTS lump sharing the same name.</summary>
    [Fact]
    public void WithReplacedLumpData_SameNamedLumpAtDifferentIndices_TargetsExactIndexOnly()
    {
        var bytes = WadTestBuilder.Build(
            ("MAP01", Array.Empty<byte>()),
            ("SCRIPTS", new byte[] { 1 }),
            ("MAP02", Array.Empty<byte>()),
            ("SCRIPTS", new byte[] { 2 }));
        var wad = WadFile.Read(new MemoryStream(bytes));

        var result = WadFile.WithReplacedLumpData(wad.Lumps, 3, new byte[] { 99 });

        Assert.Equal(new byte[] { 1 }, result[1].Data);
        Assert.Equal(new byte[] { 99 }, result[3].Data);
    }

    [Fact]
    public void WithAddedScriptsLump_ClassicFormatMap_InsertsAsTheLastLumpOfItsGroup()
    {
        var bytes = WadTestBuilder.Build(
            ("MAP01", Array.Empty<byte>()),
            ("THINGS", Array.Empty<byte>()),
            ("LINEDEFS", Array.Empty<byte>()),
            ("SIDEDEFS", Array.Empty<byte>()),
            ("VERTEXES", Array.Empty<byte>()),
            ("BEHAVIOR", new byte[] { 1 }));
        var wad = WadFile.Read(new MemoryStream(bytes));

        var (result, insertedIndex) = WadFile.WithAddedScriptsLump(wad.Lumps, 0, new byte[] { 9 });

        Assert.Equal(6, insertedIndex);
        Assert.Equal(7, result.Count);
        Assert.Equal("SCRIPTS", result[6].Name);
        Assert.Equal(new byte[] { 9 }, result[6].Data);
        Assert.Equal("BEHAVIOR", result[5].Name);
    }

    [Fact]
    public void WithAddedScriptsLump_UdmfFormatMap_InsertsBeforeEndmap()
    {
        var bytes = WadTestBuilder.Build(
            ("MAP01", Array.Empty<byte>()),
            ("TEXTMAP", Array.Empty<byte>()),
            ("ZNODES", Array.Empty<byte>()),
            ("BLOCKMAP", Array.Empty<byte>()),
            ("ENDMAP", Array.Empty<byte>()));
        var wad = WadFile.Read(new MemoryStream(bytes));

        var (result, insertedIndex) = WadFile.WithAddedScriptsLump(wad.Lumps, 0, new byte[] { 9 });

        Assert.Equal(4, insertedIndex);
        Assert.Equal(6, result.Count);
        Assert.Equal("SCRIPTS", result[4].Name);
        Assert.Equal("ENDMAP", result[5].Name);
    }

    [Fact]
    public void WithAddedScriptsLump_ReturnsTheCorrectIndex_AndLeavesEveryOtherLumpUntouched()
    {
        var bytes = WadTestBuilder.Build(
            ("MAP01", Array.Empty<byte>()),
            ("THINGS", new byte[] { 1 }),
            ("BEHAVIOR", new byte[] { 2 }));
        var wad = WadFile.Read(new MemoryStream(bytes));

        var (result, insertedIndex) = WadFile.WithAddedScriptsLump(wad.Lumps, 0, new byte[] { 9 });

        Assert.Equal(3, insertedIndex);
        Assert.Equal("MAP01", result[0].Name);
        Assert.Equal("THINGS", result[1].Name);
        Assert.Equal(new byte[] { 1 }, result[1].Data);
        Assert.Equal("BEHAVIOR", result[2].Name);
        Assert.Equal(new byte[] { 2 }, result[2].Data);
        Assert.Equal("SCRIPTS", result[3].Name);
        Assert.Equal(new byte[] { 9 }, result[3].Data);
    }

    /// <summary>A second map elsewhere in the same WAD must be left completely alone, besides its own lumps shifting by exactly one index - the whole reason the method takes a marker index, not just a lump list.</summary>
    [Fact]
    public void WithAddedScriptsLump_MultiMapWad_OnlyAffectsTheTargetMapsOwnGroup()
    {
        var bytes = WadTestBuilder.Build(
            ("MAP01", Array.Empty<byte>()),
            ("THINGS", Array.Empty<byte>()),
            ("BEHAVIOR", Array.Empty<byte>()),
            ("MAP02", Array.Empty<byte>()),
            ("THINGS", new byte[] { 7 }),
            ("BEHAVIOR", new byte[] { 8 }));
        var wad = WadFile.Read(new MemoryStream(bytes));

        var (result, insertedIndex) = WadFile.WithAddedScriptsLump(wad.Lumps, 0, new byte[] { 9 });

        Assert.Equal(3, insertedIndex);
        Assert.Equal(7, result.Count);
        Assert.Equal("MAP02", result[4].Name);
        Assert.Equal("THINGS", result[5].Name);
        Assert.Equal(new byte[] { 7 }, result[5].Data);
        Assert.Equal("BEHAVIOR", result[6].Name);
        Assert.Equal(new byte[] { 8 }, result[6].Data);
    }

    [Fact]
    public void FindScriptsLumpIndex_GroupHasOne_ReturnsItsIndex()
    {
        var bytes = WadTestBuilder.Build(
            ("MAP01", Array.Empty<byte>()),
            ("TEXTMAP", Array.Empty<byte>()),
            ("SCRIPTS", Array.Empty<byte>()),
            ("ENDMAP", Array.Empty<byte>()));
        var wad = WadFile.Read(new MemoryStream(bytes));

        Assert.Equal(2, WadFile.FindScriptsLumpIndex(wad.Lumps, 0));
    }

    [Fact]
    public void FindScriptsLumpIndex_GroupHasNone_ReturnsMinusOne()
    {
        var bytes = WadTestBuilder.Build(
            ("MAP01", Array.Empty<byte>()),
            ("TEXTMAP", Array.Empty<byte>()),
            ("ENDMAP", Array.Empty<byte>()));
        var wad = WadFile.Read(new MemoryStream(bytes));

        Assert.Equal(-1, WadFile.FindScriptsLumpIndex(wad.Lumps, 0));
    }

    [Fact]
    public void WithSetBehaviorLump_UdmfFormatMap_NoExistingBehavior_InsertsBeforeEndmap()
    {
        var bytes = WadTestBuilder.Build(
            ("MAP01", Array.Empty<byte>()),
            ("TEXTMAP", Array.Empty<byte>()),
            ("SCRIPTS", Array.Empty<byte>()),
            ("ENDMAP", Array.Empty<byte>()));
        var wad = WadFile.Read(new MemoryStream(bytes));

        var (result, behaviorIndex) = WadFile.WithSetBehaviorLump(wad.Lumps, 0, new byte[] { 9 });

        Assert.Equal(3, behaviorIndex);
        Assert.Equal(5, result.Count);
        Assert.Equal("BEHAVIOR", result[3].Name);
        Assert.Equal(new byte[] { 9 }, result[3].Data);
        Assert.Equal("ENDMAP", result[4].Name);
    }

    [Fact]
    public void WithSetBehaviorLump_ClassicFormatMap_NoExistingBehavior_InsertsAtGroupEnd()
    {
        var bytes = WadTestBuilder.Build(
            ("MAP01", Array.Empty<byte>()),
            ("THINGS", Array.Empty<byte>()),
            ("SCRIPTS", Array.Empty<byte>()));
        var wad = WadFile.Read(new MemoryStream(bytes));

        var (result, behaviorIndex) = WadFile.WithSetBehaviorLump(wad.Lumps, 0, new byte[] { 9 });

        Assert.Equal(3, behaviorIndex);
        Assert.Equal(4, result.Count);
        Assert.Equal("BEHAVIOR", result[3].Name);
        Assert.Equal(new byte[] { 9 }, result[3].Data);
    }

    [Fact]
    public void WithSetBehaviorLump_ExistingBehaviorLump_ReplacesItsDataInPlace()
    {
        var bytes = WadTestBuilder.Build(
            ("MAP01", Array.Empty<byte>()),
            ("TEXTMAP", Array.Empty<byte>()),
            ("BEHAVIOR", new byte[] { 1 }),
            ("SCRIPTS", Array.Empty<byte>()),
            ("ENDMAP", Array.Empty<byte>()));
        var wad = WadFile.Read(new MemoryStream(bytes));

        var (result, behaviorIndex) = WadFile.WithSetBehaviorLump(wad.Lumps, 0, new byte[] { 9 });

        Assert.Equal(2, behaviorIndex);
        Assert.Equal(5, result.Count);
        Assert.Equal("BEHAVIOR", result[2].Name);
        Assert.Equal(new byte[] { 9 }, result[2].Data);
        Assert.Equal("SCRIPTS", result[3].Name);
    }
}
