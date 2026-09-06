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
