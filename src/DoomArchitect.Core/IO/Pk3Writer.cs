using System.IO.Compression;

namespace DoomArchitect.Core.IO;

/// <summary>
/// Writes a PK3 - the write-side mirror of <see cref="Pk3File"/>'s own
/// read side, and the write-side counterpart to <see cref="WadWriter"/>
/// for the other container format this project supports. Always a full,
/// from-scratch rebuild of every entry, never an in-place archive patch
/// (never <see cref="ZipArchiveMode.Update"/>) - matching this project's
/// own <see cref="WadWriter"/> convention (a real UDB bug, GitHub #531,
/// is why WAD saves never patch in place) and, independently, exactly
/// what UDB's own real <c>PK3Reader.SaveFile</c> does for PK3: a fresh,
/// independent read, remove+re-add the one changed entry, rebuild the
/// whole archive into memory, then overwrite the file - confirmed by
/// reading its source directly, not guessed. Returns the finished bytes
/// rather than writing to disk itself, same split <see cref="WadWriter"/>
/// already uses - the caller owns backup/overwrite policy for the actual
/// file (matching UDB's own PK3 behavior there too: no backup file,
/// unlike its WAD/map saves, which do get one).
/// </summary>
public static class Pk3Writer
{
    public static byte[] Write(IReadOnlyList<(string Path, byte[] Data)> entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, data) in entries)
            {
                using var entryStream = archive.CreateEntry(path, CompressionLevel.Optimal).Open();
                entryStream.Write(data, 0, data.Length);
            }
        }

        return stream.ToArray();
    }
}
