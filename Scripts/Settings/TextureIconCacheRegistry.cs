using System.Collections.Generic;
using DoomArchitect.Core.Textures;
using DoomArchitect.Rendering;

namespace DoomArchitect.Settings;

/// <summary>
/// Process-wide cache of already-warmed <see cref="TextureIconCache"/>s,
/// keyed by the exact <see cref="TextureSet"/> instance they were seeded
/// from (reference equality - a plain class with no override, so a
/// <see cref="Dictionary{TKey,TValue}"/> already compares it that way).
///
/// This is the layer the visible "Caching textures... x/4500" status
/// message actually comes from (<see cref="StatusBar"/>) -
/// <see cref="TextureSetCache"/> only shares the *decoded* pixels; a
/// <see cref="TextureIconCache"/> is a further, Godot-side step on top of
/// that (wrapping each already-decoded pixel buffer in a real GPU-backed
/// <c>ImageTexture</c> for the texture picker/property dialogs), and
/// <c>MapView</c> used to own one per tab outright, unconditionally
/// re-seeding (and so re-uploading all ~4500 icons to the GPU) on every
/// map open even when <see cref="TextureSetCache"/> had already handed it
/// the exact same, already-decoded <see cref="TextureSet"/>. Since two
/// maps from the same mod now share that same <see cref="TextureSet"/>
/// reference, keying on it here is enough to also share the icons built
/// from it - no separate identity bookkeeping needed.
///
/// Like the other process-wide caches here, entries are never evicted -
/// see <see cref="ResourceContainerCache"/>'s own remarks on why that's a
/// deliberate, accepted limitation rather than an oversight.
/// </summary>
public static class TextureIconCacheRegistry
{
    private static readonly Dictionary<TextureSet, TextureIconCache> Cache = new();

    public static TextureIconCache GetOrCreate(TextureSet textures)
    {
        if (Cache.TryGetValue(textures, out var existing)) return existing;

        var cache = new TextureIconCache();
        cache.SeedAll(textures);
        Cache[textures] = cache;
        return cache;
    }
}
