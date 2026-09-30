using System.Text;
using DoomArchitect.Core.ZDoom;

namespace DoomArchitect.Core.Tests.ZDoom;

public class MapinfoParserTests
{
    private static bool Parse(MapinfoParser parser, string source, string sourceName = "MAPINFO") =>
        parser.Parse(Encoding.ASCII.GetBytes(source), sourceName);

    [Fact]
    public void Parse_DoomEdNumsBlock_ReadsEntries()
    {
        var parser = new MapinfoParser();
        var ok = Parse(parser, """
            DoomEdNums
            {
                5000 = MyZScriptActor
                5001 = "MyOtherActor"
            }
            """);

        Assert.True(ok);
        Assert.Equal("myzscriptactor", parser.DoomEdNums[5000]);
        Assert.Equal("myotheractor", parser.DoomEdNums[5001]);
    }

    [Fact]
    public void Parse_DoomEdNumsWithSpecialAndArgs_SkipsThemButStillReadsTheClass()
    {
        var parser = new MapinfoParser();
        var ok = Parse(parser, """
            DoomEdNums
            {
                5000 = MyZScriptActor, 80, 1, 2, 3
            }
            """);

        Assert.True(ok);
        Assert.Equal("myzscriptactor", parser.DoomEdNums[5000]);
    }

    [Fact]
    public void Parse_ZeroEntry_IsNotAdded()
    {
        var parser = new MapinfoParser();
        var ok = Parse(parser, "DoomEdNums { 0 = SomeActor }");

        Assert.True(ok);
        Assert.False(parser.DoomEdNums.ContainsKey(0));
    }

    [Fact]
    public void Parse_UnrelatedTopLevelBlocks_AreSkippedWithoutError()
    {
        var parser = new MapinfoParser();
        var ok = Parse(parser, """
            gameinfo
            {
                skyflatname = "F_SKY1"
            }
            map MAP01 "Entryway"
            {
                sky1 = "SKY1", 0
            }
            DoomEdNums
            {
                5000 = MyZScriptActor
            }
            """);

        Assert.True(ok);
        Assert.Equal("myzscriptactor", parser.DoomEdNums[5000]);
    }

    [Fact]
    public void Parse_Include_ResolvesViaOnIncludeDelegate()
    {
        var parser = new MapinfoParser
        {
            OnInclude = filename => filename == "extra.txt" ? Encoding.ASCII.GetBytes("DoomEdNums { 5001 = FromInclude }") : null,
        };

        var ok = Parse(parser, "include \"extra.txt\"\nDoomEdNums { 5000 = FromMainFile }");

        Assert.True(ok);
        Assert.Equal("frominclude", parser.DoomEdNums[5001]);
        Assert.Equal("frommainfile", parser.DoomEdNums[5000]);
    }

    [Fact]
    public void Parse_MalformedEntryNumber_ReportsAnError()
    {
        var parser = new MapinfoParser();
        var ok = Parse(parser, "DoomEdNums { NotANumber = MyActor }");

        Assert.False(ok);
        Assert.True(parser.HasError);
    }
}
