namespace DoomArchitect.Core.Map;

public sealed class Sidedef
{
    internal Sidedef(Sector sector, Linedef linedef)
    {
        Sector = sector;
        Linedef = linedef;
    }

    /// <summary>
    /// Settable (not just constructor-assigned) so <see cref="MapData.AttachOrRetargetSidedef"/>
    /// can re-point an already-existing sidedef at a different sector -
    /// UDB's own real <c>JoinSector</c> behavior when a linedef being
    /// joined already has a sidedef on the target side (e.g. a linedef
    /// shared between the map's own void-side and a newly drawn sector
    /// that turns out to actually border an existing one). Caller is
    /// responsible for the matching <see cref="Sector.RemoveSidedef"/>/
    /// <see cref="Sector.AddSidedef"/> bookkeeping - this alone doesn't
    /// touch either sector's own sidedef list.
    /// </summary>
    public Sector Sector { get; internal set; }

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
