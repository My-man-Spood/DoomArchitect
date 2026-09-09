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
    private readonly Dictionary<string, (PixelImage Pixels, StandardMaterial3D Material)> _spriteCache = new();

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

    /// <summary>
    /// The sprite material/size/offset for a Thing's resolved type, or null
    /// if the sprite lump isn't present in the currently loaded WAD - an
    /// expected, common miss (sprites usually live only in the IWAD; see
    /// <see cref="TextureSet.TryGetSpriteTexture"/>'s own remarks), not a
    /// load failure - callers fall back to the generic placeholder instead
    /// of a "missing texture" checkerboard. <see cref="Size"/>/<see cref="Offset"/>
    /// are the sprite's own real pixel dimensions/anchor - a thing type's
    /// gameplay radius/height are collision values, not the sprite art's
    /// actual proportions, and using them to size the billboard quad
    /// stretches or squishes any sprite whose aspect ratio doesn't happen
    /// to match "2*radius : height".
    /// </summary>
    public (StandardMaterial3D Material, Vector2I Size, Vector2I Offset)? TryGetSpriteEntry(string spriteName)
    {
        if (_spriteCache.TryGetValue(spriteName, out var cached))
        {
            return (cached.Material, new Vector2I(cached.Pixels.Width, cached.Pixels.Height), new Vector2I(cached.Pixels.OffsetX, cached.Pixels.OffsetY));
        }

        var pixels = _textures.TryGetSpriteTexture(spriteName);
        if (pixels == null) return null;

        var entry = (pixels, CreateSpriteMaterial(pixels));
        _spriteCache[spriteName] = entry;
        return (entry.Item2, new Vector2I(pixels.Width, pixels.Height), new Vector2I(pixels.OffsetX, pixels.OffsetY));
    }

    private (PixelImage Pixels, StandardMaterial3D Material) GetWallEntry(string name)
    {
        if (_wallCache.TryGetValue(name, out var cached)) return cached;

        var pixels = _textures.GetWallTexture(name);
        var entry = (pixels, CreateMaterial(pixels));
        _wallCache[name] = entry;
        return entry;
    }

    /// <summary>Same base look as <see cref="CreateMaterial"/> (unshaded, nearest-filtered) plus alpha transparency and always facing the camera - a sprite is a see-through billboard, a wall/flat surface never is.</summary>
    private static StandardMaterial3D CreateSpriteMaterial(PixelImage pixels)
    {
        var material = CreateMaterial(pixels);
        material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        material.BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY;
        material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        return material;
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
