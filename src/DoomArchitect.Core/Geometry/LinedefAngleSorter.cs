using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Geometry;

/// <summary>
/// Ported from UDB's real <c>LinedefAngleSorter</c> - the same formula
/// <see cref="SectorTracer"/>'s own already-tested <c>RelativeAngle</c>
/// uses (that one operates on <see cref="Sidedef"/>, which always belongs
/// to an already-built <see cref="Sector"/>; this one operates on the
/// more general <see cref="LinedefSide"/>, which doesn't require one -
/// <see cref="BoundaryTracer"/> needs to walk linedefs regardless of
/// whether either side has a real sector yet). Reusing the identical,
/// already-verified formula here rather than re-deriving it independently
/// - the rotational sense this produces is what makes a boundary trace
/// consistently turn the same way at every branch, and getting it subtly
/// wrong silently produces a broken (self-crossing or wrong-loop) trace.
/// </summary>
public static class LinedefAngleSorter
{
    /// <summary>
    /// Orders candidates so the one that best continues the loop just
    /// walked (largest angle relative to the side just arrived on) sorts
    /// first.
    /// </summary>
    public static void SortByRelativeAngleDescending(List<LinedefSide> candidates, LinedefSide baseSide, Vertex baseVertex)
    {
        candidates.Sort((x, y) =>
            RelativeAngle(baseSide, y, baseVertex).CompareTo(RelativeAngle(baseSide, x, baseVertex)));
    }

    public static float RelativeAngle(LinedefSide baseSide, LinedefSide candidate, Vertex baseVertex)
    {
        var baseLine = baseSide.Linedef;
        var candidateLine = candidate.Linedef;

        var baseAngle = GeometryMath.Angle(baseLine.Start.Position, baseLine.End.Position);
        if (baseLine.End == baseVertex) baseAngle += MathF.PI;

        var candidateAngle = GeometryMath.Angle(candidateLine.Start.Position, candidateLine.End.Position);
        if (candidateLine.End == baseVertex) candidateAngle += MathF.PI;

        var n = GeometryMath.AngleDifference(baseAngle, candidateAngle);

        var baseFar = baseLine.Start == baseVertex ? baseLine.End.Position : baseLine.Start.Position;
        var candidateFar = candidateLine.Start == baseVertex ? candidateLine.End.Position : candidateLine.Start.Position;

        var dir = baseSide.Front;
        if (baseLine.End == baseVertex) dir = !dir;

        var s = GeometryMath.SideOfLine(baseFar, candidateFar, baseVertex.Position);
        if (s < 0 && dir) n = MathF.PI * 2f - n;
        if (s > 0 && !dir) n = MathF.PI * 2f - n;

        return n;
    }
}
