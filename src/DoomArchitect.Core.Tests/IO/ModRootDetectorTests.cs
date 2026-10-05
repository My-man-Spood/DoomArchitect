using DoomArchitect.Core.IO;

namespace DoomArchitect.Core.Tests.IO;

public class ModRootDetectorTests
{
    [Fact]
    public void DetectFrom_WadInsideMapsSubfolder_ReturnsGrandparent()
    {
        var wadPath = Path.Combine("mods", "mymod", "maps", "MAP01.wad");

        var modRoot = ModRootDetector.DetectFrom(wadPath);

        Assert.Equal(Path.Combine("mods", "mymod"), modRoot);
    }

    [Fact]
    public void DetectFrom_MapsFolderNameIsCaseInsensitive()
    {
        var wadPath = Path.Combine("mods", "mymod", "MAPS", "MAP01.wad");

        var modRoot = ModRootDetector.DetectFrom(wadPath);

        Assert.Equal(Path.Combine("mods", "mymod"), modRoot);
    }

    [Fact]
    public void DetectFrom_WadNotInsideMapsSubfolder_ReturnsWadPathItself()
    {
        var wadPath = Path.Combine("mods", "standalone.wad");

        var modRoot = ModRootDetector.DetectFrom(wadPath);

        Assert.Equal(wadPath, modRoot);
    }

    [Fact]
    public void DetectFrom_WadWithNoParentDirectory_ReturnsWadPathItself()
    {
        var modRoot = ModRootDetector.DetectFrom("standalone.wad");

        Assert.Equal("standalone.wad", modRoot);
    }

    [Fact]
    public void DetectFrom_FolderLiterallyNamedMapsButNotAParent_StillDetectsCorrectly()
    {
        // "maps" has to be the WAD's own immediate parent, not just present
        // somewhere in the path - a WAD two levels under a "maps" folder
        // shouldn't resolve to that folder's own parent.
        var wadPath = Path.Combine("mods", "maps", "nested", "MAP01.wad");

        var modRoot = ModRootDetector.DetectFrom(wadPath);

        Assert.Equal(wadPath, modRoot);
    }
}
