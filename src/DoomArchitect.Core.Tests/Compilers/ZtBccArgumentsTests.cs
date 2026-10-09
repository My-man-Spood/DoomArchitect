using DoomArchitect.Core.Compilers;

namespace DoomArchitect.Core.Tests.Compilers;

public class ZtBccArgumentsTests
{
    [Fact]
    public void Build_NoIncludeDirs_IsJustInputThenOutput()
    {
        var args = ZtBccArguments.Build("/tmp/in.bcs", "/tmp/out.o", Array.Empty<string>());

        Assert.Equal(new[] { "/tmp/in.bcs", "/tmp/out.o" }, args);
    }

    [Fact]
    public void Build_WithIncludeDirs_AddsAMinusIFlagPerDir_BeforeInputAndOutput()
    {
        var args = ZtBccArguments.Build("/tmp/in.bcs", "/tmp/out.o", new[] { "/tmp/a", "/tmp/b" });

        Assert.Equal(new[] { "-i", "/tmp/a", "-i", "/tmp/b", "/tmp/in.bcs", "/tmp/out.o" }, args);
    }
}
