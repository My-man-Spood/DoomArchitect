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

    /// <summary>
    /// Every name <see cref="SeedAll"/> enumerated as a flat - tracked
    /// separately from which cache a name has actually been decoded into,
    /// since a still-pending name isn't a key in either
    /// <see cref="_flatIcons"/>/<see cref="_wallIcons"/> yet. Backs
    /// <see cref="IsFlat"/>, the only reliable way to tell which decode
    /// path resolves a name whose kind isn't already known by the caller -
    /// e.g. a sector's own Floor/Ceiling field, which GZDoom's real
    /// unified texture manager lets reference either a flat or a
    /// (composited or PK3-folder) wall texture, unlike classic Doom's
    /// strict namespace separation.
    /// </summary>
    private readonly HashSet<string> _flatNames = new(StringComparer.OrdinalIgnoreCase);

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
        _flatNames.Clear();

        foreach (var name in textures.GetWallTextureNames()) _pending.Enqueue((false, name));
        foreach (var name in textures.GetFlatNames())
        {
            _pending.Enqueue((true, name));
            _flatNames.Add(name);
        }

        TotalCount = _pending.Count;
    }

    /// <summary>Whether <paramref name="name"/> is enumerable as a flat (as opposed to a wall texture) - see the remarks on <see cref="_flatNames"/>.</summary>
    public bool IsFlat(string name) => _flatNames.Contains(name);

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
    /// Resolves a texture-name field's icon honoring UDB's real
    /// <c>mixtexturesflats</c> game-configuration setting (see
    /// <see cref="DoomArchitect.Core.Configuration.IGameConfiguration.MixTexturesAndFlats"/>).
    /// <paramref name="preferFlat"/> is the field's own fixed native
    /// namespace (true for a Sector's Floor/Ceiling, false for a
    /// Linedef's wall-texture parts - never varies per map/config, unlike
    /// <paramref name="mixTexturesAndFlats"/>). When mixing is disabled
    /// this is exactly <see cref="GetFlatIcon"/>/<see cref="GetWallIcon"/>
    /// on the field's own namespace; when enabled, whichever namespace
    /// actually enumerates the name wins (<see cref="IsFlat"/>) regardless
    /// of the field's own native one - mirroring UDB's own real load-time
    /// cross-merge of its <c>flats</c>/<c>textures</c> dictionaries when
    /// mixing is on, where a mixed lookup no longer cares which
    /// dictionary a name originally came from.
    /// </summary>
    public ImageTexture GetIcon(string name, bool preferFlat, bool mixTexturesAndFlats)
    {
        if (!mixTexturesAndFlats) return preferFlat ? GetFlatIcon(name) : GetWallIcon(name);
        return IsFlat(name) ? GetFlatIcon(name) : GetWallIcon(name);
    }

    /// <summary>Like <see cref="GetIcon"/>, but decodes immediately - see <see cref="GetOrDecodeFlatIcon"/>/<see cref="GetOrDecodeWallIcon"/>'s own remarks for why that matters for a property dialog's inline preview.</summary>
    public ImageTexture GetOrDecodeIcon(string name, bool preferFlat, bool mixTexturesAndFlats)
    {
        if (!mixTexturesAndFlats) return preferFlat ? GetOrDecodeFlatIcon(name) : GetOrDecodeWallIcon(name);
        return IsFlat(name) ? GetOrDecodeFlatIcon(name) : GetOrDecodeWallIcon(name);
    }

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
