namespace DoomArchitect.Core.Geometry;

/// <summary>
/// Ported from Ultimate Doom Builder's <c>EarClipPolygon.InsertChild</c>/
/// <c>Intersect</c>: arranges a sector's traced loops into a tree, where
/// each loop's children are whichever other loops sit directly nested
/// inside it. Nesting can go arbitrarily deep - a hole can itself contain
/// a separate outer boundary (e.g. an island floating inside a ring-shaped
/// sector's hole).
/// </summary>
public static class PolygonNesting
{
    public static IReadOnlyList<PolygonNode> BuildTree(IReadOnlyList<Loop> loops)
    {
        var roots = new List<PolygonNode>();

        foreach (var loop in loops)
        {
            var node = new PolygonNode(loop);
            if (!TryInsert(roots, node)) roots.Add(node);
        }

        return roots;
    }

    private static bool TryInsert(List<PolygonNode> nodes, PolygonNode candidate)
    {
        foreach (var node in nodes)
        {
            if (TryInsertChild(node, candidate)) return true;
        }

        return false;
    }

    private static bool TryInsertChild(PolygonNode node, PolygonNode candidate)
    {
        // Try to nest deeper before considering this node itself, so a
        // candidate always ends up as a direct child of its innermost
        // enclosing loop, not the first one found.
        foreach (var child in node.Children)
        {
            if (TryInsertChild(child, candidate)) return true;
        }

        if (node.Loop.Contains(candidate.Loop.Vertices[0].Position))
        {
            node.Children.Add(candidate);
            return true;
        }

        return false;
    }
}
