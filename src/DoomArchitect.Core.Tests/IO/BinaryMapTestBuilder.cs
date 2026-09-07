using System.Text;

namespace DoomArchitect.Core.Tests.IO;

/// <summary>Assembles classic binary map lump records by hand, for tests - one method per record type.</summary>
internal static class BinaryMapTestBuilder
{
    public static byte[] Vertex(short x, short y)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(x);
        writer.Write(y);
        return stream.ToArray();
    }

    public static byte[] Sector(
        short floorHeight, short ceilingHeight, string floorTexture, string ceilingTexture,
        short brightness, ushort special = 0, ushort tag = 0)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(floorHeight);
        writer.Write(ceilingHeight);
        writer.Write(PaddedName(floorTexture));
        writer.Write(PaddedName(ceilingTexture));
        writer.Write(brightness);
        writer.Write(special);
        writer.Write(tag);
        return stream.ToArray();
    }

    public static byte[] Sidedef(
        short offsetX, short offsetY, string upperTexture, string lowerTexture, string middleTexture, ushort sector)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(offsetX);
        writer.Write(offsetY);
        writer.Write(PaddedName(upperTexture));
        writer.Write(PaddedName(lowerTexture));
        writer.Write(PaddedName(middleTexture));
        writer.Write(sector);
        return stream.ToArray();
    }

    public static byte[] Linedef(
        ushort v1, ushort v2, ushort flags, ushort special, ushort tag, ushort sidefront, ushort sideback)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(v1);
        writer.Write(v2);
        writer.Write(flags);
        writer.Write(special);
        writer.Write(tag);
        writer.Write(sidefront);
        writer.Write(sideback);
        return stream.ToArray();
    }

    public static byte[] Thing(short x, short y, short angle, short type, ushort flags)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(x);
        writer.Write(y);
        writer.Write(angle);
        writer.Write(type);
        writer.Write(flags);
        return stream.ToArray();
    }

    public static byte[] Concat(params byte[][] records) => records.SelectMany(r => r).ToArray();

    private static byte[] PaddedName(string name)
    {
        var bytes = new byte[8];
        var nameBytes = Encoding.ASCII.GetBytes(name);
        Array.Copy(nameBytes, bytes, Math.Min(nameBytes.Length, 8));
        return bytes;
    }
}
