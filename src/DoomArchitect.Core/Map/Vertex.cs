using System.Numerics;

namespace DoomArchitect.Core.Map;

public sealed class Vertex
{
    private readonly List<Linedef> _linedefs = new();

    internal Vertex(Vector2 position)
    {
        Position = position;
    }

    public Vector2 Position { get; internal set; }

    public IReadOnlyList<Linedef> Linedefs => _linedefs;

    /// <summary>
    /// Set whenever this vertex's selection state changes. Not undoable -
    /// selection is view state, not document state.
    /// </summary>
    public bool IsSelected { get; internal set; }

    /// <summary>
    /// UDMF fields recognized by the format but not modeled as a typed
    /// property here (e.g. <c>zceiling</c>/<c>zfloor</c>) - preserved so a
    /// load-then-save round-trip doesn't lose them, even though nothing
    /// can interpret or edit them yet.
    /// </summary>
    public UniFields Fields { get; } = new();

    internal void AddLinedef(Linedef linedef) => _linedefs.Add(linedef);

    internal void RemoveLinedef(Linedef linedef) => _linedefs.Remove(linedef);
}
