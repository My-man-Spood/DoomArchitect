namespace DoomArchitect.Core.Textures;

/// <summary>
/// A decoded RGBA pixel buffer - row-major, 4 bytes per pixel (R,G,B,A).
/// The one pixel type every reader/decoder in this namespace produces and
/// <see cref="TextureSet"/> consumes; trivially convertible to a Godot
/// Image on the App side.
/// </summary>
public sealed class PixelImage
{
    public PixelImage(int width, int height, byte[] rgba, int offsetX = 0, int offsetY = 0)
    {
        Width = width;
        Height = height;
        Rgba = rgba;
        OffsetX = offsetX;
        OffsetY = offsetY;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Rgba { get; }

    /// <summary>
    /// The Doom picture format's own offset fields - meaningful for
    /// sprites (where they place the image relative to a thing's actual
    /// world position), always 0 for flats and composed wall textures
    /// (which never had these fields to begin with).
    /// </summary>
    public int OffsetX { get; }

    public int OffsetY { get; }
}
