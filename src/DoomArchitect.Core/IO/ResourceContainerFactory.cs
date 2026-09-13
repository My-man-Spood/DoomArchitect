namespace DoomArchitect.Core.IO;

/// <summary>
/// The one place that decides "does this path need a <see cref="WadFile"/>
/// or a <see cref="Pk3File"/>" - shared by the resource-list UI's Add flow
/// and by reloading a previously saved resource path list, so that logic
/// only lives once. Sniffs the first 4 bytes rather than trusting the
/// extension alone (a real WAD always starts with <c>IWAD</c>/<c>PWAD</c>;
/// a real zip/PK3 always starts with the <c>PK</c> signature bytes), only
/// falling back to the extension if neither signature matches - e.g. an
/// empty zip archive still opens correctly this way.
/// </summary>
public static class ResourceContainerFactory
{
    public static IResourceContainer Open(string path)
    {
        var header = new byte[4];
        using (var probe = File.OpenRead(path))
        {
            _ = probe.Read(header, 0, header.Length);
        }

        if (IsWad(header)) return WadFile.Read(path);
        if (IsZip(header)) return Pk3File.Open(path);

        return Path.GetExtension(path).Equals(".pk3", StringComparison.OrdinalIgnoreCase)
            ? Pk3File.Open(path)
            : WadFile.Read(path);
    }

    private static bool IsWad(byte[] header) => Matches(header, "IWAD") || Matches(header, "PWAD");

    private static bool IsZip(byte[] header) =>
        header[0] == (byte)'P' && header[1] == (byte)'K' && (header[2] == 3 || header[2] == 5 || header[2] == 7);

    private static bool Matches(byte[] header, string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (header[i] != (byte)text[i]) return false;
        }

        return true;
    }
}
