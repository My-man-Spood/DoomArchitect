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

    public bool IsFront => Linedef.Front == this;
}
