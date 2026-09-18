using System.Linq;
using System.Numerics;
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

    public bool IsClockwise => PolygonWinding.IsClockwise(Positions());

    /// <summary>
    /// Even-odd rule, ray cast to the right from the point. Only correct
    /// for this loop taken in isolation - a sector with holes needs
    /// <see cref="SectorHitTest"/>, which sums this across every one of a
    /// sector's traced loops.
    /// </summary>
    public bool Contains(Vector2 point)
    {
        var inside = false;
        var j = Vertices.Count - 1;

        for (var i = 0; i < Vertices.Count; i++)
        {
            var vi = Vertices[i].Position;
            var vj = Vertices[j].Position;

            if (vi.Y != vj.Y
                && point.Y > MathF.Min(vi.Y, vj.Y)
                && point.Y <= MathF.Max(vi.Y, vj.Y)
                && (point.X < MathF.Min(vi.X, vj.X)
                    || (point.X <= MathF.Max(vi.X, vj.X)
                        && (vi.X == vj.X || point.X <= (point.Y - vi.Y) * (vj.X - vi.X) / (vj.Y - vi.Y) + vi.X))))
            {
                inside = !inside;
            }

            j = i;
        }

        return inside;
    }

    private Vector2[] Positions() => Vertices.Select(v => v.Position).ToArray();
}
