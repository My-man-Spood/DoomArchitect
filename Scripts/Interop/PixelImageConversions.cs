using DoomArchitect.Core.Textures;

namespace DoomArchitect.Interop;

public static class PixelImageConversions
{
    public static Godot.Image ToGodotImage(this PixelImage image) =>
        Godot.Image.CreateFromData(image.Width, image.Height, false, Godot.Image.Format.Rgba8, image.Rgba);
}
