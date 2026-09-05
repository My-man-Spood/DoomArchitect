using System.Text;

namespace DoomArchitect.Core.Tests.IO;

/// <summary>Assembles a minimal, valid WAD byte stream for tests, without needing a real WAD file.</summary>
internal static class WadTestBuilder
{
    public static byte[] Build(params (string Name, byte[] Data)[] lumps)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write("PWAD".ToCharArray());
        writer.Write(lumps.Length);

        var positions = new int[lumps.Length];
        writer.Write(0); // directory offset placeholder, patched below
        var directoryOffsetPosition = 8;

        for (var i = 0; i < lumps.Length; i++)
        {
            positions[i] = (int)stream.Position;
            writer.Write(lumps[i].Data);
        }

        var directoryOffset = (int)stream.Position;
        for (var i = 0; i < lumps.Length; i++)
        {
            writer.Write(positions[i]);
            writer.Write(lumps[i].Data.Length);
            writer.Write(PaddedName(lumps[i].Name));
        }

        writer.Flush();
        stream.Position = directoryOffsetPosition;
        writer.Write(directoryOffset);

        return stream.ToArray();
    }

    private static byte[] PaddedName(string name)
    {
        var bytes = new byte[8];
        var nameBytes = Encoding.ASCII.GetBytes(name);
        Array.Copy(nameBytes, bytes, Math.Min(nameBytes.Length, 8));
        return bytes;
    }

    public static byte[] TextLump(string text) => Encoding.ASCII.GetBytes(text);
}
