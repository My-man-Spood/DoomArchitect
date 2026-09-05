using DoomArchitect.Core.IO;

namespace DoomArchitect.Core.Tests.IO;

public class MapFileLoaderTests
{
    [Fact]
    public void LoadUdmfMap_RealFileOnDisk_ProducesTheExpectedMap()
    {
        const string udmf =
            "namespace = \"doom\";\n" +
            "vertex { x = 0.0; y = 0.0; }\n" +
            "vertex { x = 64.0; y = 0.0; }\n" +
            "sector { heightfloor = 0; heightceiling = 128; texturefloor = \"FLOOR0_1\"; textureceiling = \"CEIL1_1\"; }\n" +
            "sidedef { sector = 0; texturemiddle = \"STARTAN2\"; }\n" +
            "linedef { v1 = 0; v2 = 1; sidefront = 0; }\n";

        var bytes = WadTestBuilder.Build(
            ("MAP01", Array.Empty<byte>()),
            ("TEXTMAP", WadTestBuilder.TextLump(udmf)),
            ("ENDMAP", Array.Empty<byte>()));

        var path = Path.Combine(Path.GetTempPath(), $"doomarchitect-test-{Guid.NewGuid():N}.wad");
        try
        {
            File.WriteAllBytes(path, bytes);

            var doc = MapFileLoader.LoadUdmfMap(path, "MAP01");

            Assert.Equal(2, doc.Map.Vertices.Count);
            var linedef = Assert.Single(doc.Map.Linedefs);
            Assert.NotNull(linedef.Front);
            Assert.Equal("STARTAN2", linedef.Front!.MiddleTexture);
            Assert.Same(doc.Map.Sectors[0], linedef.Front.Sector);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
