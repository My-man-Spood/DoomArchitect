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
/// Entries are never evicted or disposed on their own: nothing in this
/// codebase disposes a resource container today (confirmed - see
/// <see cref="Pk3File"/>'s own remarks), so a cache that only ever grows
/// for the lifetime of the process introduces no new lifetime risk, just
/// extends the existing "load once, trust it" assumption across tabs
/// instead of within just one. <see cref="Invalidate"/> is the one
/// deliberate exception - see its own remarks.
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

    /// <summary>
    /// Drops the cached container for <paramref name="path"/>, if any,
    /// WITHOUT disposing it - another tab's already-loaded
    /// <see cref="Core.Textures.TextureSet"/> might still hold its own
    /// reference to this exact instance (e.g. a still-pending lazy
    /// decompression), and disposing out from under that would be the one
    /// genuinely unsafe move here. The old instance just becomes
    /// GC-eligible once nothing else references it; the next
    /// <see cref="Open"/> call for this path builds a fresh instance
    /// reflecting whatever is now actually on disk. Called after a
    /// successful PK3 save (see <c>ScriptDocument.SavePk3Entry</c>) so a
    /// map tab opened afterward sees the saved change, not a stale cache
    /// hit.
    /// </summary>
    public static void Invalidate(string path) => Cache.Remove(Path.GetFullPath(path));
}
