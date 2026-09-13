using System.IO.Compression;
using DoomArchitect.Core.IO;

namespace DoomArchitect.Core.Tests.IO;

/// <summary>Assembles a minimal, valid PK3 (zip) entirely in memory for tests, without touching the filesystem - mirrors <see cref="WadTestBuilder"/>'s role for WADs.</summary>
internal static class Pk3TestBuilder
{
    public static Pk3File Build(params (string EntryPath, byte[] Data)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (entryPath, data) in entries)
            {
                var entry = archive.CreateEntry(entryPath);
                using var entryStream = entry.Open();
                entryStream.Write(data, 0, data.Length);
            }
        }

        stream.Position = 0;
        return Pk3File.Open(stream);
    }
}
