using Godot;

namespace DoomArchitect.Rendering;

/// <summary>
/// A small flat gray icon shown in place of a texture/flat thumbnail that
/// isn't decoded yet (or isn't resolvable at all - e.g. a blank/mixed
/// multi-select field) - shared by every UI that shows a texture/flat
/// thumbnail (<c>TextureBrowserDialog</c>'s gallery, <c>SectorEditDialog</c>'s
/// inline Floor/Ceiling Texture previews, and whatever else needs one
/// later) so they all show the exact same "not ready" placeholder.
/// </summary>
public static class PlaceholderIcon
{
    private static ImageTexture _instance;

    public static ImageTexture Instance => _instance ??= Create();

    private static ImageTexture Create()
    {
        var image = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8);
        image.Fill(new Color(0.3f, 0.3f, 0.3f));
        return ImageTexture.CreateFromImage(image);
    }
}
