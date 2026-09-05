using System.Numerics;

namespace DoomArchitect.Core.Map;

public sealed class Vertex
{
    private readonly List<Linedef> _linedefs = new();
    private readonly Dictionary<string, object> _customFields = new();

    internal Vertex(Vector2 position)
    {
        Position = position;
    }

    public Vector2 Position { get; internal set; }

    public IReadOnlyList<Linedef> Linedefs => _linedefs;

    /// <summary>
    /// UDMF fields recognized by the format but not modeled as a typed
    /// property here (e.g. <c>zceiling</c>/<c>zfloor</c>) - preserved so a
    /// load-then-save round-trip doesn't lose them, even though nothing
    /// can interpret or edit them yet.
    /// </summary>
    public IReadOnlyDictionary<string, object> CustomFields => _customFields;

    internal void AddLinedef(Linedef linedef) => _linedefs.Add(linedef);

    internal void RemoveLinedef(Linedef linedef) => _linedefs.Remove(linedef);

    internal void SetCustomField(string key, object value) => _customFields[key] = value;
}
