using System.Text;

namespace DoomArchitect.Core.IO;

/// <summary>
/// Decodes an 8-byte fixed-length Doom "name" field (lump names, texture
/// names in binary map lumps) - ASCII, null-terminated if shorter than 8
/// bytes, using the full 8 bytes as-is if not (there's nothing to
/// truncate past - only 8 bytes are ever read). Matches UDB's own
/// <c>Lump.MakeNormalName</c>.
/// </summary>
internal static class DoomBinaryNames
{
    public static string Read(byte[] bytes)
    {
        var length = Array.IndexOf(bytes, (byte)0);
        if (length < 0) length = bytes.Length;
        return Encoding.ASCII.GetString(bytes, 0, length).Trim().ToUpperInvariant();
    }
}
