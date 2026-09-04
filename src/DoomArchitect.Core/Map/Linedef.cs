namespace DoomArchitect.Core.Map;

public sealed class Linedef
{
    internal Linedef(Vertex start, Vertex end)
    {
        Start = start;
        End = end;
    }

    public Vertex Start { get; internal set; }
    public Vertex End { get; internal set; }

    /// <summary>Null on a one-sided linedef (a wall with nothing behind it).</summary>
    public Sidedef? Front { get; internal set; }

    /// <summary>Null on a one-sided linedef (a wall with nothing behind it).</summary>
    public Sidedef? Back { get; internal set; }

    /// <summary>
    /// Dirties the sector(s) on this linedef's own sides - at most two,
    /// which is the entire blast radius of moving one of its endpoints.
    /// </summary>
    internal void MarkAdjacentSectorsDirty()
    {
        if (Front != null) Front.Sector.NeedsRebuild = true;
        if (Back != null) Back.Sector.NeedsRebuild = true;
    }
}
