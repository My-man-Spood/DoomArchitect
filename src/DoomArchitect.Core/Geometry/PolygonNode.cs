namespace DoomArchitect.Core.Geometry;

/// <summary>
/// A node in the tree built by <see cref="PolygonNesting"/>: a loop plus
/// whichever other loops sit directly nested inside it. Whether a node is
/// an outer boundary or a hole is never stored separately - it's always
/// exactly <see cref="Loop.IsClockwise"/>, since a hole only ever nests
/// under a clockwise loop and vice versa.
/// </summary>
public sealed class PolygonNode
{
    internal PolygonNode(Loop loop)
    {
        Loop = loop;
    }

    public Loop Loop { get; }
    public List<PolygonNode> Children { get; } = new();
}
