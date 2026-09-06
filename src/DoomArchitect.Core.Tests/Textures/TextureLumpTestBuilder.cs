using System.Text;

namespace DoomArchitect.Core.Tests.Textures;

/// <summary>Assembles fake PLAYPAL/PNAMES/TEXTURE1/patch/flat lump byte buffers by hand, for tests.</summary>
internal static class TextureLumpTestBuilder
{
    public static byte[] Playpal(params (byte R, byte G, byte B)[] colors)
    {
        var data = new byte[768];
        for (var i = 0; i < colors.Length && i < 256; i++)
        {
            data[i * 3] = colors[i].R;
            data[i * 3 + 1] = colors[i].G;
            data[i * 3 + 2] = colors[i].B;
        }

        return data;
    }

    public static byte[] Flat(int width, int height, byte fillIndex)
    {
        var data = new byte[width * height];
        Array.Fill(data, fillIndex);
        return data;
    }

    /// <summary>
    /// Builds a patch_t lump: one post list per column, in file order. An
    /// empty column's post list is legal (immediate 0xFF terminator).
    /// </summary>
    public static byte[] Patch(short height, params (byte TopDelta, byte[] Pixels)[][] columns)
    {
        var width = (short)columns.Length;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(width);
        writer.Write(height);
        writer.Write((short)0); // offsetx
        writer.Write((short)0); // offsety

        var offsetTablePosition = stream.Position;
        for (var i = 0; i < width; i++) writer.Write(0); // placeholder column offsets

        var offsets = new int[width];
        for (var c = 0; c < columns.Length; c++)
        {
            offsets[c] = (int)stream.Position;
            foreach (var post in columns[c])
            {
                writer.Write(post.TopDelta);
                writer.Write((byte)post.Pixels.Length);
                writer.Write((byte)0); // padding before pixel data
                writer.Write(post.Pixels);
                writer.Write((byte)0); // padding after pixel data
            }

            writer.Write((byte)255); // terminator
        }

        writer.Flush();
        stream.Position = offsetTablePosition;
        foreach (var offset in offsets) writer.Write(offset);

        writer.Flush();
        return stream.ToArray();
    }

    public static byte[] PatchNames(params string[] names)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(names.Length);
        foreach (var name in names) writer.Write(PaddedName(name));
        return stream.ToArray();
    }

    public sealed record TexturePatchDef(short OriginX, short OriginY, ushort PatchIndex);

    public sealed record TextureEntryDef(
        string Name, short Width, short Height, IReadOnlyList<TexturePatchDef> Patches, bool Strife = false);

    /// <summary>
    /// Builds a TEXTURE1/TEXTURE2 lump. When <paramref name="sequential"/>
    /// is false, entries are physically written to the lump in reverse
    /// order while the offset table still points at the correct location
    /// for each - proving a reader that actually seeks with the stored
    /// offsets (rather than assuming sequential layout) reads it
    /// correctly regardless.
    /// </summary>
    public static byte[] TextureDefinitions(IReadOnlyList<TextureEntryDef> entries, bool sequential = true)
    {
        var blobs = entries.Select(BuildEntryBlob).ToList();

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(entries.Count);
        var offsetTablePosition = stream.Position;
        for (var i = 0; i < entries.Count; i++) writer.Write(0); // placeholder offsets

        var offsets = new int[entries.Count];
        var writeOrder = sequential ? Enumerable.Range(0, blobs.Count) : Enumerable.Range(0, blobs.Count).Reverse();
        foreach (var i in writeOrder)
        {
            offsets[i] = (int)stream.Position;
            writer.Write(blobs[i]);
        }

        writer.Flush();
        stream.Position = offsetTablePosition;
        foreach (var offset in offsets) writer.Write(offset);

        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildEntryBlob(TextureEntryDef entry)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(PaddedName(entry.Name));
        writer.Write((ushort)0); // flags
        writer.Write((byte)0); // scalebytex
        writer.Write((byte)0); // scalebytey
        writer.Write(entry.Width);
        writer.Write(entry.Height);

        if (entry.Strife)
        {
            writer.Write((short)entry.Patches.Count);
        }
        else
        {
            writer.Write((short)0); // read as the (fake) Strife patch count, 0 signals Doom format
            writer.Write((short)0); // remainder of Doom's unused columndirectory field
            writer.Write((short)entry.Patches.Count); // the real patch count
        }

        foreach (var patch in entry.Patches)
        {
            writer.Write(patch.OriginX);
            writer.Write(patch.OriginY);
            writer.Write(patch.PatchIndex);
            if (!entry.Strife)
            {
                writer.Write((short)0); // stepdir
                writer.Write((short)0); // colormap
            }
        }

        return stream.ToArray();
    }

    private static byte[] PaddedName(string name)
    {
        var bytes = new byte[8];
        var nameBytes = Encoding.ASCII.GetBytes(name);
        Array.Copy(nameBytes, bytes, Math.Min(nameBytes.Length, 8));
        return bytes;
    }
}
