using DoomArchitect.Core.Configuration;

namespace DoomArchitect.Core.Tests.Configuration;

public class CfgWriterTests
{
    [Fact]
    public void Write_ThenReparse_RoundTripsScalarValues()
    {
        var document = CfgBlock.Empty()
            .WithAssignment("intkey", CfgValue.OfInt(5))
            .WithAssignment("strkey", CfgValue.OfString("hello \"world\""))
            .WithAssignment("boolkey", CfgValue.OfBool(true));

        var text = CfgWriter.Write(document);
        var reparsed = CfgLoader.Parse(text);

        Assert.Equal(5L, reparsed.Find("intkey")!.Value.AsLong());
        Assert.Equal("hello \"world\"", reparsed.Find("strkey")!.Value.AsString());
        Assert.True(reparsed.Find("boolkey")!.Value.AsBool());
    }

    [Fact]
    public void Write_NestedBlock_RoundTrips()
    {
        var inner = CfgBlock.Empty("inner").WithAssignment("a", CfgValue.OfInt(1));
        var document = CfgBlock.Empty().WithBlock("outer", inner);

        var text = CfgWriter.Write(document);
        var reparsed = CfgLoader.Parse(text);

        Assert.Equal(1L, reparsed.FindBlock("outer")!.Find("a")!.Value.AsLong());
    }

    [Fact]
    public void Write_StringWithBackslashAndNewline_EscapesThem()
    {
        var document = CfgBlock.Empty().WithAssignment("path", CfgValue.OfString("C:\\a\nb"));

        var text = CfgWriter.Write(document);

        Assert.Contains("\"C:\\\\a\\nb\"", text);
    }

    [Fact]
    public void Write_BoolAndNumber_UseBareLiterals()
    {
        var document = CfgBlock.Empty()
            .WithAssignment("flag", CfgValue.OfBool(false))
            .WithAssignment("num", CfgValue.OfInt(42));

        var text = CfgWriter.Write(document);

        Assert.Contains("flag = false;", text);
        Assert.Contains("num = 42;", text);
    }
}
