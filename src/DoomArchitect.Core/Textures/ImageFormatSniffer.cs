namespace DoomArchitect.Core.Textures;

public enum ImageFormatKind
{
    DoomPicture,
    DoomFlat,
    Png,
    Jpeg,
    Pcx,
    Tga,
    Unknown,
}

/// <summary>
/// Detects PNG/JPEG/PCX/TGA by signature, mirroring UDB's own
/// <c>ImageDataFormat</c> dispatch order: every lump is signature-sniffed
/// for a modern format *before* ever attempting classic Doom picture/flat
/// parsing - this applies uniformly to standalone lookups and to patches
/// used inside a composited TEXTURE1/2 texture. Doesn't distinguish
/// DoomPicture from DoomFlat itself (both fall out as <see
/// cref="ImageFormatKind.Unknown"/> here) - callers already know which of
/// those two they're resolving from context.
/// </summary>
public static class ImageFormatSniffer
{
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    // Exact 4-byte match ported from UDB's ImageDataFormat.PCX_SIGNATURE:
    // manufacturer=10 (ZSoft), version=5 (PC Paintbrush v3.0+), encoding=1
    // (RLE), bitsPerComponent=8. UDB checks nothing beyond these 4 bytes.
    private static readonly byte[] PcxSignature = { 10, 5, 1, 8 };

    public static ImageFormatKind Detect(byte[] data)
    {
        if (StartsWith(data, PngSignature)) return ImageFormatKind.Png;
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return ImageFormatKind.Jpeg;
        if (StartsWith(data, PcxSignature)) return ImageFormatKind.Pcx;
        if (IsTga(data)) return ImageFormatKind.Tga;
        return ImageFormatKind.Unknown;
    }

    private static bool StartsWith(byte[] data, byte[] signature)
    {
        if (data.Length < signature.Length) return false;
        for (var i = 0; i < signature.Length; i++)
        {
            if (data[i] != signature[i]) return false;
        }

        return true;
    }

    // TGA has no magic number, so this is a heuristic - ported verbatim
    // from UDB's own ImageDataFormat.CheckTgaSignature. Color-map-type and
    // image-type alone are weak discriminators: a real Doom patch_t header
    // (width/height/offsets followed by an Int32 column-pointer table) can
    // easily land in the same byte ranges purely by chance, misclassifying
    // legitimate vanilla patches as TGA. UDB avoids this by also range-
    // checking width/height and the bits-per-pixel field - all three checks
    // are needed together, matching UDB exactly rather than the two-field
    // subset that first shipped here (caught in review: that subset had a
    // real false-positive rate against genuine Doom picture data).
    private static bool IsTga(byte[] data)
    {
        if (data.Length < 18) return false;

        var colorMapType = data[1];
        if (colorMapType > 1) return false;

        var imageType = data[2];
        if ((imageType > 3 && imageType < 9) || imageType > 11) return false;

        var width = data[12] + (data[13] << 8);
        if (width is <= 0 or > 8192) return false;

        var height = data[14] + (data[15] << 8);
        if (height is <= 0 or > 8192) return false;

        var bitsPerPixel = data[16];
        return bitsPerPixel is 8 or 16 or 24 or 32;
    }
}
