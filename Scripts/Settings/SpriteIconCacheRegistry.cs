using System.Collections.Generic;
using DoomArchitect.Core.Textures;
using DoomArchitect.Rendering;

namespace DoomArchitect.Settings;

/// <summary>
/// The <see cref="SpriteIconCache"/> counterpart to
/// <see cref="TextureIconCacheRegistry"/> - same reasoning, same
/// reference-equality-on-<see cref="TextureSet"/> key. <c>spriteNames</c>
/// isn't part of the key: it's a deterministic function of the same
/// <see cref="Core.Configuration.IGameConfiguration"/>/resources that
/// already determined <paramref name="textures"/>'s own identity (via
/// <see cref="TextureSetCache"/>), so two callers sharing that same
/// <see cref="TextureSet"/> reference always mean the same sprite name
/// list too.
/// </summary>
public static class SpriteIconCacheRegistry
{
    private static readonly Dictionary<TextureSet, SpriteIconCache> Cache = new();

    public static SpriteIconCache GetOrCreate(TextureSet textures, IEnumerable<string> spriteNames)
    {
        if (Cache.TryGetValue(textures, out var existing)) return existing;

        var cache = new SpriteIconCache();
        cache.SeedAll(textures, spriteNames);
        Cache[textures] = cache;
        return cache;
    }
}
