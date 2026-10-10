using DoomArchitect.Core.Compilers;

namespace DoomArchitect.Core.Tests.Compilers;

public class ZdbspArgumentsTests
{
    /// <summary>Confirmed from Ultimate Doom Builder's own real "zdbsp_udmf_normal" profile ("-c -X -o%FO %FI") - the Save-purpose flag.</summary>
    [Fact]
    public void Build_NotForTesting_UsesTheRealSaveProfileFlags()
    {
        var args = ZdbspArguments.Build("/tmp/in.wad", "/tmp/out.wad", "MAP01", forTesting: false);

        Assert.Equal(new[] { "-c", "-X", "-m", "MAP01", "-o", "/tmp/out.wad", "/tmp/in.wad" }, args);
    }

    /// <summary>Confirmed from Ultimate Doom Builder's own real "zdbsp_udmf_fast" profile ("-R -X -o%FO %FI") - the Testing-purpose flag, trading a real REJECT table for speed.</summary>
    [Fact]
    public void Build_ForTesting_UsesTheRealFastTestProfileFlags()
    {
        var args = ZdbspArguments.Build("/tmp/in.wad", "/tmp/out.wad", "MAP01", forTesting: true);

        Assert.Equal(new[] { "-R", "-X", "-m", "MAP01", "-o", "/tmp/out.wad", "/tmp/in.wad" }, args);
    }
}
