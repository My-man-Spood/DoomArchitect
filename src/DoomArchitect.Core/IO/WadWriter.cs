using System.Text;

namespace DoomArchitect.Core.IO;

/// <summary>
/// Writes the classic WAD container format - the mirror image of
/// <see cref="WadFile.Read(Stream)"/>, matching its own already-verified
/// format understanding exactly (same field order/sizes, 8-byte
/// fixed-width uppercase names). Always writes a "PWAD" (this project
/// only ever authors user-created content, never an IWAD). A full,
/// from-scratch rebuild every time - never an in-place patch of an
/// existing file - matching UDB's own real <c>MapManager.SaveMap</c>,
/// which deliberately rebuilds the whole target WAD rather than editing
/// one in place (its own source cites a real bug, GitHub issue #531, as
/// the reason). Returns the finished bytes rather than writing to disk
/// itself - the caller (a save feature) owns backup/overwrite policy for
/// the actual file on disk.
/// </summary>
public static class WadWriter
{
    public static byte[] Write(IReadOnlyList<WadLump> lumps)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("PWAD"));
        writer.Write(lumps.Count);

        var directoryOffsetPosition = stream.Position;
        writer.Write(0); // patched below, once the real directory offset is known

        var positions = new int[lumps.Count];
        for (var i = 0; i < lumps.Count; i++)
        {
            positions[i] = (int)stream.Position;
            writer.Write(lumps[i].Data);
        }

        var directoryOffset = (int)stream.Position;
        for (var i = 0; i < lumps.Count; i++)
        {
            writer.Write(positions[i]);
            writer.Write(lumps[i].Data.Length);
            WriteLumpName(writer, lumps[i].Name);
        }

        writer.Flush();
        stream.Position = directoryOffsetPosition;
        writer.Write(directoryOffset);

        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteLumpName(BinaryWriter writer, string name)
    {
        var bytes = new byte[8];
        var nameBytes = Encoding.ASCII.GetBytes(name.ToUpperInvariant());
        Array.Copy(nameBytes, bytes, Math.Min(nameBytes.Length, 8));
        writer.Write(bytes);
    }
}
