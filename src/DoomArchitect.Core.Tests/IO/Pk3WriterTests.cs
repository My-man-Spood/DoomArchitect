using DoomArchitect.Core.IO;

namespace DoomArchitect.Core.Tests.IO;

public class Pk3WriterTests
{
    [Fact]
    public void Write_ThenReopen_RoundTrips()
    {
        var entries = new (string Path, byte[] Data)[]
        {
            ("SCRIPTS", "script source"u8.ToArray()),
            ("flats/MYFLAT.png", new byte[] { 1, 2, 3 }),
        };

        var bytes = Pk3Writer.Write(entries);
        var pk3 = Pk3File.Open(new MemoryStream(bytes));

        Assert.Equal("script source"u8.ToArray(), pk3.FindByPath("SCRIPTS"));
        Assert.Equal(new byte[] { 1, 2, 3 }, pk3.FindByPath("flats/MYFLAT.png"));
    }

    [Fact]
    public void WithReplacedEntry_ThenWrite_ThenReopen_ReflectsTheReplacementAndPreservesSiblings()
    {
        var original = Pk3TestBuilder.Build(
            ("SCRIPTS", "old script"u8.ToArray()),
            ("zscript.zs", "class Actor {}"u8.ToArray()));

        var replaced = original.WithReplacedEntry("SCRIPTS", "new script"u8.ToArray());
        var bytes = Pk3Writer.Write(replaced);
        var reopened = Pk3File.Open(new MemoryStream(bytes));

        Assert.Equal("new script"u8.ToArray(), reopened.FindByPath("SCRIPTS"));
        Assert.Equal("class Actor {}"u8.ToArray(), reopened.FindByPath("zscript.zs"));
    }
}
