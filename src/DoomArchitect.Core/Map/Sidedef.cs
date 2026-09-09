namespace DoomArchitect.Core.Map;

public sealed class Sidedef
{
    internal Sidedef(Sector sector, Linedef linedef)
    {
        Sector = sector;
        Linedef = linedef;
    }

    public Sector Sector { get; }
    public Linedef Linedef { get; }
    public string UpperTexture { get; set; } = "-";
    public string MiddleTexture { get; set; } = "-";
    public string LowerTexture { get; set; } = "-";
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }

    public bool IsFront => Linedef.Front == this;

    /// <summary>
    /// UDMF fields recognized by the format but not modeled as a typed
    /// property here (e.g. flags) - preserved so a load-then-save
    /// round-trip doesn't lose them, even though nothing can interpret or
    /// edit them yet.
    /// </summary>
    public UniFields Fields { get; } = new();
}
