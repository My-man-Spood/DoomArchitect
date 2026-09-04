namespace DoomArchitect.Core.Map;

public sealed class Sector
{
    private readonly List<Sidedef> _sidedefs = new();

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

    public IReadOnlyList<Sidedef> Sidedefs => _sidedefs;

    /// <summary>
    /// Set whenever this sector's geometry or appearance changes such that
    /// its rendered mesh is stale. Cleared by the rendering layer once it
    /// has rebuilt the mesh; Core never clears it on its own.
    /// </summary>
    public bool NeedsRebuild { get; internal set; }

    internal void AddSidedef(Sidedef sidedef) => _sidedefs.Add(sidedef);

    internal void RemoveSidedef(Sidedef sidedef) => _sidedefs.Remove(sidedef);
}
