using System;
using System.Collections.Generic;
using System.Linq;
using DoomArchitect.Core.Textures;
using DoomArchitect.Interop;
using Godot;

namespace DoomArchitect.Rendering;

/// <summary>
/// The sprite-icon counterpart to <see cref="TextureIconCache"/> - same
/// "seed warm, decode-on-demand fallback, <see cref="ImageTexture"/> out"
/// shape, backed by <see cref="TextureSet.TryGetSpriteTexture"/> (the same
/// real Doom-picture-format decode the 3D view's own thing billboards
/// already use, just packaged here as a flat 2D icon instead of a
/// <c>StandardMaterial3D</c>). Exists for the Thing dialog's embedded type
/// picker and its own live preview panel - neither of which needs a
/// sprite's full rotation set, just the one canonical frame each
/// <see cref="Core.Configuration.ThingTypeInfo.SpriteName"/> already names.
///
/// Deliberately seeded from a caller-supplied name list
/// (<see cref="SeedAll"/>'s <c>spriteNames</c>), not "every sprite lump in
/// the loaded resources" - unlike wall textures/flats,
/// <see cref="TextureSet"/> has no real "enumerate every valid sprite
/// name" method to mirror (sprites are looked up by exact name only,
/// verified directly against <see cref="TextureSet.TryGetSpriteTexture"/>),
/// and a WAD's own sprite namespace holds many rotation-frame lumps per
/// actor a thing-type icon never needs anyway - the game configuration's
/// own <see cref="Core.Configuration.ThingTypeInfo.SpriteName"/> list is
/// the actual bounded set of names this cache will ever be asked for.
///
/// A sprite name genuinely can be missing from the loaded resources (e.g.
/// a PWAD-only map opened without its parent IWAD layered in) - unlike
/// <see cref="TextureIconCache"/>'s own wall/flat lookups, which always
/// resolve to *something* (a placeholder pixel image, internally, inside
/// <see cref="TextureSet"/> itself), <see cref="TextureSet.TryGetSpriteTexture"/>
/// returns a real <c>null</c> in that case - so every lookup here can
/// genuinely return <c>null</c> too, and callers fall back to the shared
/// <see cref="PlaceholderIcon"/> themselves, exactly like
/// <see cref="View.TexturePreviewEdit.SetPreviewTexture"/> already does
/// for a null texture.
/// </summary>
public sealed class SpriteIconCache
{
    private readonly Dictionary<string, ImageTexture> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _pending = new();
    private TextureSet _textures;

    /// <summary>How many names were queued by the most recent <see cref="SeedAll"/> - a status display's denominator.</summary>
    public int TotalCount { get; private set; }

    /// <summary>How many queued names are still waiting to be decoded - a status display's "done" count is <see cref="TotalCount"/> minus this.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>Queues every given sprite name for decode - call once whenever a map's resources (re)load, passing the current game configuration's own known thing-type sprite names.</summary>
    public void SeedAll(TextureSet textures, IEnumerable<string> spriteNames)
    {
        _textures = textures;
        _icons.Clear();
        _pending.Clear();

        foreach (var name in spriteNames.Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _pending.Enqueue(name);
        }

        TotalCount = _pending.Count;
    }

    /// <summary>Decodes up to <paramref name="count"/> pending names - call once per frame from a live map's own update loop, mirroring <see cref="TextureIconCache.ProcessBudget"/>.</summary>
    public void ProcessBudget(int count)
    {
        for (var i = 0; i < count && _pending.Count > 0; i++)
        {
            var name = _pending.Dequeue();
            var icon = Decode(name);
            if (icon != null) _icons[name] = icon;
        }
    }

    /// <summary><c>null</c> if this name hasn't been decoded yet (or genuinely isn't in the loaded resources) - callers show a placeholder and check back later, they never trigger decoding themselves.</summary>
    public ImageTexture GetSpriteIcon(string name) => _icons.GetValueOrDefault(name);

    /// <summary>Like <see cref="GetSpriteIcon"/>, but decodes immediately if not cached yet - safe for a single, bounded request (the Properties tab's own live preview of whichever type is currently selected), matching <see cref="TextureIconCache.GetOrDecodeWallIcon"/>'s own reasoning.</summary>
    public ImageTexture GetOrDecodeSpriteIcon(string name)
    {
        if (_textures == null || string.IsNullOrEmpty(name)) return null;
        if (_icons.TryGetValue(name, out var cached)) return cached;

        var icon = Decode(name);
        if (icon != null) _icons[name] = icon;
        return icon;
    }

    private ImageTexture Decode(string name)
    {
        var pixels = _textures.TryGetSpriteTexture(name);
        return pixels == null ? null : ImageTexture.CreateFromImage(pixels.ToGodotImage());
    }
}
