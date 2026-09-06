using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DoomArchitect.Core.Textures;

/// <summary>Default <see cref="IModernImageDecoder"/>, backed by SixLabors.ImageSharp. Handles PNG/JPEG only.</summary>
public sealed class ImageSharpModernImageDecoder : IModernImageDecoder
{
    public bool TryDecode(byte[] data, ImageFormatKind kind, out PixelImage? image)
    {
        if (kind != ImageFormatKind.Png && kind != ImageFormatKind.Jpeg)
        {
            image = null;
            return false;
        }

        try
        {
            using var decoded = Image.Load<Rgba32>(data);
            var rgba = new byte[decoded.Width * decoded.Height * 4];
            decoded.CopyPixelDataTo(rgba);
            image = new PixelImage(decoded.Width, decoded.Height, rgba);
            return true;
        }
        catch (Exception)
        {
            // Signature matched, but the data itself is malformed - a
            // decode failure here is a data-quality problem in the WAD,
            // not a programming error, so it's handled like any other
            // "couldn't resolve this patch" case rather than propagated.
            image = null;
            return false;
        }
    }
}
