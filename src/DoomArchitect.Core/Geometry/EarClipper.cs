using System.Numerics;

namespace DoomArchitect.Core.Geometry;

/// <summary>
/// Ported from Ultimate Doom Builder's ear-clipping step in
/// <c>Triangulation.DoEarClip</c>/<c>EarClipVertex</c>: clips ears off a
/// simple (hole-free) polygon until only triangles remain. Tracks each
/// vertex's convex/reflex/ear-tip status incrementally as vertices are
/// removed - after an ear is clipped, only its two former neighbors are
/// re-evaluated, not the whole remaining polygon - matching UDB's actual
/// algorithm rather than recomputing everything from scratch each
/// iteration, since a large enough sector polygon would make that
/// difference real.
/// </summary>
public static class EarClipper
{
    private sealed class ClipVertex
    {
        public Vector2 Position;
        public LinkedListNode<ClipVertex>? VertsNode;
        public LinkedListNode<ClipVertex>? ReflexNode;
        public LinkedListNode<ClipVertex>? EarTipNode;

        public bool IsReflex => ReflexNode != null;
    }

    public static IReadOnlyList<(Vector2 A, Vector2 B, Vector2 C)> Clip(IReadOnlyList<Vector2> polygon)
    {
        var verts = BuildVertexList(polygon);
        RemoveZeroLengthEdges(verts);
        RemoveCollinearVertices(verts);

        var reflexes = new LinkedList<ClipVertex>();
        var eartips = new LinkedList<ClipVertex>();
        var triangles = new List<(Vector2, Vector2, Vector2)>();

        foreach (var vertex in verts)
            if (IsReflex(vertex)) AddReflex(vertex, reflexes);

        foreach (var vertex in verts)
            if (!vertex.IsReflex && IsValidEar(vertex, reflexes)) AddEarTip(vertex, eartips);

        while (eartips.Count > 0 && verts.Count > 2)
        {
            var vertex = eartips.First!.Value;
            var (a, b, c) = GetTriangle(vertex);

            if (HasArea(a, b, c)) triangles.Add((a, b, c));

            var prev = Neighbor(vertex, before: true);
            var next = Neighbor(vertex, before: false);

            Remove(vertex, verts, eartips);

            Restatus(prev, reflexes, eartips);
            Restatus(next, reflexes, eartips);
        }

        return triangles;
    }

    private static LinkedList<ClipVertex> BuildVertexList(IReadOnlyList<Vector2> polygon)
    {
        var list = new LinkedList<ClipVertex>();
        foreach (var position in polygon)
        {
            var vertex = new ClipVertex { Position = position };
            vertex.VertsNode = list.AddLast(vertex);
        }
        return list;
    }

    private static void RemoveZeroLengthEdges(LinkedList<ClipVertex> verts)
    {
        var n1 = verts.First;
        if (n1 == null) return;

        do
        {
            var n2 = n1.Next ?? verts.First!;
            while (n1 != n2 && IsNearlySamePosition(n1.Value.Position, n2.Value.Position))
            {
                var toRemove = n2;
                n2 = n2.Next ?? verts.First!;
                verts.Remove(toRemove);
            }
            n1 = n2;
        }
        while (n1 != verts.First);
    }

    private static bool IsNearlySamePosition(Vector2 a, Vector2 b) =>
        MathF.Abs(a.X - b.X) < 0.00001f && MathF.Abs(a.Y - b.Y) < 0.00001f;

    // A vertex whose two edges point in the same direction is a straight
    // 180-degree "corner" that contributes nothing to the polygon's shape
    // - harmless to keep, but routine here, since a bridge point is
    // deliberately collinear with the outer edge it was spliced into.
    private static void RemoveCollinearVertices(LinkedList<ClipVertex> verts)
    {
        var n1 = verts.First;
        while (n1 != null)
        {
            var n2 = n1.Next;
            var (a, b, c) = GetTriangle(n1.Value);

            if (GeometryMath.AngleDifference(GeometryMath.Angle(a, b), GeometryMath.Angle(b, c)) < 0.00001f)
                verts.Remove(n1);

            n1 = n2;
        }
    }

    private static (Vector2 A, Vector2 B, Vector2 C) GetTriangle(ClipVertex vertex) =>
        (Neighbor(vertex, before: true).Position, vertex.Position, Neighbor(vertex, before: false).Position);

    private static ClipVertex Neighbor(ClipVertex vertex, bool before)
    {
        var node = vertex.VertsNode!;
        return (before ? node.Previous ?? node.List!.Last : node.Next ?? node.List!.First)!.Value;
    }

    private static bool IsReflex(ClipVertex vertex)
    {
        var (a, b, c) = GetTriangle(vertex);
        return GeometryMath.SideOfLine(a, c, b) < 0f;
    }

