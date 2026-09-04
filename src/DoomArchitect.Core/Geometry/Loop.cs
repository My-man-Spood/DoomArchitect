using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Geometry;

/// <summary>
/// A closed boundary loop derived from tracing a sector's sidedefs. A
/// clockwise loop is an outer boundary; a counter-clockwise loop is a hole
/// nested inside another loop of the same sector.
/// </summary>
public sealed class Loop
{
    internal Loop(IReadOnlyList<Vertex> vertices)
    {
        Vertices = vertices;
    }

    public IReadOnlyList<Vertex> Vertices { get; }

    public bool IsClockwise => SignedArea() < 0;

    private float SignedArea()
    {
        var sum = 0f;
        for (var i = 0; i < Vertices.Count; i++)
        {
            var a = Vertices[i].Position;
            var b = Vertices[(i + 1) % Vertices.Count].Position;
            sum += a.X * b.Y - b.X * a.Y;
        }
        return sum * 0.5f;
    }
}
