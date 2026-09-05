using System.Numerics;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Undo;

public sealed class MoveVertexCommand : ICommand
{
    private readonly MapData map;
    private readonly Vertex vertex;
    private readonly Vector2 from;
    private readonly Vector2 to;

    public MoveVertexCommand(MapData map, Vertex vertex, Vector2 from, Vector2 to)
    {
        this.map = map;
        this.vertex = vertex;
        this.from = from;
        this.to = to;
    }

    public void Do() => map.MoveVertex(vertex, to);

    public void Undo() => map.MoveVertex(vertex, from);
}
