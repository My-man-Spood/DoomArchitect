namespace DoomArchitect.Core.Map;

public sealed class Sector
{
    internal Sector(double floorHeight, double ceilingHeight)
    {
        FloorHeight = floorHeight;
        CeilingHeight = ceilingHeight;
        NeedsRebuild = true;
    }

    public double FloorHeight { get; set; }
    public double CeilingHeight { get; set; }
    public string FloorTexture { get; set; } = "-";
    public string CeilingTexture { get; set; } = "-";
    public int Brightness { get; set; } = 160;

    /// <summary>
    /// Set whenever this sector's geometry or appearance changes such that
    /// its rendered mesh is stale. Cleared by the rendering layer once it
    /// has rebuilt the mesh; Core never clears it on its own.
    /// </summary>
    public bool NeedsRebuild { get; internal set; }
}
