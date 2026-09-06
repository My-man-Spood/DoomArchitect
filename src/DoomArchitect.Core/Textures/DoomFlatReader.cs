namespace DoomArchitect.Core.Textures;

/// <summary>
/// Decodes a flat (floor/ceiling texture): a raw indexed byte array with
/// no header at all. Size is inferred from the lump length rather than
/// fixed at 64x64, matching UDB's own <c>DoomFlatReader</c> exactly - a
/// perfect-square length is used as-is (covering 32x32, 64x64, 128x128,
/// etc.), otherwise a length over 4096 bytes is forced to 64x64 and only
/// the first 4096 bytes are read, silently ignoring the rest. This is a
/// real UDB quirk for malformed/odd-sized flats, reproduced as-is rather
/// than "fixed" - it's Core map-format logic, not App-layer rendering.
/// </summary>
public static class DoomFlatReader
{
    private const int StandardSize = 64;
    private const int StandardByteCount = StandardSize * StandardSize;

    public static PixelImage? TryRead(byte[] data, Playpal palette)
    {
        if (data.Length == 0) return null;

        int size;
        var sqrt = (int)Math.Round(Math.Sqrt(data.Length));
        if (sqrt * sqrt == data.Length)
        {
            size = sqrt;
        }
        else if (data.Length > StandardByteCount)
        {
            size = StandardSize;
        }
        else
        {
            return null;
        }

        var rgba = new byte[size * size * 4];
        for (var i = 0; i < size * size; i++)
        {
            var (r, g, b) = palette[data[i]];
            var pixelIndex = i * 4;
            rgba[pixelIndex] = r;
            rgba[pixelIndex + 1] = g;
            rgba[pixelIndex + 2] = b;
            rgba[pixelIndex + 3] = 255;
        }

        return new PixelImage(size, size, rgba);
    }
}
