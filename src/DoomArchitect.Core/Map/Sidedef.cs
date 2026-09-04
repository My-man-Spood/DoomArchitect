namespace DoomArchitect.Core.Map;

public sealed class Sidedef
{
    internal Sidedef(Sector sector)
    {
        Sector = sector;
    }

    public Sector Sector { get; }
    public string UpperTexture { get; set; } = "-";
    public string MiddleTexture { get; set; } = "-";
    public string LowerTexture { get; set; } = "-";
}
