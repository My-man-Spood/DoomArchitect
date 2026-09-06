namespace DoomArchitect.Core.Textures;

/// <summary>
/// A decoded RGBA pixel buffer - row-major, 4 bytes per pixel (R,G,B,A).
/// The one pixel type every reader/decoder in this namespace produces and
/// <see cref="TextureSet"/> consumes; trivially convertible to a Godot
/// Image on the App side.
/// </summary>
public sealed class PixelImage
{
    public PixelImage(int width, int height, byte[] rgba)
    {
        Width = width;
        Height = height;
        Rgba = rgba;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Rgba { get; }
}
