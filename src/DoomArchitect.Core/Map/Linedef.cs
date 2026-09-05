namespace DoomArchitect.Core.Map;

public sealed class Linedef
{
    private readonly Dictionary<string, object> _customFields = new();

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
    /// UDMF fields recognized by the format but not modeled as a typed
    /// property here (e.g. <c>special</c>/<c>arg0..arg4</c>/flags/tags) -
    /// preserved so a load-then-save round-trip doesn't lose them, even
    /// though nothing can interpret or edit them yet.
    /// </summary>
    public IReadOnlyDictionary<string, object> CustomFields => _customFields;

    /// <summary>
    /// Dirties the sector(s) on this linedef's own sides - at most two,
    /// which is the entire blast radius of moving one of its endpoints.
    /// </summary>
    internal void MarkAdjacentSectorsDirty()
    {
        if (Front != null) Front.Sector.NeedsRebuild = true;
        if (Back != null) Back.Sector.NeedsRebuild = true;
    }

    internal void SetCustomField(string key, object value) => _customFields[key] = value;
}
