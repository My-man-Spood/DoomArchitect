namespace DoomArchitect.Core.Textures;

/// <summary>
/// The 256-color palette used to turn indexed pixel data (patches, flats)
/// into RGB. Matches UDB's own <c>Playpal</c> exactly: only ever reads the
/// first of PLAYPAL's 14 palettes (the base game palette) and ignores the
/// rest (berserk/pain-flash/radiation-suit variants, etc.) - an editor
/// preview has no notion of those in-game effects. A missing PLAYPAL lump
/// falls back to a flat gray (127,127,127) palette, matching UDB's own
/// fallback exactly.
/// </summary>
public sealed class Playpal
{
    private const int ColorCount = 256;

    private readonly (byte R, byte G, byte B)[] _colors;

    private Playpal((byte R, byte G, byte B)[] colors)
    {
        _colors = colors;
    }

    public (byte R, byte G, byte B) this[int index] => _colors[index];

    public static Playpal Read(byte[] data)
    {
        var colors = new (byte, byte, byte)[ColorCount];
        using var reader = new BinaryReader(new MemoryStream(data));
        for (var i = 0; i < ColorCount; i++)
        {
            colors[i] = (reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
        }

        return new Playpal(colors);
    }

    public static Playpal CreateFallback()
    {
        var colors = new (byte, byte, byte)[ColorCount];
        Array.Fill(colors, ((byte)127, (byte)127, (byte)127));
        return new Playpal(colors);
    }
}
