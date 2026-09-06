using System.Collections.Generic;
using DoomArchitect.Core.Textures;
using DoomArchitect.Interop;
using Godot;

namespace DoomArchitect.Rendering;

/// <summary>
/// App-side wrapper around a <see cref="TextureSet"/>: converts decoded
/// pixel data into actual Godot resources and caches one
/// <see cref="StandardMaterial3D"/> per distinct texture/flat name, since
/// many sidedefs/sectors typically share the same texture name. When the
/// underlying <see cref="TextureSet"/> has nothing loaded (no WAD open
/// yet), every lookup just resolves to its shared placeholder image - no
/// special-casing needed here for that state.
/// </summary>
public sealed class TextureCache
{
    private readonly TextureSet _textures;
    private readonly Dictionary<string, (PixelImage Pixels, StandardMaterial3D Material)> _wallCache = new();
    private readonly Dictionary<string, StandardMaterial3D> _flatCache = new();

    public TextureCache(TextureSet textures)
    {
        _textures = textures;
    }

    public StandardMaterial3D GetWallMaterial(string name) => GetWallEntry(name).Material;

    /// <summary>Pixel dimensions of a wall texture, needed to build wall UVs and to size a masked middle wall.</summary>
    public Vector2I GetWallTextureSize(string name)
    {
        var pixels = GetWallEntry(name).Pixels;
        return new Vector2I(pixels.Width, pixels.Height);
    }

    public StandardMaterial3D GetFlatMaterial(string name)
    {
        if (_flatCache.TryGetValue(name, out var cached)) return cached;

        var material = CreateMaterial(_textures.GetFlatTexture(name));
        _flatCache[name] = material;
        return material;
    }

    private (PixelImage Pixels, StandardMaterial3D Material) GetWallEntry(string name)
    {
        if (_wallCache.TryGetValue(name, out var cached)) return cached;

        var pixels = _textures.GetWallTexture(name);
        var entry = (pixels, CreateMaterial(pixels));
        _wallCache[name] = entry;
        return entry;
    }

    private static StandardMaterial3D CreateMaterial(PixelImage pixels)
    {
        var texture = ImageTexture.CreateFromImage(pixels.ToGodotImage());
        return new StandardMaterial3D
        {
            AlbedoTexture = texture,
            // Doom art is deliberately low-res and chunky - nearest-neighbor
            // preserves that look instead of Godot's default smoothing. A
            // rendering choice, not a UDB behavior.
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            // Sector/wall brightness is baked as a per-vertex color (see
            // SectorMeshBuilder/WallMeshBuilder) rather than driven by a
            // real Godot light - Doom's own lighting has no concept of
            // light direction or shadows, so letting the scene's actual
            // light respond to surface normals would look wrong and
            // wouldn't reflect a sector's real light level at all.
            // Unshaded turns that off entirely; VertexColorUseAsAlbedo is
            // what makes the baked color actually multiply the texture.
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
        };
    }
}
