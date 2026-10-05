using DoomArchitect.Core.IO;

namespace DoomArchitect.Core.Tests.IO;

/// <summary>Writes a minimal folder tree to a fresh temp directory for tests - the one real-filesystem-touching test builder in this project, since <see cref="DirectoryResource"/> has no in-memory equivalent of a WAD/PK3's own byte stream to build against.</summary>
internal static class DirectoryTestBuilder
{
    public static DirectoryResource Build(params (string EntryPath, byte[] Data)[] entries) => BuildWithRoot(entries).Resource;

    /// <summary>Same as <see cref="Build"/>, but also hands back the real temp-directory root - needed by any test asserting against an entry's own absolute on-disk path (e.g. <c>ContainsFile</c>).</summary>
    public static (DirectoryResource Resource, string Root) BuildWithRoot(params (string EntryPath, byte[] Data)[] entries)
    {
        var root = Directory.CreateTempSubdirectory("da_dirresource_test_").FullName;

        foreach (var (entryPath, data) in entries)
        {
            var fullPath = Path.Combine(root, entryPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, data);
        }

        return (DirectoryResource.Open(root), root);
    }
}
