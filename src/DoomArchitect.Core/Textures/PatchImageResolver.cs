namespace DoomArchitect.Core.Textures;

/// <summary>
/// The single "decode these lump bytes into pixels" entry point, mirroring
/// UDB's <c>ImageDataFormat.TryLoadImage</c> dispatch order: sniff for a
/// modern format first, and only fall back to classic Doom picture format
/// if nothing modern matched. Both standalone patch lookups and
/// composite-texture patch resolution go through this, so a PNG-encoded
/// patch decodes identically in either context.
/// </summary>
public sealed class PatchImageResolver
{
    private readonly Playpal _palette;
    private readonly IModernImageDecoder _modernDecoder;

    public PatchImageResolver(Playpal palette, IModernImageDecoder? modernDecoder = null)
    {
        _palette = palette;
        _modernDecoder = modernDecoder ?? new ImageSharpModernImageDecoder();
    }

    /// <summary>
    /// Just the "is this actually a modern image format" half of
    /// <see cref="TryResolvePatch"/>'s own dispatch, with no classic-patch
    /// fallback baked in - for a caller whose own classic fallback isn't
    /// the column-post patch format (<see cref="TextureSet.GetFlatTexture"/>'s
    /// raw-indexed-bytes flat reader has no signature of its own to
    /// distinguish real flat data from a modern-format lump that merely
    /// happens to also decode "successfully" as garbage, so it must be
    /// tried only after this one has already ruled a modern format out -
    /// see that method's own remarks).
    /// </summary>
    public bool TryResolveModernImage(byte[] data, out PixelImage? image)
    {
        var kind = ImageFormatSniffer.Detect(data);
        if (kind is ImageFormatKind.Png or ImageFormatKind.Jpeg && _modernDecoder.TryDecode(data, kind, out image))
        {
            return true;
        }

        image = null;
        return false;
    }

    public bool TryResolvePatch(byte[] data, out PixelImage? image, out string? warning)
    {
        var kind = ImageFormatSniffer.Detect(data);

        switch (kind)
        {
            case ImageFormatKind.Png:
            case ImageFormatKind.Jpeg:
                if (_modernDecoder.TryDecode(data, kind, out image))
                {
                    warning = null;
                    return true;
                }

                warning = $"Recognized a {kind} signature but failed to decode it.";
                return false;

            case ImageFormatKind.Pcx:
            case ImageFormatKind.Tga:
                image = null;
                warning = $"Recognized a {kind} signature, but {kind} decoding is not supported.";
                return false;

            default:
                image = DoomPictureReader.TryRead(data, _palette);
                if (image != null)
                {
                    warning = null;
                    return true;
                }

                warning = "Data could not be decoded as a Doom picture.";
                return false;
        }
    }
}
