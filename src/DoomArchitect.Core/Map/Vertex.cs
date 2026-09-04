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

    internal void AddLinedef(Linedef linedef) => _linedefs.Add(linedef);

    internal void RemoveLinedef(Linedef linedef) => _linedefs.Remove(linedef);
}
