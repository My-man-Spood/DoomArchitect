using System;
using System.Collections.Generic;
using DoomArchitect.Core.Textures;
using DoomArchitect.Interop;
using Godot;

namespace DoomArchitect.Rendering;

/// <summary>
/// A warm cache of every wall-texture/flat icon (a small, ready-to-display
/// <see cref="ImageTexture"/>), seeded from a <see cref="TextureSet"/> the
/// moment a map's resources load and filled in progressively over
/// subsequent frames - never decoded on-demand by some specific UI feature
/// (a texture picker, a future hover-preview) asking for one for the first
/// time. Decoding stays on the main thread, budgeted a handful of names per
/// frame via <see cref="ProcessBudget"/> rather than done all at once:
/// <see cref="TextureSet"/>'s own internal caches aren't thread-safe, so a
/// real background thread would need locking this project doesn't have,
/// and a <c>gzdoom.pk3</c>-sized resource (thousands of entries) decoded in
/// one go would visibly stall whatever frame it happened on. Within a
/// couple of seconds of a map loading, every icon is ready and
/// <see cref="GetWallIcon"/>/<see cref="GetFlatIcon"/> are instant.
/// </summary>
public sealed class TextureIconCache
{
    // Case-insensitive, matching every other name-keyed texture cache in
    // this codebase (TextureSet's own _wallCache/_flatCache/_spriteCache) -
    // a name seeded here in the WAD/PK3's own canonical casing must still
    // be found when looked up via a map's stored texture name, which can
    // legitimately differ in case (e.g. an existing sector's FloorTexture
    // string vs. the lump's real directory casing).
    private readonly Dictionary<string, ImageTexture> _wallIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ImageTexture> _flatIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<(bool IsFlat, string Name)> _pending = new();
    private TextureSet _textures;

    /// <summary>How many names were queued by the most recent <see cref="SeedAll"/> - a status display's denominator.</summary>
    public int TotalCount { get; private set; }

    /// <summary>How many queued names are still waiting to be decoded - a status display's "done" count is <see cref="TotalCount"/> minus this.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>Queues every known wall/flat name for decode - call once whenever a map's resources (re)load.</summary>
    public void SeedAll(TextureSet textures)
    {
        _textures = textures;
        _wallIcons.Clear();
        _flatIcons.Clear();
        _pending.Clear();

        foreach (var name in textures.GetWallTextureNames()) _pending.Enqueue((false, name));
        foreach (var name in textures.GetFlatNames()) _pending.Enqueue((true, name));

        TotalCount = _pending.Count;
    }

    /// <summary>Decodes up to <paramref name="count"/> pending names - call once per frame from a live map's own update loop.</summary>
    public void ProcessBudget(int count)
    {
        for (var i = 0; i < count && _pending.Count > 0; i++)
        {
            var (isFlat, name) = _pending.Dequeue();
            var pixels = isFlat ? _textures.GetFlatTexture(name) : _textures.GetWallTexture(name);
            var icon = ImageTexture.CreateFromImage(pixels.ToGodotImage());
            (isFlat ? _flatIcons : _wallIcons)[name] = icon;
        }
    }

    /// <summary><c>null</c> if this name hasn't been decoded yet - callers show a placeholder and check back later, they never trigger decoding themselves.</summary>
    public ImageTexture GetWallIcon(string name) => _wallIcons.GetValueOrDefault(name);

    public ImageTexture GetFlatIcon(string name) => _flatIcons.GetValueOrDefault(name);

    /// <summary>
    /// Like <see cref="GetFlatIcon"/>, but decodes immediately (bypassing
    /// the background queue entirely) if the name isn't cached yet -
    /// safe for a single, bounded request (e.g. a property dialog's own
    /// inline preview, at most a couple of images at once) in a way that
    /// wouldn't be for the picker's full gallery, which can hold thousands
    /// of entries and is exactly why <see cref="ProcessBudget"/> exists.
    ///
    /// Also closes a real gap <see cref="SeedAll"/> alone can't: it only
    /// ever queues names <see cref="TextureSet.GetWallTextureNames"/>/
    /// <see cref="TextureSet.GetFlatNames"/> can *enumerate* (namespace-
    /// scanned), which isn't guaranteed to be every name
    /// <see cref="TextureSet.GetWallTexture"/>/<see cref="TextureSet.GetFlatTexture"/>
    /// can actually *resolve* (an unrestricted lookup by exact name) - a
    /// flat missing proper <c>F_START</c>/<c>F_END</c> markers, for
    /// instance, renders correctly in the map but was never enumerable,
    /// so it would never have been queued and would sit gray forever
    /// without this fallback.
    /// </summary>
    public ImageTexture GetOrDecodeWallIcon(string name) => GetOrDecode(_wallIcons, name, isFlat: false);

    public ImageTexture GetOrDecodeFlatIcon(string name) => GetOrDecode(_flatIcons, name, isFlat: true);

    private ImageTexture GetOrDecode(Dictionary<string, ImageTexture> cache, string name, bool isFlat)
    {
        if (_textures == null) return null;
        if (cache.TryGetValue(name, out var cached)) return cached;

        var pixels = isFlat ? _textures.GetFlatTexture(name) : _textures.GetWallTexture(name);
        var icon = ImageTexture.CreateFromImage(pixels.ToGodotImage());
        cache[name] = icon;
        return icon;
    }
}