    private static bool HasArea(Vector2 a, Vector2 b, Vector2 c) =>
        a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y) != 0f;

    private static void AddReflex(ClipVertex vertex, LinkedList<ClipVertex> reflexes)
    {
        vertex.ReflexNode ??= reflexes.AddLast(vertex);
    }

    private static void RemoveReflex(ClipVertex vertex)
    {
        if (vertex.ReflexNode == null) return;
        vertex.ReflexNode.List!.Remove(vertex.ReflexNode);
        vertex.ReflexNode = null;
    }

    private static void AddEarTip(ClipVertex vertex, LinkedList<ClipVertex> eartips)
    {
        vertex.EarTipNode ??= eartips.AddLast(vertex);
    }

    private static void RemoveEarTip(ClipVertex vertex)
    {
        if (vertex.EarTipNode == null) return;
        vertex.EarTipNode.List!.Remove(vertex.EarTipNode);
        vertex.EarTipNode = null;
    }

    private static void Remove(ClipVertex vertex, LinkedList<ClipVertex> verts, LinkedList<ClipVertex> eartips)
    {
        verts.Remove(vertex.VertsNode!);
        RemoveReflex(vertex);
        RemoveEarTip(vertex);
    }

    /// <summary>Re-evaluates a vertex's reflex/ear-tip status after a neighboring ear was clipped.</summary>
    private static void Restatus(ClipVertex vertex, LinkedList<ClipVertex> reflexes, LinkedList<ClipVertex> eartips)
    {
        if (IsReflex(vertex))
        {
            AddReflex(vertex, reflexes);
            RemoveEarTip(vertex);
        }
        else
        {
            RemoveReflex(vertex);
        }

        if (!vertex.IsReflex && IsValidEar(vertex, reflexes))
            AddEarTip(vertex, eartips);
        else
            RemoveEarTip(vertex);
    }

    private static bool IsValidEar(ClipVertex vertex, LinkedList<ClipVertex> reflexes)
    {
        var (a, b, c) = GetTriangle(vertex);
        if (!HasArea(a, b, c)) return true;

        foreach (var reflex in reflexes)
        {
            var p = reflex.Position;

            // Position-based, matching UDB exactly: a reflex vertex that
            // happens to share a position with one of this triangle's
            // corners (routine here, again from bridge points) is treated
            // as a corner, not a real containment case.
            if (p == a || p == b || p == c) continue;

            if (p.X < MathF.Min(a.X, MathF.Min(b.X, c.X)) || p.X > MathF.Max(a.X, MathF.Max(b.X, c.X)) ||
                p.Y < MathF.Min(a.Y, MathF.Min(b.Y, c.Y)) || p.Y > MathF.Max(a.Y, MathF.Max(b.Y, c.Y)))
                continue;

            var side01 = GeometryMath.SideOfLine(a, b, p);
            var side12 = GeometryMath.SideOfLine(b, c, p);
            var side20 = GeometryMath.SideOfLine(c, a, p);

            if (side01 == 0f || side12 == 0f || side20 == 0f)
            {
                var onLine = side01 == 0f ? GeometryMath.NearestOnLine(a, b, p)
                    : side12 == 0f ? GeometryMath.NearestOnLine(b, c, p)
                    : GeometryMath.NearestOnLine(c, a, p);

                if (onLine < 0f || onLine > 1f) continue;

                var prevPosition = Neighbor(reflex, before: true).Position;
                var nextPosition = Neighbor(reflex, before: false).Position;

                if (LineInsideTriangle(a, b, c, p, prevPosition)) return false;
                if (LineInsideTriangle(a, b, c, p, nextPosition)) return false;

                continue;
            }

            if (side01 < 0f && side12 < 0f && side20 < 0f) return false;
        }

        return true;
    }

    private static bool LineInsideTriangle(Vector2 a, Vector2 b, Vector2 c, Vector2 p1, Vector2 p2)
    {
        var side01 = GeometryMath.SideOfLine(a, b, p2);
        var side12 = GeometryMath.SideOfLine(b, c, p2);
        var side20 = GeometryMath.SideOfLine(c, a, p2);

        if (side01 < 0f && side12 < 0f && side20 < 0f) return true;

        var p2OnEdge = 2f;
        var p1OnSameEdge = 2f;

        if (side01 == 0f)
        {
            p2OnEdge = GeometryMath.NearestOnLine(a, b, p2);
            p1OnSameEdge = GeometryMath.SideOfLine(a, b, p1);
        }
        else if (side12 == 0f)
        {
            p2OnEdge = GeometryMath.NearestOnLine(b, c, p2);
            p1OnSameEdge = GeometryMath.SideOfLine(b, c, p1);
        }
        else if (side20 == 0f)
        {
            p2OnEdge = GeometryMath.NearestOnLine(c, a, p2);
            p1OnSameEdge = GeometryMath.SideOfLine(c, a, p1);
        }

        if (p2OnEdge >= 0f && p2OnEdge <= 1f && p1OnSameEdge == 0f) return false;

        if (GeometryMath.SegmentsIntersect(a, b, p1, p2)) return true;
        if (GeometryMath.SegmentsIntersect(b, c, p1, p2)) return true;
        if (GeometryMath.SegmentsIntersect(c, a, p1, p2)) return true;

        return false;
    }
}
