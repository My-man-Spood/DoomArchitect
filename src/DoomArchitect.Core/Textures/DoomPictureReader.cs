namespace DoomArchitect.Core.Textures;

/// <summary>
/// Decodes the classic Doom "picture" format (<c>patch_t</c>): a small
/// header followed by one column-of-posts per pixel column, each post
/// RLE-encoding a run of opaque pixels with fully transparent gaps between
/// posts. Ported from UDB's own <c>DoomPictureReader</c>, including two
/// verbatim quirks:
///
/// - Tall-patch topdelta accumulation: a post's topdelta is treated as
///   relative to the previous one (accumulated) whenever it's strictly
///   less than the running total, or - only for patches taller than 256
///   pixels - when it's exactly equal to the running total; otherwise an
///   equal topdelta is treated as an absolute value. (UDB's own comment
///   claims this exists for patches "higher than 508 pixels", but the
///   actual guard checks <c>height &gt; 256</c> - the code, not the
///   comment, is what's reproduced here.)
/// - A column whose computed pixel offset overflows the pixel buffer
///   aborts decoding the *entire* patch, not just that column.
///
/// Also ports UDB's separate <c>Validate</c> gate (normally run *before*
/// <c>ReadAsPixelData</c> even starts, to help UDB's own format-guessing
/// dispatch pick the right reader) as an inline check here instead: every
/// column offset must genuinely point somewhere inside this lump's own
/// data, strictly past its own 8-byte header plus column-offset table
/// (<c>8 + width * 4</c>). Skipping this (an earlier gap in this port)
/// let a lump that isn't really patch-format data at all - one that just
/// happened to reach this reader as a fallback, e.g. something routed
/// here by name/namespace convention rather than genuinely being a
/// <c>patch_t</c> - decode into whatever bytes its bogus offsets
/// happened to land on instead of being rejected, surfacing as a
/// texture full of "random multicolor noise" instead of a clean null.
/// </summary>
public static class DoomPictureReader
{
    public static PixelImage? TryRead(byte[] data, Playpal palette)
    {
        if (data.Length < 4) return null;

        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);

        // Column offsets are absolute from the very start of the patch
        // data (byte 0), not relative to wherever the header happens to
        // end - captured here, before any header field is read, to make
        // that explicit rather than hardcoding 0.
        var dataOffset = (int)stream.Position;

        var width = reader.ReadInt16();
        var height = reader.ReadInt16();
        // Only meaningful for standalone sprites (where they place the
        // image relative to a thing's world position); composed wall
        // textures/flats never read them at all, so PixelImage just
        // defaults both to 0 there.
        var offsetX = reader.ReadInt16();
        var offsetY = reader.ReadInt16();

        if (width < 1 || height < 1) return null;

        // Everything from here on trusts the lump's own header-declared
        // width plus whatever column offsets/post data follow it - real
        // WAD content in the wild (a sprite-namespace lump that isn't
        // actually a patch, a truncated/corrupt one, or one whose column
        // offsets point past the end of its own data) can violate any of
        // that, which surfaces as a stream-bounds exception
        // (EndOfStreamException most commonly, but a bad seek/index can
        // throw other exception types too) rather than a clean early
        // return. Matches this project's own established
        // ImageSharpModernImageDecoder precedent: a decode failure here is
        // a data-quality problem in the WAD, not a programming error, so
        // it's handled like any other "couldn't resolve this patch" case
        // (a null return) rather than propagated to crash the caller -
        // this method's own "Try" name is a real contract, not just
        // nullable-annotation decoration.
        try
        {
            var columnOffsets = new int[width];
            for (var x = 0; x < width; x++) columnOffsets[x] = reader.ReadInt32();

            var minValidColumnOffset = 8 + width * 4;
            foreach (var columnOffset in columnOffsets)
            {
                if (columnOffset < minValidColumnOffset || columnOffset >= data.Length) return null;
            }

            var rgba = new byte[width * height * 4];

            for (var x = 0; x < width; x++)
            {
                stream.Seek(dataOffset + columnOffsets[x], SeekOrigin.Begin);

                var y = (int)reader.ReadByte();
                var terminator = y;

                while (terminator != 255)
                {
                    var count = reader.ReadByte();
                    reader.ReadByte(); // padding byte before pixel data

                    for (var i = 0; i < count; i++)
                    {
                        var paletteIndex = reader.ReadByte();
                        var offset = (y + i) * width + x;
                        if (offset < 0 || offset >= width * height) return null;

                        var (r, g, b) = palette[paletteIndex];
                        var pixelIndex = offset * 4;
                        rgba[pixelIndex] = r;
                        rgba[pixelIndex + 1] = g;
                        rgba[pixelIndex + 2] = b;
                        rgba[pixelIndex + 3] = 255;
                    }

                    reader.ReadByte(); // padding byte after pixel data

                    terminator = reader.ReadByte();
                    if (terminator < y || (height > 256 && terminator == y)) y += terminator;
                    else y = terminator;
                }
            }

            return new PixelImage(width, height, rgba, offsetX, offsetY);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
