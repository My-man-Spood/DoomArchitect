using System.Collections.Generic;
using System.IO;
using DoomArchitect.Core.IO;

namespace DoomArchitect.Settings;

/// <summary>
/// Process-wide cache of already-opened <see cref="IResourceContainer"/>s,
/// keyed by full path - so opening a second map from the same mod (the
/// common case now that multiple Map tabs can be open at once) reuses the
/// exact same containers instead of re-reading them from disk. This matters
/// most for a WAD resource - <see cref="WadFile.Read"/> eagerly copies every
/// lump's bytes into memory - and for the per-instance decode caches a fresh
/// <see cref="Core.Textures.TextureSet"/> would otherwise start cold with
/// (every wall/flat/sprite re-decoded on first use, even though the exact
/// same bytes were just decoded for another open tab).
///
/// Entries are never evicted or disposed: nothing in this codebase disposes
/// a resource container today (confirmed - see <see cref="Pk3File"/>'s own
/// remarks), so a cache that only ever grows for the lifetime of the process
/// introduces no new lifetime risk, just extends the existing "load once,
/// trust it" assumption across tabs instead of within just one.
/// </summary>
public static class ResourceContainerCache
{
    private static readonly Dictionary<string, IResourceContainer> Cache = new();

    public static IResourceContainer Open(string path)
    {
        var key = Path.GetFullPath(path);
        if (Cache.TryGetValue(key, out var existing)) return existing;

        var container = ResourceContainerFactory.Open(path);
        Cache[key] = container;
        return container;
    }
}
