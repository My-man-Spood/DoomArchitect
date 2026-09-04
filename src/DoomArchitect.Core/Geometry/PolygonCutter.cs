using System.Numerics;

namespace DoomArchitect.Core.Geometry;

/// <summary>
/// Ported from Ultimate Doom Builder's <c>Triangulation.DoCutting</c>/
/// <c>MergeInnerPolys</c>/<c>SplitOuterWithInner</c>: walks a
/// <see cref="PolygonNesting"/> tree and bridges each hole into its
/// enclosing outer polygon via a rightward ray cast, producing a flat
/// list of hole-free polygons ready for ear-clipping. A hole's own
/// children (an island floating inside it) are promoted back to
/// independent top-level polygons rather than merged - they get their own
/// pass. Includes UDB's tie-break rules for a ray that grazes a perfectly
/// horizontal edge, or lands where a previous cut already sits on the
/// same horizontal.
/// </summary>
public static class PolygonCutter
{
    public static IReadOnlyList<IReadOnlyList<Vector2>> Cut(IReadOnlyList<PolygonNode> tree)
    {
        var todo = new Queue<PolygonNode>(tree);
        var results = new List<IReadOnlyList<Vector2>>();

        while (todo.Count > 0)
        {
            var node = todo.Dequeue();

            if (node.Children.Count == 0)
            {
                results.Add(Positions(node.Loop));
                continue;
            }

            var outer = new LinkedList<Vector2>(Positions(node.Loop));
            var holes = new List<Loop>(node.Children.Count);

            foreach (var hole in node.Children)
            {
                holes.Add(hole.Loop);
                foreach (var grandchild in hole.Children) todo.Enqueue(grandchild);
            }

            while (holes.Count > 0)
            {
                var index = IndexOfRightmostOwner(holes);
                var hole = holes[index];
                holes.RemoveAt(index);
                Bridge(outer, hole);
            }

            results.Add(outer.ToArray());
        }

        return results;
    }

    private static Vector2[] Positions(Loop loop)
    {
        var positions = new Vector2[loop.Vertices.Count];
        for (var i = 0; i < positions.Length; i++) positions[i] = loop.Vertices[i].Position;
        return positions;
    }

    private static int IndexOfRightmostOwner(List<Loop> loops)
    {
        var best = 0;
        var bestX = RightmostX(loops[0]);
        for (var i = 1; i < loops.Count; i++)
        {
            var x = RightmostX(loops[i]);
            if (x > bestX)
            {
                bestX = x;
                best = i;
            }
        }
        return best;
    }

    private static float RightmostX(Loop loop)
    {
        var best = loop.Vertices[0].Position.X;
        foreach (var v in loop.Vertices)
            if (v.Position.X > best) best = v.Position.X;
        return best;
    }

    private static void Bridge(LinkedList<Vector2> outer, Loop hole)
    {
        var holePositions = Positions(hole);
        var startIndex = IndexOfRightmost(holePositions);
        var start = holePositions[startIndex];

        var (insertBefore, cutPosition) = FindCut(outer, start);

        outer.AddBefore(insertBefore, cutPosition);

        for (var i = 0; i < holePositions.Length; i++)
        {
            outer.AddBefore(insertBefore, holePositions[(startIndex + i) % holePositions.Length]);
        }

        outer.AddBefore(insertBefore, start);

        if (cutPosition != insertBefore.Value)
        {
            outer.AddBefore(insertBefore, cutPosition);
        }
    }

    private static int IndexOfRightmost(Vector2[] positions)
    {
        var best = 0;
        for (var i = 1; i < positions.Length; i++)
            if (positions[i].X > positions[best].X) best = i;
        return best;
    }

    /// <summary>
    /// Casts a ray from <paramref name="start"/> to the right and finds
    /// the nearest edge of <paramref name="outer"/> it crosses, returning
    /// the node to splice the hole in before and the exact crossing
    /// point. Compares candidates using the ray's own parameter (0 at
    /// start, 1 at the ray's far end) rather than raw X, so a normal
    /// crossing and a horizontal-edge special case below stay on the same
    /// scale.
    /// </summary>
    private static (LinkedListNode<Vector2> InsertBefore, Vector2 Position) FindCut(LinkedList<Vector2> outer, Vector2 start)
    {
        var nodes = new List<LinkedListNode<Vector2>>(outer.Count);
        for (var n = outer.First; n != null; n = n.Next) nodes.Add(n);

        var rightmostX = nodes[0].Value.X;
        for (var i = 1; i < nodes.Count; i++)
            if (nodes[i].Value.X > rightmostX) rightmostX = nodes[i].Value.X;

        var rayEnd = new Vector2(rightmostX + 10f, start.Y);

        // A tiny nudge (0.1 world units, converted to ray-parameter space)
        // used below to break a tie between two horizontal candidates.
        var bonus = GeometryMath.NearestOnLine(start, rayEnd, new Vector2(start.X + 0.1f, start.Y));

        LinkedListNode<Vector2>? bestNode = null;
        var bestU = float.MaxValue;
        var bestPosition = default(Vector2);

        for (var i = 0; i < nodes.Count; i++)
        {
            var v1 = nodes[i];
            var v2 = nodes[(i + 1) % nodes.Count];
            var p1 = v1.Value;
            var p2 = v2.Value;

            if (p1.Y == p2.Y)
            {
                // Parallel to the ray. Only relevant if it lies exactly on
                // it - this happens routinely here, since a previous cut's
                // bridge is deliberately collinear with the outer edge it
                // was spliced into.
                if (p1.Y != start.Y) continue;

                var u1 = GeometryMath.NearestOnLine(start, rayEnd, p1);
                var u2 = GeometryMath.NearestOnLine(start, rayEnd, p2);
                if (u1 < 0f) u1 = float.MaxValue;
                if (u2 < 0f) u2 = float.MaxValue;

                var insertU = MathF.Min(u1, u2);
                var insertPosition = start + insertU * (rayEnd - start);

                if (p1.X > p2.X)
                {
                    // Runs right-to-left (back toward start) - the cut
                    // always goes after this edge. Prefer it slightly if
                    // what follows heads away from start's height, which
                    // reads as the more sensible of two otherwise-tied
                    // horizontal candidates.
                    var next = v2.Next ?? outer.First!;
                    if (next.Value.Y < p2.Y) insertU -= bonus;

                    if (insertU <= bestU)
                    {
                        bestNode = v2.Next ?? outer.First!;
                        bestU = insertU;
                        bestPosition = insertPosition;
                    }
                }
                else
                {
                    // Runs left-to-right (away from start) - the cut
                    // always goes before this edge.
                    var previous = v1.Previous ?? outer.Last!;
                    if (previous.Value.Y > p1.Y) insertU -= bonus;

                    if (insertU <= bestU)
                    {
                        bestNode = v2;
                        bestU = insertU;
                        bestPosition = insertPosition;
                    }
                }

                continue;
            }

            var s = (start.Y - p1.Y) / (p2.Y - p1.Y);
            if (s < 0f || s > 1f) continue;

            var x = p1.X + s * (p2.X - p1.X);
            var u = GeometryMath.NearestOnLine(start, rayEnd, new Vector2(x, start.Y));
            if (u <= 0f || u > bestU) continue;

            bestU = u;
            bestNode = v2;
            bestPosition = new Vector2(x, start.Y);
        }

        return (bestNode!, bestPosition);
    }
}
