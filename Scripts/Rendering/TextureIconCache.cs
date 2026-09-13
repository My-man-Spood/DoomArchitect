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
    private readonly Dictionary<string, ImageTexture> _wallIcons = new();
    private readonly Dictionary<string, ImageTexture> _flatIcons = new();
    private readonly Queue<(bool IsFlat, string Name)> _pending = new();
    private TextureSet _textures;

    /// <summary>Queues every known wall/flat name for decode - call once whenever a map's resources (re)load.</summary>
    public void SeedAll(TextureSet textures)
    {
        _textures = textures;
        _wallIcons.Clear();
        _flatIcons.Clear();
        _pending.Clear();

        foreach (var name in textures.GetWallTextureNames()) _pending.Enqueue((false, name));
        foreach (var name in textures.GetFlatNames()) _pending.Enqueue((true, name));
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
}
