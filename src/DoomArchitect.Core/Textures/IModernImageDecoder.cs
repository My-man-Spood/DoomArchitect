namespace DoomArchitect.Core.Textures;

/// <summary>
/// Decodes a PNG/JPEG-encoded lump into an RGBA pixel buffer. A seam
/// rather than a hardcoded call to a specific image library, so the
/// underlying decoder can be swapped out later without touching anything
/// else in this namespace.
/// </summary>
public interface IModernImageDecoder
{
    bool TryDecode(byte[] data, ImageFormatKind kind, out PixelImage? image);
}
