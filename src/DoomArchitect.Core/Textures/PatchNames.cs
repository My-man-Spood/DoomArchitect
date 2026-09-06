using DoomArchitect.Core.IO;

namespace DoomArchitect.Core.Textures;

/// <summary>Reads the PNAMES lump: a patch-name lookup table indexed by TEXTURE1/2 patch entries.</summary>
public static class PatchNames
{
    public static IReadOnlyList<string> Read(byte[] data)
    {
        using var reader = new BinaryReader(new MemoryStream(data));
        var count = reader.ReadInt32();
        var names = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            names.Add(DoomBinaryNames.Read(reader.ReadBytes(8)));
        }

        return names;
    }
}
